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
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace WaveDudeNext.App.Controls;

/// <summary>What a <see cref="MarkerThumb"/> stands for.</summary>
public enum MarkerRole
{
	/// <summary>The position of a source (its centre for a composite source).</summary>
	Source,
	/// <summary>The rotation handle of a composite source: sits on the array's row axis and turns it (2026-10-14).</summary>
	SourceAngle,
	/// <summary>The centre of an optical part.</summary>
	Part,
	/// <summary>The rotation handle of an optical part: sits on the part's axis beyond its reach and turns it.</summary>
	PartAngle,
	/// <summary>The size grip of an optical part: a corner or vertex of its outline that sets its extents (2026-10-19).</summary>
	PartGrip,
	/// <summary>One point of an outline's list, or one node of a fibre's path (<see cref="MarkerThumb.Index"/> says which).</summary>
	PartPoint,
	/// <summary>The vertex of a lens surface (<see cref="MarkerThumb.Index"/> 1 or 2): moves along the axis and sets the surface's radius, the rim held (2026-10-26, Jeroen's proposal).</summary>
	PartSurface,
	/// <summary>The in-handle of a node of a fibre's path: the Bézier handle of the segment arriving there (<see cref="MarkerThumb.Index"/> says which node; 2026-10-22).</summary>
	FiberIn,
	/// <summary>The out-handle of a node of a fibre's path: the Bézier handle of the segment leaving there.</summary>
	FiberOut,
	/// <summary>Point A of a probe (the point of a point probe, the start of a line probe).</summary>
	ProbeA,
	/// <summary>Point B of a line probe.</summary>
	ProbeB,
	/// <summary>The graph box of a probe (moves it), see the probe graph overlay.</summary>
	ProbeBox,
	/// <summary>The bottom-right corner of a probe's graph box (resizes it).</summary>
	ProbeBoxCorner,
}

/// <summary>
/// One draggable handle of the <see cref="SceneOverlay"/> (or of the probe graph overlay): a <see cref="Thumb"/> that
/// knows which model object and which of its points (or its graph box) it moves. Focusable, so the arrow keys can move
/// it too; the overlay does the geometry and the model updates. The glyph comes from a control theme in
/// Themes/Markers.axaml, picked by the overlay for the role; the theme reads <see cref="StrokeThickness"/> and
/// <see cref="HaloThickness"/> and the <c>:selected</c>, <c>:focus</c> and <c>:pointerover</c> pseudo-classes.
/// <para>
/// The drag itself is run here rather than by <see cref="Thumb"/>, with the same three routed events: Avalonia's thumb
/// reports only the pointer's movement relative to itself and has no way to cancel a drag, while the overlay measures
/// every drag from the pointer's absolute position (a handle held back by a clamp or a group's limit would otherwise
/// feed the miss back on every move; upstream's SceneOverlay, 2026-10-19) and puts things back on Escape
/// (<see cref="CancelDrag"/>). So the thumb records the pointer's position in its parent's coordinates and the key
/// modifiers on every press, move and release (<see cref="PointerPosition"/>, <see cref="PointerModifiers"/>), raises
/// <see cref="Thumb.DragStarted"/>, <see cref="Thumb.DragDelta"/> and <see cref="Thumb.DragCompleted"/> as the base
/// class would, and flags a completion raised by <see cref="CancelDrag"/> with <see cref="DragCancelled"/>. Only the
/// left button drags; another button is left unhandled, so a right click reaches the pane's context menu. UI thread.
/// </para>
/// </summary>
[PseudoClasses(":selected", ":pressed")]
public sealed class MarkerThumb : Thumb
{
	/// <summary>Identifies the <see cref="IsSelectedMarker"/> property.</summary>
	public static readonly StyledProperty<bool> IsSelectedMarkerProperty =
		AvaloniaProperty.Register<MarkerThumb, bool>(nameof(IsSelectedMarker));

	/// <summary>Identifies the <see cref="StrokeThickness"/> property.</summary>
	public static readonly StyledProperty<double> StrokeThicknessProperty =
		AvaloniaProperty.Register<MarkerThumb, double>(nameof(StrokeThickness), 1.5);

