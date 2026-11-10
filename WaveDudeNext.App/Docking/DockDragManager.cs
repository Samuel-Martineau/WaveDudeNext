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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Docking;

/// <summary>
/// Pointer-driven pane moving for a <see cref="DockHost"/>. A title bar or tab arms a drag on
/// pointer press; once the pointer travels past the platform's tap size the host captures the
/// pointer, a <see cref="DockDropAdorner"/> shows the drop targets, and releasing over one of
/// them performs the move. Avalonia routes a captured pointer's events to the host itself, and the host's tunnelling
/// handlers see the moves before capture too, so one pair of handlers covers both phases.
/// </summary>
internal sealed class DockDragManager
{
	/// <summary>
	/// Movement that still counts as a click, when the platform does not say: WPF's SystemParameters minimum drag
	/// distance, which upstream reads. Avalonia offers the platform's tap size per pointer type instead, and that is used
	/// when a top level is at hand.
	/// </summary>
	private const double FallbackDragThreshold = 4;

	private readonly DockHost _host;
	private DockGroupControl? _source;
	private DockItem? _sourceItem;
	private Point _armPoint;
	private bool _armed;
	private bool _dragging;
	private bool _finishing;
	private DockDropAdorner? _adorner;
	private AdornerLayer? _layer;
	private IPointer? _pointer;

	public DockDragManager(DockHost host)
	{
		_host = host;
		_host.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
		_host.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
		_host.PointerCaptureLost += OnPointerCaptureLost;
	}

	/// <summary>
	/// Records a potential drag. <paramref name="item"/> is the single tab being dragged, or
	/// null when the whole group (title bar) is dragged.
	/// </summary>
	public void Arm(DockGroupControl source, DockItem? item, Point hostPoint)
	{
		_source = source;
		_sourceItem = item;
		_armPoint = hostPoint;
		_armed = true;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs e)
	{
		if (!_armed || _source == null)
		{
			return;
		}
		if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
		{
			Finish();
			return;
		}
		Point point = e.GetPosition(_host);
		if (!_dragging)
		{
			Size threshold = _host.GetPlatformSettings()?.GetTapSize(e.Pointer.Type)
				?? new Size(FallbackDragThreshold, FallbackDragThreshold);
			if (Math.Abs(point.X - _armPoint.X) < threshold.Width && Math.Abs(point.Y - _armPoint.Y) < threshold.Height)
			{
				return;
			}
			AdornerLayer? layer = AdornerLayer.GetAdornerLayer(_host);
			if (layer == null)
			{
				Finish();
				return;
			}
			// State is complete before capturing (upstream's rule: WPF re-raised a move synchronously inside the capture
			// call; Avalonia does not, but the order costs nothing and keeps the two implementations alike).
			_dragging = true;
			string title = _sourceItem?.Title ?? _source.Group.ActiveItem?.Title ?? string.Empty;
			_adorner = new DockDropAdorner(_host, title);
			AdornerLayer.SetAdornedElement(_adorner, _host);
			// the ghost label follows the pointer to +14,+14 and may cross the host's edge
			AdornerLayer.SetIsClipEnabled(_adorner, false);
			layer.Children.Add(_adorner);
			_layer = layer;
			_pointer = e.Pointer;
			e.Pointer.Capture(_host);
			if (!ReferenceEquals(e.Pointer.Captured, _host))
			{
				Finish();
				return;
			}
		}
		var hovered = _host.HitNode(_host.InputHitTest(point) as Visual);
		_adorner!.Update(point, hovered, _source.Group, _sourceItem);
		e.Handled = true;
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (!_armed || e.InitialPressMouseButton != MouseButton.Left)
		{
			return;
		}
		if (!_dragging)
		{
			Finish();
			return;
		}
		DropTarget target = _adorner!.CurrentTarget;
		DockGroupControl source = _source!;
		DockItem? item = _sourceItem;
		Finish();
		Perform(target, source.Group, item);
		e.Handled = true;
	}

	/// <summary>
	/// Ends the drag when the host loses the capture it took for it (another window activated, say). Only the host's own
	/// loss counts: the tab strip is a list box that may hold the pointer at the press on a tab and loses it to the host
	/// when the drag starts, and upstream's own crash came from taking a descendant's loss for the drag's end (2026-11-06).
	/// The release the host performs itself arrives here too, inside <see cref="Finish"/>, and is ignored by its guard.
	/// </summary>
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		if (_dragging && !_finishing && ReferenceEquals(e.Source, _host))
		{
			Finish();
		}
	}

	private void Finish()
	{
		if (_finishing)
		{
			return;
		}
		_finishing = true;
		_armed = false;
		_dragging = false;
		if (_adorner != null)
		{
			_layer?.Children.Remove(_adorner);
			_adorner = null;
			_layer = null;
		}
		if (_pointer != null)
		{
			if (ReferenceEquals(_pointer.Captured, _host))
			{
				_pointer.Capture(null);
			}
			_pointer = null;
		}
		_source = null;
		_sourceItem = null;
		_finishing = false;
	}

	private void Perform(DropTarget target, DockGroup group, DockItem? item)
	{
		if (target.Kind == DropKind.None)
		{
			return;
		}
		bool wholeGroup = item == null || group.Items.Count == 1;
		switch (target.Kind)
		{
			case DropKind.HostEdge:
				if (wholeGroup)
				{
					_host.DockGroupToEdge(group, target.Side);
				}
				else
				{
					_host.DockToEdge(item!, target.Side);
				}
				break;
			case DropKind.NodeCenter:
				if (wholeGroup)
				{
					_host.MergeGroup(group, (DockGroup)target.Node!);
				}
				else
				{
					_host.MoveToGroup(item!, (DockGroup)target.Node!);
				}
				break;
			case DropKind.NodeSide:
				if (wholeGroup)
				{
					_host.DockGroupRelative(group, target.Node!, target.Side);
				}
				else
				{
					_host.DockRelative(item!, target.Node!, target.Side);
				}
				break;
		}
	}
}

/// <summary>What a drop would do.</summary>
internal enum DropKind
{
	/// <summary>No indicator under the pointer.</summary>
	None,
	/// <summary>New pane at the outer edge of the host on <see cref="DropTarget.Side"/>.</summary>
	HostEdge,
	/// <summary>Tab into the group <see cref="DropTarget.Node"/>.</summary>
	NodeCenter,
	/// <summary>New pane next to <see cref="DropTarget.Node"/>, on <see cref="DropTarget.Side"/> of it.</summary>
	NodeSide,
}

/// <summary>A resolved drop target.</summary>
internal readonly record struct DropTarget(DropKind Kind, DockSide Side, DockNode? Node);
