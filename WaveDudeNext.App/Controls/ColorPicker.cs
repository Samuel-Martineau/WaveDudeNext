// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WaveDudeNext.App.Converters;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// The colour picker inside a <see cref="ColorPickerButton"/>'s popup, upstream's in-house picker: a saturation/value
/// square under the pure hue, a hue strip, red, green and blue number boxes and the hex text, all editing the one
/// <see cref="Color"/>, which the owner binds two-way. Keyboard: the square and the strip are focusable; on the square
/// the arrow keys move saturation (left, right) and value (up, down) by 1/100, on the strip Up and Down move the hue by
/// a degree, ten with Shift; the mouse drags both markers. The HSV side keeps its hue for a grey (a grey has no hue)
/// and whenever the colour set from outside is what the current hue, saturation and value already give, so a drag
/// never jumps. The owner is told at most ten times a second: a change of hue, saturation or value moves the markers
/// and recolours the square at once, but <see cref="Color"/>, which sets off the scheme's change, the gradient bar, the
/// dirty flag and a re-render of both preview pictures, is committed after <see cref="CommitInterval"/> at most, and
/// once more when a drag ends; a colour set from outside cancels a pending commit (Escape puts the old colour back and
/// nothing may overwrite it). The control theme in Themes/Editors.axaml names PART_Square and PART_HueStrip (both
/// <see cref="Thumb"/>s, which capture the pointer by themselves and have an automation peer, so a harness sees which
/// one has the focus), PART_SquareMarker and PART_HueMarker inside them, placed from here on a Canvas, and the channel
/// boxes PART_Red, PART_Green, PART_Blue and the hex box PART_Hex, bound from here. UI thread only.
/// </summary>
public class ColorPicker : TemplatedControl
{
	private const string SquarePart = "PART_Square";
	private const string SquareMarkerPart = "PART_SquareMarker";
	private const string HueStripPart = "PART_HueStrip";
	private const string HueMarkerPart = "PART_HueMarker";

	/// <summary>Identifies the <see cref="Color"/> property.</summary>
	public static readonly StyledProperty<Rgb> ColorProperty =
		AvaloniaProperty.Register<ColorPicker, Rgb>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="Hue"/> property.</summary>
	public static readonly StyledProperty<double> HueProperty =
		AvaloniaProperty.Register<ColorPicker, double>(nameof(Hue));

	/// <summary>Identifies the <see cref="Saturation"/> property.</summary>
	public static readonly StyledProperty<double> SaturationProperty =
		AvaloniaProperty.Register<ColorPicker, double>(nameof(Saturation));

	/// <summary>Identifies the <see cref="Value"/> property.</summary>
	public static readonly StyledProperty<double> ValueProperty =
		AvaloniaProperty.Register<ColorPicker, double>(nameof(Value));

	/// <summary>Identifies the <see cref="Red"/> property.</summary>
	public static readonly StyledProperty<int> RedProperty =
		AvaloniaProperty.Register<ColorPicker, int>(nameof(Red));

	/// <summary>Identifies the <see cref="Green"/> property.</summary>
	public static readonly StyledProperty<int> GreenProperty =
		AvaloniaProperty.Register<ColorPicker, int>(nameof(Green));

	/// <summary>Identifies the <see cref="Blue"/> property.</summary>
	public static readonly StyledProperty<int> BlueProperty =
		AvaloniaProperty.Register<ColorPicker, int>(nameof(Blue));

	/// <summary>Identifies the <see cref="HueBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush> HueBrushProperty =
		AvaloniaProperty.Register<ColorPicker, IBrush>(nameof(HueBrush), Brushes.Red);

	/// <summary>The least time between two commits of <see cref="Color"/> to the owner while the picker is being worked, in milliseconds.</summary>
	public const int CommitInterval = 100;

	private readonly DispatcherTimer _commitTimer;
	private Thumb? _square, _strip;
	private Control? _squareMarker, _hueMarker;
	private bool _updating, _pending, _squareDown, _stripDown;
	private Rgb _pendingColor;
	private long _lastCommit;

