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

using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// The time axis of a run, in frames: a tall scrub bar with tick labels, a playhead with its
/// frame number, an in/out range drawn as a band between two brackets, and the frame under the
/// pointer. Click or drag scrubs; dragging a bracket asks for a new in or out point through
/// <see cref="SetRangeStartCommand"/> / <see cref="SetRangeEndCommand"/> (the range itself is
/// owned by the model, so the control never writes <see cref="RangeStart"/> or
/// <see cref="RangeEnd"/>). Keyboard: Left/Right one frame, PageUp/PageDown ten, Home/End to
/// the in/out points, the wheel one frame. While a run is in progress <see cref="Available"/> shades the
/// frames that exist and bounds the playhead. Drawn in <see cref="Render"/>; colours come from
/// the brush properties so a theme style can set them. A port of upstream's WPF
/// <c>TimelineBar</c> (W/Controls/TimelineBar.cs), same geometry and draw order.
/// </summary>
public sealed class TimelineBar : Control
{
	/// <summary>Height of the label row above the bar, in pixels.</summary>
	public const double LabelRowHeight = 14.0;

	/// <summary>Default height of the whole control, in pixels.</summary>
	public const double DefaultHeight = 36.0;

	/// <summary>Pixels a tick label needs at least, which picks the labelled tick interval.</summary>
	private const double MinLabelSpacing = 64.0;

	/// <summary>Half-width of the hit area of a range bracket, in pixels.</summary>
	private const double BracketHitHalfWidth = 6.0;

	/// <summary>Size of every label; upstream draws the playhead's number at the same size, only bolder.</summary>
	private const double LabelFontSize = 11.0;

	private static readonly int[] TickCandidates = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

	// the application's default font, the platform's (Segoe UI on Windows, which the WPF head names); the weight is the
	// only thing a label varies
	private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
	private static readonly Typeface BoldLabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

	/// <summary>Identifies the <see cref="Maximum"/> property: the last frame index.</summary>
	public static readonly StyledProperty<int> MaximumProperty =
		AvaloniaProperty.Register<TimelineBar, int>(nameof(Maximum));

