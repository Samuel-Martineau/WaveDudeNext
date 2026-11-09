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
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Docking;

/// <summary>
/// Overlay drawn on the <see cref="DockHost"/> while a pane is dragged: four edge indicators,
/// a five-way cluster (centre plus four sides) on the hovered pane or document area, a
/// translucent preview of the drop area and a ghost label following the pointer. It sits on the window's adorner
/// layer, arranged over the host, and is not hit-testable, so the pointer keeps reaching the panes beneath. Brushes
/// come from the theme dictionaries.
/// </summary>
internal sealed class DockDropAdorner : Control
{
	private const double IndicatorSize = 32;
	private const double EdgeInset = 12;
	private const double ClusterOffset = 36;
	private const double GlyphSize = 10;
	/// <summary>Fraction of the hovered pane the preview covers for a side drop.</summary>
	private const double SidePreviewShare = 1.0 / 3.0;
	/// <summary>Fraction of the host the preview covers for an edge drop.</summary>
	private const double EdgePreviewShare = 0.25;

	private readonly DockHost _host;
	private readonly string _title;
	private readonly List<Indicator> _indicators = new();
	private Rect? _preview;
	private Point _pointer;

	public DockDropAdorner(DockHost host, string title)
	{
		_host = host;
		_title = title;
		IsHitTestVisible = false;
	}

	/// <summary>Target under the pointer after the last <see cref="Update"/>.</summary>
	public DropTarget CurrentTarget { get; private set; }

	/// <summary>Recomputes indicators and the hit target for the given pointer position, in the host's coordinates.</summary>
	public void Update(Point pointer, (DockNode Node, Control Element)? hovered, DockGroup sourceGroup, DockItem? sourceItem)
	{
		_pointer = pointer;
		_indicators.Clear();
		_preview = null;
		CurrentTarget = default;

		double w = _host.Bounds.Width;
		double h = _host.Bounds.Height;
		AddIndicator(new Point(EdgeInset + IndicatorSize / 2, h / 2), new DropTarget(DropKind.HostEdge, DockSide.Left, null), DockSide.Left);
		AddIndicator(new Point(w - EdgeInset - IndicatorSize / 2, h / 2), new DropTarget(DropKind.HostEdge, DockSide.Right, null), DockSide.Right);
		AddIndicator(new Point(w / 2, EdgeInset + IndicatorSize / 2), new DropTarget(DropKind.HostEdge, DockSide.Top, null), DockSide.Top);
		AddIndicator(new Point(w / 2, h - EdgeInset - IndicatorSize / 2), new DropTarget(DropKind.HostEdge, DockSide.Bottom, null), DockSide.Bottom);

		Rect? hoveredBounds = null;
		if (hovered is { } hit)
		{
			Point origin = hit.Element.TranslatePoint(new Point(), _host) ?? default;
			Rect bounds = new(origin, hit.Element.Bounds.Size);
			hoveredBounds = bounds;
			Point centre = new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
			bool wholeGroup = sourceItem == null || sourceGroup.Items.Count == 1;
			bool isSource = ReferenceEquals(hit.Node, sourceGroup);

			// tabbing into a group: not into the document area, not into the group the item already sits in
			if (hit.Node is DockGroup && !isSource)
			{
				AddIndicator(centre, new DropTarget(DropKind.NodeCenter, DockSide.Left, hit.Node), null);
			}
			// splitting next to the hovered node: pointless for a whole group dropped on itself
			if (!(isSource && wholeGroup))
			{
				AddIndicator(new Point(centre.X - ClusterOffset, centre.Y), new DropTarget(DropKind.NodeSide, DockSide.Left, hit.Node), DockSide.Left);
				AddIndicator(new Point(centre.X + ClusterOffset, centre.Y), new DropTarget(DropKind.NodeSide, DockSide.Right, hit.Node), DockSide.Right);
				AddIndicator(new Point(centre.X, centre.Y - ClusterOffset), new DropTarget(DropKind.NodeSide, DockSide.Top, hit.Node), DockSide.Top);
				AddIndicator(new Point(centre.X, centre.Y + ClusterOffset), new DropTarget(DropKind.NodeSide, DockSide.Bottom, hit.Node), DockSide.Bottom);
			}
		}

		foreach (Indicator indicator in _indicators)
		{
			if (indicator.Bounds.Contains(pointer))
			{
				CurrentTarget = indicator.Target;
				_preview = PreviewRect(indicator.Target, hoveredBounds, w, h);
				break;
			}
		}
		InvalidateVisual();
	}

	private void AddIndicator(Point centre, DropTarget target, DockSide? arrow)
	{
		var bounds = new Rect(centre.X - IndicatorSize / 2, centre.Y - IndicatorSize / 2, IndicatorSize, IndicatorSize);
		_indicators.Add(new Indicator(bounds, target, arrow));
	}