	/// <summary>Identifies the <see cref="HaloThickness"/> property (read-only, follows <see cref="StrokeThickness"/>).</summary>
	public static readonly DirectProperty<MarkerThumb, double> HaloThicknessProperty =
		AvaloniaProperty.RegisterDirect<MarkerThumb, double>(nameof(HaloThickness), o => o.HaloThickness);

	/// <summary>How far the dark halo sticks out on each side of a stroke, in pixels, so the glyph reads on any picture.</summary>
	public const double HaloWidth = 2.0;

	/// <summary>The pointer of the drag in progress, to release its capture when the drag is cancelled.</summary>
	private IPointer? _pointer;

	/// <param name="item">The source, part or probe.</param>
	/// <param name="role">Which point of it.</param>
	/// <param name="index">For a point, node or Bézier handle, the index in the part's point or node list.</param>
	public MarkerThumb(object item, MarkerRole role, int index = -1)
	{
		Item = item;
		Role = role;
		Index = index;
		Focusable = true;
	}

	/// <summary>For a point, node or Bézier handle, the index in the part's point or node list; -1 otherwise.</summary>
	public int Index { get; }

	/// <summary>The source, part or probe this handle moves.</summary>
	public object Item { get; }

	/// <summary>Which point of the item.</summary>
	public MarkerRole Role { get; }

	/// <summary>Whether the item is the one selected in the project; the theme highlights it through the <c>:selected</c> pseudo-class.</summary>
	public bool IsSelectedMarker
	{
		get => GetValue(IsSelectedMarkerProperty);
		set => SetValue(IsSelectedMarkerProperty, value);
	}

	/// <summary>Outline width of the glyph in pixels; the theme thickens it on hover and focus.</summary>
	public double StrokeThickness
	{
		get => GetValue(StrokeThicknessProperty);
		set => SetValue(StrokeThicknessProperty, value);
	}

	/// <summary>Width of the dark outline drawn under the glyph: <see cref="StrokeThickness"/> plus <see cref="HaloWidth"/> on each side.</summary>
	public double HaloThickness => StrokeThickness + HaloWidth;

	/// <summary>Whether a drag is in progress: the left button went down on the handle and has not been released, and the drag was not cancelled.</summary>
	public bool IsDragging { get; private set; }

	/// <summary>
	/// Set while the <see cref="Thumb.DragCompleted"/> event raised by <see cref="CancelDrag"/> runs, so its handler can
	/// put the model back instead of keeping the drag's result; false for a completion by releasing the button.
	/// </summary>
	public bool DragCancelled { get; private set; }

	/// <summary>
	/// Where the pointer was at the last press, move or release, in the coordinates of the thumb's parent (the overlay),
	/// so a drag is measured against the absolute pointer position and not against the handle, which may be held back.
	/// </summary>
	public Point PointerPosition { get; private set; }

	/// <summary>The key modifiers held at the last press, move or release (Ctrl toggles, Shift adds or snaps).</summary>
	public KeyModifiers PointerModifiers { get; private set; }

	/// <summary>Model coordinate of the point (or the box's width, for a corner) when the current drag started (canvas pixels).</summary>
	internal int DragStartX { get; set; }

	/// <summary>Model coordinate of the point (or the box's height, for a corner) when the current drag started (canvas pixels).</summary>
	internal int DragStartY { get; set; }

	/// <summary>Where the pointer was when the drag started, in continuous canvas coordinates; a drag is measured from here against the pointer's absolute position, never from the handle, which may be held back (a clamp, a group's limit) and would feed the miss back (2026-10-19).</summary>
	internal double PressCanvasX { get; set; }

	/// <summary>The vertical part of <see cref="PressCanvasX"/>.</summary>
	internal double PressCanvasY { get; set; }

	/// <summary>For a grip or point handle: where the pointer took hold, relative to the handle's canvas position (canvas pixels), so the handle keeps that grip while it follows the pointer.</summary>
	internal double GrabOffsetX { get; set; }