	/// <summary>Identifies the <see cref="Value"/> property: the frame shown.</summary>
	public static readonly StyledProperty<int> ValueProperty =
		AvaloniaProperty.Register<TimelineBar, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay, coerce: ClampValue);

	/// <summary>Identifies the <see cref="RangeStart"/> property: the in point, in frames.</summary>
	public static readonly StyledProperty<int> RangeStartProperty =
		AvaloniaProperty.Register<TimelineBar, int>(nameof(RangeStart));

	/// <summary>Identifies the <see cref="RangeEnd"/> property: the out point, in frames.</summary>
	public static readonly StyledProperty<int> RangeEndProperty =
		AvaloniaProperty.Register<TimelineBar, int>(nameof(RangeEnd));

	/// <summary>Identifies the <see cref="SetRangeStartCommand"/> property.</summary>
	public static readonly StyledProperty<ICommand?> SetRangeStartCommandProperty =
		AvaloniaProperty.Register<TimelineBar, ICommand?>(nameof(SetRangeStartCommand));

	/// <summary>Identifies the <see cref="SetRangeEndCommand"/> property.</summary>
	public static readonly StyledProperty<ICommand?> SetRangeEndCommandProperty =
		AvaloniaProperty.Register<TimelineBar, ICommand?>(nameof(SetRangeEndCommand));

	/// <summary>Identifies the <see cref="Background"/> property: fill of the bar.</summary>
	public static readonly StyledProperty<IBrush?> BackgroundProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(Background), Brushes.Transparent);

	/// <summary>Identifies the <see cref="Foreground"/> property: playhead and its label.</summary>
	public static readonly StyledProperty<IBrush?> ForegroundProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(Foreground), Brushes.Black);

	/// <summary>Identifies the <see cref="TickBrush"/> property: the ticks.</summary>
	public static readonly StyledProperty<IBrush?> TickBrushProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(TickBrush), Brushes.Gray);

	/// <summary>Identifies the <see cref="OutlineBrush"/> property: the bar's border and the hover line.</summary>
	public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(OutlineBrush), Brushes.Gray);

	/// <summary>Identifies the <see cref="LabelBrush"/> property: tick labels and the hover label.</summary>
	public static readonly StyledProperty<IBrush?> LabelBrushProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(LabelBrush), Brushes.Gray);

	/// <summary>Identifies the <see cref="AccentBrush"/> property: range band, brackets and keyboard focus.</summary>
	public static readonly StyledProperty<IBrush?> AccentBrushProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(AccentBrush), Brushes.DodgerBlue);

	/// <summary>Identifies the <see cref="Available"/> property: frames computed so far.</summary>
	public static readonly StyledProperty<int> AvailableProperty =
		AvaloniaProperty.Register<TimelineBar, int>(nameof(Available));

	/// <summary>Identifies the <see cref="AvailableBrush"/> property: shading of the available frames.</summary>
	public static readonly StyledProperty<IBrush?> AvailableBrushProperty =
		AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(AvailableBrush), Brushes.Transparent);

	/// <summary>What a pointer press started, and what <see cref="BracketAt"/> found under the pointer.</summary>
	internal enum DragMode
	{
		/// <summary>No drag; from <see cref="BracketAt"/>: no bracket there.</summary>
		None,

		/// <summary>The playhead follows the pointer.</summary>
		Scrub,

		/// <summary>The in bracket; from <see cref="BracketAt"/>: the in bracket is under the pointer.</summary>
		RangeStart,

		/// <summary>The out bracket; from <see cref="BracketAt"/>: the out bracket is under the pointer.</summary>
		RangeEnd,
	}

	private DragMode _drag;
	private int? _hoverFrame;
	private Cursor? _bracketCursor;

	static TimelineBar()
	{
		AffectsRender<TimelineBar>(MaximumProperty, ValueProperty, RangeStartProperty, RangeEndProperty, BackgroundProperty, ForegroundProperty,
			TickBrushProperty, OutlineBrushProperty, LabelBrushProperty, AccentBrushProperty, AvailableProperty, AvailableBrushProperty);
	}

	/// <summary>A focusable bar of <see cref="DefaultHeight"/> that draws its own focus rectangle (so no theme adorner).</summary>
	public TimelineBar()
	{
		Focusable = true;
		FocusAdorner = null;
		Height = DefaultHeight;
		AutomationProperties.SetItemStatus(this, ItemStatusText());
	}

	/// <summary>Last frame index; 0 means a single frame and nothing to scrub.</summary>
	public int Maximum
	{
		get => GetValue(MaximumProperty);
		set => SetValue(MaximumProperty, value);
	}

	/// <summary>Frame shown, 0..<see cref="Maximum"/>.</summary>
	public int Value
	{
		get => GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>In point of the range, in frames.</summary>
	public int RangeStart
	{
		get => GetValue(RangeStartProperty);
		set => SetValue(RangeStartProperty, value);
	}

	/// <summary>Out point of the range, in frames.</summary>
	public int RangeEnd
	{
		get => GetValue(RangeEndProperty);
		set => SetValue(RangeEndProperty, value);
	}

	/// <summary>Executed with the frame (int) when the in bracket is dragged.</summary>
	public ICommand? SetRangeStartCommand
	{
		get => GetValue(SetRangeStartCommandProperty);
		set => SetValue(SetRangeStartCommandProperty, value);
	}

	/// <summary>Executed with the frame (int) when the out bracket is dragged.</summary>
	public ICommand? SetRangeEndCommand
	{
		get => GetValue(SetRangeEndCommandProperty);
		set => SetValue(SetRangeEndCommandProperty, value);
	}

	/// <summary>Fill of the bar.</summary>
	public IBrush? Background
	{
		get => GetValue(BackgroundProperty);
		set => SetValue(BackgroundProperty, value);
	}

	/// <summary>Playhead and its frame label.</summary>
	public IBrush? Foreground
	{
		get => GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}

	/// <summary>
	/// The ticks (2026-11-10, the user's request: visible but subtle; the theme's TimelineTick, a few dozen levels from the
	/// bar's fill, where ControlBorder had been four levels from it and the ticks invisible).
	/// </summary>
	public IBrush? TickBrush
	{
		get => GetValue(TickBrushProperty);
		set => SetValue(TickBrushProperty, value);
	}

	/// <summary>The bar's border and the hover line.</summary>
	public IBrush? OutlineBrush
	{
		get => GetValue(OutlineBrushProperty);
		set => SetValue(OutlineBrushProperty, value);
	}

	/// <summary>Tick labels and the hover label.</summary>
	public IBrush? LabelBrush
	{
		get => GetValue(LabelBrushProperty);
		set => SetValue(LabelBrushProperty, value);
	}

	/// <summary>Range band and brackets, keyboard focus border.</summary>
	public IBrush? AccentBrush
	{
		get => GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	/// <summary>
	/// Number of frames that exist so far while a run fills the axis; 0 when every frame of the axis is
	/// there (no run). The bar shades them; whether <see cref="Value"/> may go beyond is the owner's call.
	/// </summary>
	public int Available
	{
		get => GetValue(AvailableProperty);
		set => SetValue(AvailableProperty, value);
	}

	/// <summary>Shading of the available frames (a translucent fill works best).</summary>
	public IBrush? AvailableBrush
	{
		get => GetValue(AvailableBrushProperty);
		set => SetValue(AvailableBrushProperty, value);
	}

	// the coercion of upstream's Value DP: 0..Maximum. Math.Clamp throws when min > max, and a Maximum below 0 (an
	// empty run's planned last frame) must not take the whole binding down, so the upper bound is at least 0
	private static int ClampValue(AvaloniaObject owner, int value) => Math.Clamp(value, 0, Math.Max(0, ((TimelineBar)owner).Maximum));

	private string ItemStatusText() => $"range {RangeStart}..{RangeEnd}";

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == MaximumProperty || change.Property == AvailableProperty)
		{
			// Avalonia coerces a property only when it is set; a new bound makes the current Value stale
			CoerceValue(ValueProperty);
		}
		else if (change.Property == ValueProperty)
		{
			if (ControlAutomationPeer.FromElement(this) is TimelineBarAutomationPeer peer)
			{
				peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)change.GetOldValue<int>(), (double)change.GetNewValue<int>());
			}
		}
		else if (change.Property == RangeStartProperty || change.Property == RangeEndProperty)
		{
			// the harnesses read the in/out points as ItemStatus "range a..b" (upstream's peer); kept on the attached
			// property so any peer, not only ours, reports it
			AutomationProperties.SetItemStatus(this, ItemStatusText());
		}
	}

	/// <inheritdoc/>
	protected override AutomationPeer OnCreateAutomationPeer() => new TimelineBarAutomationPeer(this);

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize) =>
		new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, DefaultHeight);

	// ---------------------------------------------------------------- geometry

	/// <summary>
	/// Usable track: frame 0 on the first pixel column inside the bar's outline, the last frame on the last one
	/// (columns 1 to width − 2), so the playhead and the brackets at either end stay off the outline.
	/// </summary>
	private double TrackLeft => 1.0;
	private double TrackWidth => Math.Max(1.0, Bounds.Width - 3.0);
	private double BarTop => LabelRowHeight;
	private double BarHeight => Math.Max(1.0, Bounds.Height - LabelRowHeight);

	/// <summary>Pixel x of a frame's centre line.</summary>
	internal double XOf(int frame) => Maximum > 0 ? TrackLeft + frame * TrackWidth / Maximum : TrackLeft;

	/// <summary>Frame nearest to a pixel x, clamped.</summary>
	internal int FrameAt(double x) =>
		Maximum > 0 ? Math.Clamp((int)Math.Round((x - TrackLeft) / TrackWidth * Maximum), 0, Maximum) : 0;

	/// <summary>Which bracket, if any, a pixel x is on; the nearer one wins when both are close.</summary>
	internal DragMode BracketAt(double x)
	{
		double dStart = Math.Abs(x - XOf(RangeStart));
		double dEnd = Math.Abs(x - XOf(RangeEnd));
		if (dStart <= BracketHitHalfWidth && dStart <= dEnd)
		{
			return DragMode.RangeStart;
		}
		return dEnd <= BracketHitHalfWidth ? DragMode.RangeEnd : DragMode.None;
	}

	// ---------------------------------------------------------------- rendering

	/// <inheritdoc/>
	public override void Render(DrawingContext dc)
	{
		base.Render(dc);
		double w = Bounds.Width;
		double h = Bounds.Height;
		if (w <= 2 || h <= LabelRowHeight)
		{
			return;
		}
		_scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0; // once per draw, not per edge
		// a line is a whole number of device pixels thick (one at 100 %, two at 150 and 200 %), and every edge drawn
		// below is moved onto the device grid by Snap, so the lines and fills come out whole at a UI zoom and on a
		// 150 % monitor as at 100 % (upstream's rule of 2026-11-02, there through WPF guidelines, which Avalonia has not);
		// at 100 % the arithmetic is upstream's, pixel for pixel
		double t = Hairline;
		double half = t / 2;
		IPen pen = PenFor(OutlineBrush, t);
		double right = Snap(w);
		double barTop = Snap(BarTop);
		double barBottom = Snap(BarTop + BarHeight);
		var bar = new Rect(half, barTop + half, right - t, barBottom - barTop - t);
		// fills and ticks lie on whole pixels inside the outline, whose rectangle runs through the centres of its pixels:
		// on those half-pixel edges a fill is smeared over one row or column more (upstream, 2026-11-01, the in point's bracket)
		var inner = new Rect(t, barTop + t, right - 2 * t, barBottom - barTop - 2 * t);

		// the frames a run has produced so far, as a band from the start to the newest frame's column
		double xa = Available > 0 && Available <= Maximum ? Math.Round(XOf(Available - 1)) + 1.0 : double.NaN;
		// the in/out range: a translucent band over both points' columns once it is narrower than the run
		double c0 = Math.Round(XOf(RangeStart));
		double c1 = Math.Round(XOf(RangeEnd));
		double hoverX = _hoverFrame is int hover && _drag == DragMode.None && hover != Value ? Math.Round(XOf(hover)) : double.NaN;
		double playheadX = Math.Round(XOf(Value));

		dc.DrawRectangle(Background, pen, bar);
		if (!double.IsNaN(xa))
		{
			dc.DrawRectangle(AvailableBrush, null, SnapRect(inner.Left, inner.Top, Math.Max(inner.Left, xa), inner.Bottom));
		}
		if (c1 > c0 && (RangeStart > 0 || RangeEnd < Maximum))
		{
			using (dc.PushOpacity(0.25))
			{
				dc.DrawRectangle(AccentBrush, null, SnapRect(c0, inner.Top, c1 + 1.0, inner.Bottom));
			}
		}

		// ticks and labels: their layout is cached (upstream, 2026-10-16: laid out afresh they cost over a millisecond
		// per draw, and the bar is drawn on every position change, sixty times a second during playback); the layout
		// is rebuilt only when the width, the range or the culture change, and the labels come from the label cache
		if (Maximum > 0)
		{
			DrawTicks(dc, w, inner, PenFor(TickBrush, t), half);
		}

		// the brackets over the ticks: a tick on a point's frame (frame 0 always has one) cut a notch into its bracket
		DrawBracket(dc, c0, inner, open: true);
		DrawBracket(dc, c1, inner, open: false);

		// the frame under the pointer
		if (!double.IsNaN(hoverX))
		{
			double x = Snap(hoverX) + half;
			using (dc.PushOpacity(0.5))
			{
				dc.DrawLine(pen, new Point(x, bar.Top), new Point(x, bar.Bottom));
			}
		}

		// the playhead: a line through the bar, a handle on top, the frame number next to it
		double px = Snap(playheadX) + half;
		dc.DrawLine(PenFor(Foreground, t), new Point(px, bar.Top), new Point(px, bar.Bottom));
		var handle = new StreamGeometry();
		using (StreamGeometryContext g = handle.Open())
		{
			g.BeginFigure(new Point(px - 5, bar.Top - 6), true);
			g.LineTo(new Point(px + 5, bar.Top - 6), true);
			g.LineTo(new Point(px, bar.Top), true);
			g.EndFigure(true);
		}
		dc.DrawGeometry(Foreground, null, handle);
		FormattedText playhead = Label(Value.ToString(CultureInfo.CurrentCulture), Foreground, bold: true);
		double tx = px + 8 + playhead.Width <= w ? px + 8 : px - 8 - playhead.Width;
		dc.DrawRectangle(Background, null, new Rect(tx - 3, 0, playhead.Width + 6, LabelRowHeight)); // over the tick labels under it
		dc.DrawText(playhead, new Point(tx, 0));

		if (IsFocused)
		{
			dc.DrawRectangle(null, PenFor(AccentBrush, t), new Rect(half, half, right - t, Snap(h) - t));
		}
	}

	/// <summary>
	/// A bracket on the frame's pixel <paramref name="column"/> (its left edge): a 2 px bar over that column and its
	/// neighbour inside the range, with 6 px feet 2 px thick pointing into the range, filling <paramref name="inner"/>'s height.
	/// </summary>
	private void DrawBracket(DrawingContext dc, double column, Rect inner, bool open)
	{
		double left = open ? column : column - 1.0;
		double feet = open ? left : left - 4.0;
		dc.DrawRectangle(AccentBrush, null, SnapRect(left, inner.Top, left + 2.0, inner.Bottom));
		dc.DrawRectangle(AccentBrush, null, SnapRect(feet, inner.Top, feet + 6.0, inner.Top + 2.0));
		dc.DrawRectangle(AccentBrush, null, SnapRect(feet, inner.Bottom - 2.0, feet + 6.0, inner.Bottom));
	}

	// ---------------------------------------------------------------- device pixels

	private double _scale = 1.0;

	/// <summary>Thickness of a line that is a whole number of device pixels (one at 100 %, two at 150 and 200 %), in layout pixels.</summary>
	private double Hairline => Math.Max(1.0, Math.Round(_scale)) / _scale;

	/// <summary>The layout coordinate moved onto the nearest device pixel boundary (the job of a WPF guideline).</summary>
	private double Snap(double v) => Math.Round(v * _scale) / _scale;

	/// <summary>A rectangle whose four edges lie on device pixel boundaries.</summary>
	private Rect SnapRect(double x0, double y0, double x1, double y1)
	{
		double left = Snap(x0);
		double top = Snap(y0);
		return new Rect(left, top, Math.Max(0, Snap(x1) - left), Math.Max(0, Snap(y1) - top));
	}

	// ---------------------------------------------------------------- caches

	// the labels and pens are cached (upstream, 2026-10-16): built afresh, a draw of the bar cost 3 ms, mostly
	// FormattedText, and the bar is drawn on every frame-count and position change; the cache is cleared when it grows
	// large (the playhead's label changes per frame) and when the culture changes, which the key carries. Avalonia lays
	// text out in layout pixels, so the DPI is not part of the key as it was in WPF.
	private readonly Dictionary<(string Text, bool Bold, IBrush? Brush, string Culture), FormattedText> _labels = new();
	private readonly Dictionary<(IBrush? Brush, double Thickness), IPen> _pens = new();
	private const int LabelCacheLimit = 2048;

	private FormattedText Label(string text, IBrush? brush, bool bold = false)
	{
		var key = (text, bold, brush, CultureInfo.CurrentUICulture.Name);
		if (!_labels.TryGetValue(key, out FormattedText? label))
		{
			if (_labels.Count >= LabelCacheLimit)
			{
				_labels.Clear();
			}
			label = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? BoldLabelTypeface : LabelTypeface, LabelFontSize, brush);
			_labels[key] = label;
		}
		return label;
	}

	/// <summary>A pen of <paramref name="brush"/> and <paramref name="thickness"/>, made once per pair (the theme brushes are shared instances).</summary>
	private IPen PenFor(IBrush? brush, double thickness)
	{
		if (!_pens.TryGetValue((brush, thickness), out IPen? pen))
		{
			pen = new Pen(brush, thickness);
			_pens[(brush, thickness)] = pen;
		}
		return pen;
	}

	/// <summary>The tick columns of one width and range: minor ticks, and major ticks with their label texts.</summary>
	private sealed record TickLayout(double[] MinorColumns, double[] MajorColumns, string[] MajorLabels);

	private TickLayout? _ticks;
	private (double Width, int Maximum, string Culture) _ticksKey;

	/// <summary>
	/// The minor and major ticks (3 and 6 px down from the top of <paramref name="inner"/>, the bar inside its outline)
	/// with their labels, from a layout rebuilt only when its inputs change; <paramref name="half"/> is half the tick
	/// line's thickness, which puts the line on its snapped column.
	/// </summary>
	private void DrawTicks(DrawingContext dc, double w, Rect inner, IPen pen, double half)
	{
		var key = (w, Maximum, CultureInfo.CurrentCulture.Name);
		if (_ticks == null || key != _ticksKey)
		{
			_ticks = LayOutTicks();
			_ticksKey = key;
		}
		double minorBottom = Snap(inner.Top + 3);
		double majorBottom = Snap(inner.Top + 6);
		foreach (double column in _ticks.MinorColumns)
		{
			double x = Snap(column) + half;
			dc.DrawLine(pen, new Point(x, inner.Top), new Point(x, minorBottom));
		}
		for (int i = 0; i < _ticks.MajorColumns.Length; i++)
		{
			double x = Snap(_ticks.MajorColumns[i]) + half;
			dc.DrawLine(pen, new Point(x, inner.Top), new Point(x, majorBottom));
			FormattedText label = Label(_ticks.MajorLabels[i], LabelBrush);
			dc.DrawText(label, new Point(Math.Min(x + 2, w - label.Width - 1), 0));
		}
	}

	private TickLayout LayOutTicks()
	{
		// the labelled interval is the smallest candidate that leaves room for its text
		double pxPerFrame = TrackWidth / Maximum;
		int major = TickCandidates[^1];
		foreach (int candidate in TickCandidates)
		{
			if (candidate * pxPerFrame >= MinLabelSpacing)
			{
				major = candidate;
				break;
			}
		}
		int minor = major >= 10 ? major / 5 : major >= 2 ? 1 : 0;
		var minors = new List<double>();
		if (minor > 0)
		{
			for (int f = 0; f <= Maximum; f += minor)
			{
				if (f % major != 0)
				{
					minors.Add(Math.Round(XOf(f)));
				}
			}
		}
		var majors = new List<double>();
		var labels = new List<string>();
		for (int f = 0; f <= Maximum; f += major)
		{
			majors.Add(Math.Round(XOf(f)));
			labels.Add(f.ToString(CultureInfo.CurrentCulture));
		}
		return new TickLayout([.. minors], [.. majors], [.. labels]);
	}

	// ---------------------------------------------------------------- pointer

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Focus();
		double x = e.GetPosition(this).X;
		_drag = BracketAt(x) switch
		{
			DragMode.RangeStart => DragMode.RangeStart,
			DragMode.RangeEnd => DragMode.RangeEnd,
			_ => DragMode.Scrub,
		};
		ApplyDrag(x);
		e.Pointer.Capture(this);
		e.Handled = true;
	}

	/// <inheritdoc/>
	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		double x = e.GetPosition(this).X;
		if (_drag != DragMode.None)
		{
			ApplyDrag(x);
			return;
		}
		_hoverFrame = FrameAt(x);
		// the cursor is made on first use: a cursor needs the platform, which is not up when the type loads
		Cursor = BracketAt(x) == DragMode.None ? Cursor.Default : _bracketCursor ??= new Cursor(StandardCursorType.SizeWestEast);
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (_drag != DragMode.None)
		{
			_drag = DragMode.None;
			e.Pointer.Capture(null);
			InvalidateVisual();
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		// another control took the pointer mid-drag (a popup, a window switch): the drag ends there, or the bar would
		// go on scrubbing from the next move it happens to see
		if (_drag != DragMode.None)
		{
			_drag = DragMode.None;
			InvalidateVisual();
		}
	}

	/// <inheritdoc/>
	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);
		_hoverFrame = null;
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		if (e.Delta.Y == 0)
		{
			return; // a sideways scroll: not a frame step
		}
		Value += e.Delta.Y > 0 ? 1 : -1;
		e.Handled = true;
	}

	private void ApplyDrag(double x)
	{
		int frame = FrameAt(x);
		switch (_drag)
		{
			case DragMode.Scrub:
				Value = frame;
				break;
			case DragMode.RangeStart:
				Execute(SetRangeStartCommand, frame);
				break;
			case DragMode.RangeEnd:
				Execute(SetRangeEndCommand, frame);
				break;
		}
	}

	private static void Execute(ICommand? command, int frame)
	{
		if (command?.CanExecute(frame) == true)
		{
			command.Execute(frame);
		}
	}

	// ---------------------------------------------------------------- keyboard

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		int? target = e.Key switch
		{
			Key.Left => Value - 1,
			Key.Right => Value + 1,
			Key.PageDown => Value - 10,
			Key.PageUp => Value + 10,
			Key.Home => RangeStart,
			Key.End => RangeEnd,
			_ => null,
		};
		if (target is int frame)
		{
			Value = frame;
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnLostFocus(FocusChangedEventArgs e)
	{
		base.OnLostFocus(e);
		InvalidateVisual();
	}
}

