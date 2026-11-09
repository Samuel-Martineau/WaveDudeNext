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
//   Mark <mark@pab-ai.com>, author of the Avalonia port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// A text box for one number with up/down buttons, upstream's <c>NumberBox</c>: Up/Down change the value by
/// <see cref="Increment"/>, PageUp/PageDown by ten increments, the mouse wheel does the same while the box has keyboard
/// focus, the two repeat buttons on the right do it with the mouse, and a horizontal drag on the text itself scrubs the
/// value, Blender style: press an unfocused box, move sideways past the drag threshold and the value follows the mouse
/// by <see cref="DragStep"/> per pixel (by default one increment per <see cref="PixelsPerStep"/> pixels), ten times
/// finer with Shift, ten times coarser with Ctrl, rounded to <see cref="Decimals"/> places (by default the increment's,
/// so a whole-number box never writes a fraction); Escape during the drag restores the value the drag started from; a
/// press without movement stays an ordinary click that places the caret. Upstream wrapped the cursor at the monitor's
/// edge through user32; there is no portable equivalent, so a drag ends at the screen's edge here. A
/// <see cref="Logarithmic"/> box, for a gain such as a brightness, steps by a factor instead. Every step clamps to
/// <see cref="Minimum"/>..<see cref="Maximum"/>, writes the text back through the <see cref="TextBox.Text"/> binding
/// at once and selects it, so a source moves on the picture with each key press. Typing stays plain text: the binding
/// converts when the box loses the focus (the views bind with <c>UpdateSourceTrigger=LostFocus</c>) or on Enter
/// through <see cref="EditBehavior"/>, and the model clamps. Numbers are parsed and written in the current culture,
/// like the bindings. The control theme in Themes/Editors.axaml names the repeat buttons PART_Up and PART_Down. UI thread.
/// </summary>
public sealed class NumberBox : TextBox
{
	/// <summary>Identifies the <see cref="Minimum"/> property.</summary>
	public static readonly StyledProperty<double> MinimumProperty =
		AvaloniaProperty.Register<NumberBox, double>(nameof(Minimum), double.NegativeInfinity);

	/// <summary>Identifies the <see cref="Maximum"/> property.</summary>
	public static readonly StyledProperty<double> MaximumProperty =
		AvaloniaProperty.Register<NumberBox, double>(nameof(Maximum), double.PositiveInfinity);

	/// <summary>Identifies the <see cref="Increment"/> property.</summary>
	public static readonly StyledProperty<double> IncrementProperty =
		AvaloniaProperty.Register<NumberBox, double>(nameof(Increment), 1.0);

	/// <summary>Identifies the <see cref="DragStep"/> property.</summary>
	public static readonly StyledProperty<double> DragStepProperty =
		AvaloniaProperty.Register<NumberBox, double>(nameof(DragStep), double.NaN);

	/// <summary>Identifies the <see cref="Decimals"/> property.</summary>
	public static readonly StyledProperty<int> DecimalsProperty =
		AvaloniaProperty.Register<NumberBox, int>(nameof(Decimals), -1);

	/// <summary>Identifies the <see cref="Logarithmic"/> property.</summary>
	public static readonly StyledProperty<bool> LogarithmicProperty =
		AvaloniaProperty.Register<NumberBox, bool>(nameof(Logarithmic));

	/// <summary>Steps per PageUp/PageDown and per wheel notch with Shift.</summary>
	private const int PageSteps = 10;

	/// <summary>The factor of one step of a <see cref="Logarithmic"/> box: two percent (upstream's choice after seven proved too coarse).</summary>
	private const double StepFactor = 1.02;

	/// <summary>Mouse pixels per doubling while scrubbing a <see cref="Logarithmic"/> box (before the Shift and Ctrl factors).</summary>
	private const double PixelsPerDoubling = 40.0;

	/// <summary>Mouse pixels per increment while scrubbing when <see cref="DragStep"/> is not set.</summary>
	public const double PixelsPerStep = 2.0;

	/// <summary>Device-independent pixels the pointer must travel sideways before a press becomes a scrub (WPF's MinimumHorizontalDragDistance).</summary>
	public const double DragThreshold = 4.0;

	private RepeatButton? _up;
	private RepeatButton? _down;
	private Point? _pressPoint;
	private double _pressValue;
	private bool _scrubbing;
	private IPointer? _pointer;