	/// <summary>For a grip or point handle: the vertical part of the grab offset (canvas pixels).</summary>
	internal double GrabOffsetY { get; set; }

	/// <summary>For the Bézier handle of a smooth node: the other handle's length when the drag or key press began, which it keeps while it turns (canvas pixels); null otherwise.</summary>
	internal double? MirrorLength { get; set; }

	/// <summary>Canvas points captured as a drag or a run of keyboard steps starts, which it holds still: a lens surface's rim and the other surface's vertex (2026-10-26); null for a handle without any. Forgotten when the focus leaves.</summary>
	internal (double X, double Y)[]? DragAnchors { get; set; }

	/// <summary>
	/// Ends the drag in progress as cancelled (Escape): raises <see cref="Thumb.DragCompleted"/> with
	/// <see cref="DragCancelled"/> set, so the overlay restores the model, then lets the pointer go. Nothing happens
	/// without a drag.
	/// </summary>
	public void CancelDrag()
	{
		if (!IsDragging)
		{
			return;
		}
		IsDragging = false;
		PseudoClasses.Set(":pressed", false);
		DragCancelled = true;
		try
		{
			RaiseEvent(new VectorEventArgs { RoutedEvent = DragCompletedEvent, Vector = default });
		}
		finally
		{
			DragCancelled = false;
		}
		IPointer? pointer = _pointer;
		_pointer = null;
		// released after the completion, so the capture-lost it raises finds no drag to end
		pointer?.Capture(null);
	}

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		// the left button alone starts a drag (the base class took every button); the pointer is captured to the pressed
		// element by the input manager, so the moves keep coming while the button is held
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		e.Handled = true;
		e.PreventGestureRecognition();
		Record(e);
		_pointer = e.Pointer;
		IsDragging = true;
		PseudoClasses.Set(":pressed", true);
		RaiseEvent(new VectorEventArgs { RoutedEvent = DragStartedEvent, Vector = (Vector)e.GetPosition(this) });
	}

	/// <inheritdoc/>
	protected override void OnPointerMoved(PointerEventArgs e)
	{
		if (!IsDragging)
		{
			return;
		}
		Point before = PointerPosition;
		Record(e);
		RaiseEvent(new VectorEventArgs { RoutedEvent = DragDeltaEvent, Vector = PointerPosition - before });
	}

	/// <inheritdoc/>
	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		if (!IsDragging)
		{
			return;
		}
		e.Handled = true;
		Record(e);
		Complete(e.GetPosition(this));
	}

	/// <inheritdoc/>
	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		// the capture went elsewhere (a window switch, a popup): the drag ends where it is, as a release would end it
		if (IsDragging)
		{
			Complete(PointerPosition);
		}
		base.OnPointerCaptureLost(e);
	}

	/// <inheritdoc/>
	protected override void OnLostFocus(FocusChangedEventArgs e)
	{
		base.OnLostFocus(e);
		DragAnchors = null;
	}

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == IsSelectedMarkerProperty)
		{
			PseudoClasses.Set(":selected", change.GetNewValue<bool>());
		}
		else if (change.Property == StrokeThicknessProperty)
		{
			RaisePropertyChanged(HaloThicknessProperty, change.GetOldValue<double>() + HaloWidth, change.GetNewValue<double>() + HaloWidth);
		}
	}

	/// <summary>Ends the drag by a release or a lost capture and raises the completion.</summary>
	private void Complete(Point at)
	{
		IsDragging = false;
		_pointer = null;
		PseudoClasses.Set(":pressed", false);
		RaiseEvent(new VectorEventArgs { RoutedEvent = DragCompletedEvent, Vector = (Vector)at });
	}

	/// <summary>Notes the pointer's position in the parent's coordinates (the thumb's own when it has no parent yet) and the modifiers.</summary>
	private void Record(PointerEventArgs e)
	{
		PointerPosition = e.GetPosition(Parent as Visual ?? this);
		PointerModifiers = e.KeyModifiers;
	}
}