	/// <summary>Builds the picker with its commit timer.</summary>
	public ColorPicker()
	{
		_commitTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(CommitInterval), DispatcherPriority.Background, (_, _) => Commit());
		_commitTimer.Stop();
	}

	/// <summary>The colour being edited; every other property is a view of it.</summary>
	public Rgb Color
	{
		get => GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	/// <summary>Hue in degrees, 0 to 360 (red at 0, green at 120, blue at 240).</summary>
	public double Hue
	{
		get => GetValue(HueProperty);
		set => SetValue(HueProperty, value);
	}

	/// <summary>Saturation 0 (grey) to 1 (the pure hue).</summary>
	public double Saturation
	{
		get => GetValue(SaturationProperty);
		set => SetValue(SaturationProperty, value);
	}

	/// <summary>Value (brightness) 0 (black) to 1.</summary>
	public double Value
	{
		get => GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>The red channel, 0 to 255.</summary>
	public int Red
	{
		get => GetValue(RedProperty);
		set => SetValue(RedProperty, value);
	}

	/// <summary>The green channel, 0 to 255.</summary>
	public int Green
	{
		get => GetValue(GreenProperty);
		set => SetValue(GreenProperty, value);
	}

	/// <summary>The blue channel, 0 to 255.</summary>
	public int Blue
	{
		get => GetValue(BlueProperty);
		set => SetValue(BlueProperty, value);
	}

	/// <summary>The pure hue at full saturation and value, the square's background.</summary>
	public IBrush HueBrush
	{
		get => GetValue(HueBrushProperty);
		set => SetValue(HueBrushProperty, value);
	}

	/// <summary>Puts the keyboard focus on the saturation/value square (the popup does this when it opens).</summary>
	public void FocusSquare() => _square?.Focus();

	/// <summary>Hands a pending colour to the owner now instead of at the timer's next tick; a test observes the result without waiting.</summary>
	public void CommitPending() => Commit();

	/// <inheritdoc />
	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);
		Detach(_square, OnSquarePressed, OnSquareMoved, OnSquareReleased);
		Detach(_strip, OnStripPressed, OnStripMoved, OnStripReleased);
		_square = e.NameScope.Find<Thumb>(SquarePart);
		_strip = e.NameScope.Find<Thumb>(HueStripPart);
		Attach(_square, OnSquarePressed, OnSquareMoved, OnSquareReleased);
		Attach(_strip, OnStripPressed, OnStripMoved, OnStripReleased);
		// the markers live in the thumbs' own templates, which have to be applied before they can be found
		_squareMarker = MarkerOf(_square, SquareMarkerPart);
		_hueMarker = MarkerOf(_strip, HueMarkerPart);
		BindChannel(e.NameScope.Find<NumberBox>("PART_Red"), RedProperty);
		BindChannel(e.NameScope.Find<NumberBox>("PART_Green"), GreenProperty);
		BindChannel(e.NameScope.Find<NumberBox>("PART_Blue"), BlueProperty);
		if (e.NameScope.Find<TextBox>("PART_Hex") is { } hex)
		{
			// #RRGGBB and back; the text is committed on Enter or when the box loses the focus, and text that does not parse yet changes nothing
			hex.Bind(TextBox.TextProperty, new ReflectionBinding(nameof(Color)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, Converter = new RgbTextConverter() });
		}
		ApplyColor(Color);
	}

	private void BindChannel(NumberBox? box, StyledProperty<int> channel)
	{
		if (box != null)
		{
			box.Bind(TextBox.TextProperty, new ReflectionBinding(channel.Name) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
		}
	}

	private static void Attach(Thumb? thumb, EventHandler<PointerPressedEventArgs> pressed, EventHandler<PointerEventArgs> moved, EventHandler<PointerReleasedEventArgs> released)
	{
		if (thumb == null)
		{
			return;
		}
		// tunnelling, so the position is taken before the thumb starts its own drag handling
		thumb.AddHandler(PointerPressedEvent, pressed, RoutingStrategies.Tunnel);
		thumb.AddHandler(PointerMovedEvent, moved, RoutingStrategies.Tunnel);
		thumb.AddHandler(PointerReleasedEvent, released, RoutingStrategies.Tunnel);
		thumb.SizeChanged += OnThumbSizeChanged;
	}

	private static void Detach(Thumb? thumb, EventHandler<PointerPressedEventArgs> pressed, EventHandler<PointerEventArgs> moved, EventHandler<PointerReleasedEventArgs> released)
	{
		if (thumb == null)
		{
			return;
		}
		thumb.RemoveHandler(PointerPressedEvent, pressed);
		thumb.RemoveHandler(PointerMovedEvent, moved);
		thumb.RemoveHandler(PointerReleasedEvent, released);
		thumb.SizeChanged -= OnThumbSizeChanged;
	}

	private static void OnThumbSizeChanged(object? sender, SizeChangedEventArgs e)
	{
		if (sender is Thumb thumb && thumb.FindAncestorOfType<ColorPicker>() is { } picker)
		{
			picker.UpdateMarkers();
		}
	}

	private static Control? MarkerOf(Thumb? thumb, string name)
	{
		if (thumb == null)
		{
			return null;
		}
		thumb.ApplyTemplate();
		return thumb.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
	}

	/// <inheritdoc />
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == ColorProperty)
		{
			ApplyColor(change.GetNewValue<Rgb>());
		}
		else if (change.Property == HueProperty || change.Property == SaturationProperty || change.Property == ValueProperty)
		{
			OnHsvChanged();
		}
		else if (change.Property == RedProperty || change.Property == GreenProperty || change.Property == BlueProperty)
		{
			OnChannelChanged();
		}
	}

	/// <summary>The colour changed from outside (the owner, the hex box or the channels): the other views follow.</summary>
	private void ApplyColor(Rgb color)
	{
		if (_updating)
		{
			return;
		}
		_commitTimer.Stop();
		_pending = false;
		_updating = true;
		try
		{
			Red = color.R;
			Green = color.G;
			Blue = color.B;
			if (Rgb.FromHsv(Hue, Saturation, Value) != color)
			{
				(double hue, double saturation, double value) = color.ToHsv();
				if (saturation > 0 && value > 0)
				{
					Hue = hue;
				}
				Saturation = saturation;
				Value = value;
				UpdateHueBrush();
			}
		}
		finally
		{
			_updating = false;
		}
		UpdateMarkers();
	}

	/// <summary>
	/// Hue, saturation or value changed (keys, mouse or a binding): the channels, the square and the markers follow at
	/// once, the owner's colour through the throttled commit.
	/// </summary>
	private void OnHsvChanged()
	{
		if (_updating)
		{
			return;
		}
		_updating = true;
		Rgb color;
		try
		{
			color = Rgb.FromHsv(Hue, Saturation, Value);
			Red = color.R;
			Green = color.G;
			Blue = color.B;
			UpdateHueBrush();
		}
		finally
		{
			_updating = false;
		}
		UpdateMarkers();
		RequestCommit(color);
	}

	/// <summary>Commits now when the last commit is <see cref="CommitInterval"/> or more ago, else when the timer fires.</summary>
	private void RequestCommit(Rgb color)
	{
		_pendingColor = color;
		_pending = true;
		if (Environment.TickCount64 - _lastCommit >= CommitInterval)
		{
			Commit();
		}
		else if (!_commitTimer.IsEnabled)
		{
			_commitTimer.Start();
		}
	}

	/// <summary>Hands the pending colour to the owner; the colour's own change handler is skipped, the views are current.</summary>
	private void Commit()
	{
		_commitTimer.Stop();
		if (!_pending)
		{
			return;
		}
		_pending = false;
		_lastCommit = Environment.TickCount64;
		_updating = true;
		try
		{
			SetCurrentValue(ColorProperty, _pendingColor);
		}
		finally
		{
			_updating = false;
		}
	}

	/// <summary>A channel box changed: the colour follows, and through it the HSV side.</summary>
	private void OnChannelChanged()
	{
		if (!_updating)
		{
			SetCurrentValue(ColorProperty, Rgb.Clamped(Red, Green, Blue));
		}
	}

	private void UpdateHueBrush()
	{
		Rgb pure = Rgb.FromHsv(Hue, 1, 1);
		HueBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(pure.R, pure.G, pure.B));
	}

	/// <summary>Places the two markers: saturation along the square, value up it, hue down the strip.</summary>
	private void UpdateMarkers()
	{
		_squareMarker ??= MarkerOf(_square, SquareMarkerPart);
		_hueMarker ??= MarkerOf(_strip, HueMarkerPart);
		if (_square != null && _squareMarker != null)
		{
			Canvas.SetLeft(_squareMarker, Saturation * _square.Bounds.Width - _squareMarker.Width / 2);
			Canvas.SetTop(_squareMarker, (1 - Value) * _square.Bounds.Height - _squareMarker.Height / 2);
		}
		if (_strip != null && _hueMarker != null)
		{
			Canvas.SetTop(_hueMarker, Hue / 360 * _strip.Bounds.Height - _hueMarker.Height / 2);
		}
	}

	// ---------------------------------------------------------------- mouse (the thumbs capture it; the position is read, not the delta)

	private void OnSquarePressed(object? sender, PointerPressedEventArgs e)
	{
		if (_square == null || !e.GetCurrentPoint(_square).Properties.IsLeftButtonPressed)
		{
			return;
		}
		_squareDown = true;
		_square.Focus();
		ApplySquarePoint(e.GetPosition(_square));
	}

	private void OnSquareMoved(object? sender, PointerEventArgs e)
	{
		if (_squareDown && _square != null)
		{
			ApplySquarePoint(e.GetPosition(_square));
		}
	}

	private void OnSquareReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_squareDown)
		{
			_squareDown = false;
			Commit();
		}
	}

	private void ApplySquarePoint(Point p)
	{
		if (_square == null || _square.Bounds.Width <= 0 || _square.Bounds.Height <= 0)
		{
			return;
		}
		Saturation = Math.Clamp(p.X / _square.Bounds.Width, 0, 1);
		Value = Math.Clamp(1 - p.Y / _square.Bounds.Height, 0, 1);
	}

	private void OnStripPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_strip == null || !e.GetCurrentPoint(_strip).Properties.IsLeftButtonPressed)
		{
			return;
		}
		_stripDown = true;
		_strip.Focus();
		ApplyStripPoint(e.GetPosition(_strip));
	}

	private void OnStripMoved(object? sender, PointerEventArgs e)
	{
		if (_stripDown && _strip != null)
		{
			ApplyStripPoint(e.GetPosition(_strip));
		}
	}

	private void OnStripReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_stripDown)
		{
			_stripDown = false;
			Commit();
		}
	}

	private void ApplyStripPoint(Point p)
	{
		if (_strip == null || _strip.Bounds.Height <= 0)
		{
			return;
		}
		Hue = Math.Clamp(p.Y / _strip.Bounds.Height, 0, 1) * 360 % 360;
	}

	// ---------------------------------------------------------------- keyboard

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		if (_square?.IsFocused == true)
		{
			double step = shift ? 0.1 : 0.01;
			switch (e.Key)
			{
				case Key.Left:
					Saturation = Math.Clamp(Saturation - step, 0, 1);
					break;
				case Key.Right:
					Saturation = Math.Clamp(Saturation + step, 0, 1);
					break;
				case Key.Up:
					Value = Math.Clamp(Value + step, 0, 1);
					break;
				case Key.Down:
					Value = Math.Clamp(Value - step, 0, 1);
					break;
				default:
					base.OnKeyDown(e);
					return;
			}
			e.Handled = true;
			return;
		}
		if (_strip?.IsFocused == true)
		{
			double degrees = shift ? 10 : 1;
			switch (e.Key)
			{
				case Key.Up:
					Hue = (Hue - degrees + 360) % 360;
					break;
				case Key.Down:
					Hue = (Hue + degrees) % 360;
					break;
				default:
					base.OnKeyDown(e);
					return;
			}
			e.Handled = true;
			return;
		}
		base.OnKeyDown(e);
	}
}