	/// <summary>Creates the box with the tunnelling handlers that see the pointer and the wheel before the text box does (WPF's Preview events).</summary>
	public NumberBox()
	{
		// Enter commits and Escape reverts whatever theme the box gets (the editor theme sets it for every text box too)
		EditBehavior.SetCommitOnEnter(this, true);
		AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
		AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
		AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
		AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);
	}

	/// <summary>Lowest value a step can reach (typed text is not limited here; the model clamps it).</summary>
	public double Minimum
	{
		get => GetValue(MinimumProperty);
		set => SetValue(MinimumProperty, value);
	}

	/// <summary>Highest value a step can reach.</summary>
	public double Maximum
	{
		get => GetValue(MaximumProperty);
		set => SetValue(MaximumProperty, value);
	}

	/// <summary>Amount of one Up/Down step; 1 for integers, a fraction for real-valued settings.</summary>
	public double Increment
	{
		get => GetValue(IncrementProperty);
		set => SetValue(IncrementProperty, value);
	}

	/// <summary>
	/// Change of the value per pixel of a horizontal drag (in device-independent pixels), before the Shift and Ctrl
	/// factors; NaN, the default, means one <see cref="Increment"/> per <see cref="PixelsPerStep"/> pixels.
	/// </summary>
	public double DragStep
	{
		get => GetValue(DragStepProperty);
		set => SetValue(DragStepProperty, value);
	}

	/// <summary>
	/// Decimal places a step or drag writes; -1, the default, means as many as <see cref="Increment"/> has, so a box
	/// bound to an integer never writes a fraction. A box bound to a real-valued property sets it to let a fine
	/// drag (Shift) reach below its increment.
	/// </summary>
	public int Decimals
	{
		get => GetValue(DecimalsProperty);
		set => SetValue(DecimalsProperty, value);
	}

	/// <summary>
	/// Whether the box holds a gain and steps by a factor (upstream's choice for the brightness, which added 1 to 0.0001
	/// on one press and nothing visible to 30000): Up and Down by <see cref="StepFactor"/>, two percent, PageUp and
	/// PageDown and a Shift wheel notch by ten such steps, a fifth, a drag by a doubling per
	/// <see cref="PixelsPerDoubling"/> pixels; the result rounded to three significant digits. <see cref="Increment"/>,
	/// <see cref="DragStep"/> and <see cref="Decimals"/> play no part; <see cref="Minimum"/> must be positive, where a
	/// value that is not starts.
	/// </summary>
	public bool Logarithmic
	{
		get => GetValue(LogarithmicProperty);
		set => SetValue(LogarithmicProperty, value);
	}

	/// <summary>The value the text holds, or null while it does not parse.</summary>
	public double? Value => double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ? value : null;

	/// <summary>Whether a horizontal drag is changing the value right now.</summary>
	public bool IsScrubbing => _scrubbing;

	/// <summary>
	/// Adds <paramref name="steps"/> increments to the value (a non-parsing text counts as 0), clamps, writes the text
	/// and pushes it into the binding. Rounded to the increment's precision so that 0.1 steps do not accumulate binary
	/// noise. A <see cref="Logarithmic"/> box multiplies by <see cref="StepFactor"/> per step instead.
	/// </summary>
	public void Step(int steps)
	{
		if (IsReadOnly || !IsEnabled)
		{
			return;
		}
		Write(Logarithmic ? Positive(Value) * Math.Pow(StepFactor, steps) : (Value ?? 0.0) + steps * Increment);
	}

	/// <summary>A value a logarithmic step can start from: the one given when above zero, else the minimum, else 1.</summary>
	private double Positive(double? value) => value is > 0.0 ? value.Value : Minimum > 0.0 ? Minimum : 1.0;

	/// <summary>Clamps <paramref name="value"/>, rounds it to the box's precision, writes the text, pushes it into the binding and selects it.</summary>
	private void Write(double value)
	{
		value = Math.Clamp(value, Minimum, Maximum);
		if (Logarithmic)
		{
			// three significant digits, through the text so the double is the one that prints as them
			value = double.Parse(value.ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}
		else
		{
			value = Math.Round(value, Math.Clamp(Decimals >= 0 ? Decimals : DecimalsOf(Increment), 0, 10));
		}
		// SetCurrentValue keeps the binding in place where a plain set would compete with it
		SetCurrentValue(TextProperty, value.ToString(CultureInfo.CurrentCulture));
		BindingOperations.GetBindingExpressionBase(this, TextProperty)?.UpdateSource();
		SelectAll();
	}

	/// <summary>How many decimals the increment needs ("0.01" → 2); 0 for whole numbers.</summary>
	private static int DecimalsOf(double increment)
	{
		int decimals = 0;
		while (decimals < 10 && Math.Abs(increment * Math.Pow(10, decimals) - Math.Round(increment * Math.Pow(10, decimals))) > 1e-9)
		{
			decimals++;
		}
		return decimals;
	}

	/// <inheritdoc/>
	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);
		if (_up != null)
		{
			_up.Click -= OnUpClick;
		}
		if (_down != null)
		{
			_down.Click -= OnDownClick;
		}
		_up = e.NameScope.Find<RepeatButton>("PART_Up");
		_down = e.NameScope.Find<RepeatButton>("PART_Down");
		if (_up != null)
		{
			_up.Click += OnUpClick;
		}
		if (_down != null)
		{
			_down.Click += OnDownClick;
		}
	}

	private void OnUpClick(object? sender, RoutedEventArgs e) => StepFromButton(1);

	private void OnDownClick(object? sender, RoutedEventArgs e) => StepFromButton(-1);

	/// <summary>A button step also takes the focus, so the written value is visibly the box's and the wheel works afterwards.</summary>
	private void StepFromButton(int direction)
	{
		if (!IsKeyboardFocusWithin)
		{
			Focus();
		}
		Step(direction);
	}

	// ---------------------------------------------------------------- scrubbing: a horizontal drag on the text changes the value

	/// <summary>The visual the drag is measured against: the window, so a re-layout of the form during the drag does not shift the measure.</summary>
	private Visual Root => TopLevel.GetTopLevel(this) ?? (Visual)this;

	private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		_pressPoint = null;
		// a press on the repeat buttons is theirs; one on the text of a box that already has the focus selects text
		// as in any text box (Blender's rule: a drag scrubbed where a selection was meant); one on an unfocused box may
		// become a scrub, or stay a click that focuses it and places the caret
		if (e.Handled || IsReadOnly || !IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
			|| IsOnRepeatButton(e.Source) || IsKeyboardFocusWithin)
		{
			return;
		}
		_pressPoint = e.GetPosition(Root);
		_pressValue = Value ?? 0.0;
	}

	private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_pressPoint == null)
		{
			return;
		}
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			// the release went elsewhere
			EndScrub();
			return;
		}
		double dx = e.GetPosition(Root).X - _pressPoint.Value.X;
		if (!_scrubbing)
		{
			if (Math.Abs(dx) < DragThreshold)
			{
				return;
			}
			_scrubbing = true;
			_pointer = e.Pointer;
			e.Pointer.Capture(this);
			Cursor = new Cursor(StandardCursorType.SizeWestEast);
			if (!IsKeyboardFocusWithin)
			{
				Focus();
			}
		}
		double step = Logarithmic ? 1.0 / PixelsPerDoubling : double.IsNaN(DragStep) ? Increment / PixelsPerStep : DragStep;
		if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
		{
			step /= 10.0;
		}
		else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			step *= 10.0;
		}
		Write(Logarithmic ? Positive(_pressValue) * Math.Pow(2.0, dx * step) : _pressValue + dx * step);
		e.Handled = true;   // not a text selection
	}

	private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_scrubbing)
		{
			EndScrub();
			e.Handled = true;
		}
		_pressPoint = null;
	}

	/// <inheritdoc/>
	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		if (_scrubbing)
		{
			EndScrub();
		}
		_pressPoint = null;
	}

	private void EndScrub()
	{
		_scrubbing = false;
		_pressPoint = null;
		ClearValue(CursorProperty);
		if (_pointer != null)
		{
			IPointer pointer = _pointer;
			_pointer = null;
			if (ReferenceEquals(pointer.Captured, this))
			{
				pointer.Capture(null);
			}
		}
	}

	private bool IsOnRepeatButton(object? source)
	{
		for (Visual? visual = source as Visual; visual != null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
		{
			if (visual is RepeatButton)
			{
				return true;
			}
		}
		return false;
	}

	// ---------------------------------------------------------------- keyboard and wheel

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (!e.Handled)
		{
			if (_scrubbing && e.Key == Key.Escape)
			{
				// the drag is abandoned: back to the value it started from
				Write(_pressValue);
				EndScrub();
				e.Handled = true;
				return;
			}
			int steps = e.Key switch
			{
				Key.Up => 1,
				Key.Down => -1,
				Key.PageUp => PageSteps,
				Key.PageDown => -PageSteps,
				_ => 0,
			};
			if (steps != 0)
			{
				Step(steps);
				e.Handled = true;
				return;
			}
		}
		base.OnKeyDown(e);
	}

	private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
	{
		if (e.Handled || !IsKeyboardFocusWithin || e.Delta.Y == 0)
		{
			return;
		}
		int steps = Math.Sign(e.Delta.Y) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? PageSteps : 1);
		Step(steps);
		e.Handled = true;
	}
}