	private static Rect? PreviewRect(DropTarget target, Rect? hovered, double w, double h)
	{
		switch (target.Kind)
		{
			case DropKind.HostEdge:
				return SideRect(new Rect(0, 0, w, h), target.Side, EdgePreviewShare);
			case DropKind.NodeCenter:
				return hovered;
			case DropKind.NodeSide:
				return hovered is { } b ? SideRect(b, target.Side, SidePreviewShare) : null;
			default:
				return null;
		}
	}

	private static Rect SideRect(Rect b, DockSide side, double share) => side switch
	{
		DockSide.Left => new Rect(b.X, b.Y, b.Width * share, b.Height),
		DockSide.Right => new Rect(b.X + b.Width * (1 - share), b.Y, b.Width * share, b.Height),
		DockSide.Top => new Rect(b.X, b.Y, b.Width, b.Height * share),
		_ => new Rect(b.X, b.Y + b.Height * (1 - share), b.Width, b.Height * share),
	};

	/// <inheritdoc/>
	public override void Render(DrawingContext dc)
	{
		IBrush indicatorBackground = Resource("DropIndicatorBackground", Brushes.DimGray);
		IBrush indicatorBorder = Resource("DropIndicatorBorder", Brushes.Gray);
		IBrush glyph = Resource("DropIndicatorGlyph", Brushes.White);
		IBrush hot = Resource("DropIndicatorHot", Brushes.DodgerBlue);
		IBrush previewFill = Resource("DropPreviewFill", new SolidColorBrush(Color.FromArgb(0x60, 0, 0x7A, 0xCC)));
		IBrush previewBorder = Resource("DropPreviewBorder", Brushes.DodgerBlue);

		if (_preview is { Width: > 0, Height: > 0 } preview)
		{
			dc.DrawRectangle(previewFill, new Pen(previewBorder, 2), preview);
		}

		foreach (Indicator indicator in _indicators)
		{
			bool isHot = indicator.Bounds.Contains(_pointer);
			dc.DrawRectangle(indicatorBackground, new Pen(isHot ? hot : indicatorBorder, 1), indicator.Bounds, 2, 2);
			DrawGlyph(dc, indicator, isHot ? hot : glyph);
		}

		// the ghost label in the host's font (the platform's default, Segoe UI on Windows as in the WPF head) at 12 px, as the tabs
		var text = new FormattedText(_title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
			new Typeface(TextElement.GetFontFamily(_host)), 12, glyph);
		var ghost = new Rect(_pointer.X + 14, _pointer.Y + 14, text.Width + 16, text.Height + 6);
		dc.DrawRectangle(indicatorBackground, new Pen(indicatorBorder, 1), ghost, 2, 2);
		dc.DrawText(text, new Point(ghost.X + 8, ghost.Y + 3));
	}

	private static void DrawGlyph(DrawingContext dc, Indicator indicator, IBrush brush)
	{
		Rect b = indicator.Bounds;
		double x = b.X + (b.Width - GlyphSize) / 2;
		double y = b.Y + (b.Height - GlyphSize) / 2;
		if (indicator.Arrow == null)
		{
			dc.DrawRectangle(null, new Pen(brush, 1.5), new Rect(x + 0.5, y + 0.5, GlyphSize - 1, GlyphSize - 1));
			return;
		}
		Point a, tip, c;
		switch (indicator.Arrow.Value)
		{
			case DockSide.Left:
				a = new Point(GlyphSize - 1, 0);
				tip = new Point(1, GlyphSize / 2);
				c = new Point(GlyphSize - 1, GlyphSize);
				break;
			case DockSide.Right:
				a = new Point(1, 0);
				tip = new Point(GlyphSize - 1, GlyphSize / 2);
				c = new Point(1, GlyphSize);
				break;
			case DockSide.Top:
				a = new Point(0, GlyphSize - 1);
				tip = new Point(GlyphSize / 2, 1);
				c = new Point(GlyphSize, GlyphSize - 1);
				break;
			default:
				a = new Point(0, 1);
				tip = new Point(GlyphSize / 2, GlyphSize - 1);
				c = new Point(GlyphSize, 1);
				break;
		}
		var geometry = new StreamGeometry();
		using (StreamGeometryContext ctx = geometry.Open())
		{
			ctx.BeginFigure(a, true);
			ctx.LineTo(tip, true);
			ctx.LineTo(c, true);
			ctx.EndFigure(true);
		}
		using (dc.PushTransform(Matrix.CreateTranslation(x, y)))
		{
			dc.DrawGeometry(brush, null, geometry);
		}
	}

	/// <summary>A theme brush by key, for the host's theme variant (the brushes live in the variant dictionaries), else the fallback.</summary>
	private IBrush Resource(string key, IBrush fallback) =>
		_host.TryFindResource(key, _host.ActualThemeVariant, out object? value) && value is IBrush brush ? brush : fallback;

	private readonly record struct Indicator(Rect Bounds, DropTarget Target, DockSide? Arrow);
}