/// <summary>Exposes a <see cref="TimelineBar"/> to UI Automation as a slider with a range value.</summary>
public sealed class TimelineBarAutomationPeer : ControlAutomationPeer, IRangeValueProvider
{
	/// <summary>A peer of <paramref name="owner"/>.</summary>
	public TimelineBarAutomationPeer(TimelineBar owner) : base(owner)
	{
	}

	private TimelineBar Bar => (TimelineBar)Owner;

	/// <inheritdoc/>
	protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

	/// <summary>
	/// The in and out points in frames as "range &lt;in&gt;..&lt;out&gt;", for the harnesses (the range value carries the
	/// playhead and the maximum); the same text the bar keeps on its ItemStatus attached property.
	/// </summary>
	protected override string? GetItemStatusCore() => $"range {Bar.RangeStart}..{Bar.RangeEnd}";

	/// <inheritdoc/>
	protected override bool IsContentElementCore() => true;

	/// <inheritdoc/>
	protected override bool IsControlElementCore() => true;

	/// <inheritdoc/>
	public bool IsReadOnly => false;

	/// <inheritdoc/>
	public double Value => Bar.Value;

	/// <inheritdoc/>
	public double Minimum => 0;

	/// <inheritdoc/>
	public double Maximum => Bar.Maximum;

	/// <inheritdoc/>
	public double SmallChange => 1;

	/// <inheritdoc/>
	public double LargeChange => 10;

	/// <inheritdoc/>
	public void SetValue(double value) => Bar.Value = (int)Math.Round(value);
}
