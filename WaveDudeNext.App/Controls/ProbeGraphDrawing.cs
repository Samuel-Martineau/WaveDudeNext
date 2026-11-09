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
using Avalonia.Media;
using Avalonia.Media.Immutable;
using WaveDudeNext.Probes;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// Draws a probe graph box from its <see cref="ProbeGraphLayout"/> with Avalonia drawing primitives: the translucent
/// dark box, the axes with their labels, the curve and the title strip. Used by the preview's
/// <see cref="ProbeGraphOverlay"/> (screen pixels, the box scaled with the picture); the export's painter draws the
/// same layout on SkiaSharp. Colours are upstream's, fixed, not themed: the box sits on the picture. The brushes and
/// pens are immutable, the text cache is the UI thread's.
/// </summary>
public static class ProbeGraphDrawing
{
	/// <summary>The fixed label size in pixels (text does not scale with the box).</summary>
	public const double FontSize = 11.0;

	/// <summary>The graph font: the application's default family, the platform's (Segoe UI on Windows), as the UI's text.</summary>
	public static readonly Typeface Typeface = Typeface.Default;

	/// <summary>The box's fill, upstream's ARGB(235, 14, 16, 22).</summary>
	public static readonly Color BackgroundColor = Color.FromArgb(235, 14, 16, 22);

	private static readonly IImmutableBrush BackgroundBrush = new ImmutableSolidColorBrush(BackgroundColor);
	private static readonly IImmutableBrush BorderBrush = new ImmutableSolidColorBrush(Color.FromArgb(170, 200, 200, 210));
	private static readonly IImmutableBrush GridBrush = new ImmutableSolidColorBrush(Color.FromArgb(46, 255, 255, 255));
	private static readonly IImmutableBrush ZeroBrush = new ImmutableSolidColorBrush(Color.FromArgb(120, 255, 255, 255));
	private static readonly IImmutableBrush FrameBrush = new ImmutableSolidColorBrush(Color.FromArgb(130, 200, 200, 210));
	private static readonly IImmutableBrush LabelBrush = new ImmutableSolidColorBrush(Color.FromArgb(225, 225, 225, 230));
	private static readonly IImmutableBrush TitleBrush = new ImmutableSolidColorBrush(Color.FromArgb(240, 240, 240, 245));
	private static readonly IImmutableBrush CurveBrush = new ImmutableSolidColorBrush(Color.FromArgb(255, 255, 208, 64));
	private static readonly Dictionary<(IImmutableBrush Brush, double Thickness), IPen> Pens = [];
	private static readonly Dictionary<double, IPen> CurvePens = [];

	// cached, as upstream did: built afresh, a draw of the boxes cost over 2 ms per picture, mostly the text, and the
	// boxes are drawn on every position change and probe update; the readout changes per frame, so the cache is cleared
	// when it grows large; the culture is part of the key
	private static readonly Dictionary<(string Text, double Size, IBrush Brush, string Culture), FormattedText> TextCache = [];
	private const int TextCacheLimit = 4096;

	/// <summary>
	/// The text metrics of the graph font, for <see cref="ProbeGraph.Layout"/>: the line height and a measure of label
	/// widths. Avalonia lays text out in DIPs and scales it to the device itself, so the metrics are the same at every
	/// scale; the parameter names the scale for symmetry with the hairline arithmetic and is not used.
	/// </summary>
	public static ProbeGraphStyle MakeStyle(double scale)
	{
		_ = scale;
		float lineHeight = (float)Formatted("0", FontSize, LabelBrush).Height;
		return new ProbeGraphStyle((float)FontSize, lineHeight, text => (float)Formatted(text, FontSize, LabelBrush).Width);
	}

	/// <summary>A line a whole number of device pixels thick, in DIPs: one pixel at 100 %, two at 150 and 200 %.</summary>
	public static double Hairline(double scale) => Math.Max(1, Math.Round(scale)) / scale;

	/// <summary>Draws one box from its layout, with the origin at the box's top-left corner.</summary>
	/// <param name="dc">The drawing context.</param>
	/// <param name="layout">The box's geometry and text.</param>
	/// <param name="border">The border pen for a selected box, or null for the usual one.</param>
	/// <param name="scale">The device pixels per DIP, for the hairline.</param>
	public static void DrawBox(DrawingContext dc, ProbeGraphLayout layout, IPen? border, double scale)
	{
		double w = layout.Width;
		double h = layout.Height;
		// a line is a whole number of device pixels thick, and every edge drawn below sits on a whole pixel plus half the
		// thickness, so the box's lines come out whole at 100 % and in an export; Avalonia has no guideline set, so at an
		// odd zoom a line may straddle two pixels, which upstream's guidelines avoided
		double t = Hairline(scale);
		double half = t / 2;
		dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
		dc.DrawRectangle(null, border ?? PenOf(BorderBrush, t), new Rect(half, half, w - t, h - t));
		double fontPx = layout.FontSize;
		if (layout.HasPlot)
		{
			double plotLeft = layout.PlotX;
			double plotTop = layout.PlotY;
			double plotRight = layout.PlotX + layout.PlotWidth;
			double plotBottom = layout.PlotY + layout.PlotHeight;
			// value axis: grid lines (the zero line brighter) with right-aligned labels in the left margin
			foreach (GraphTick tick in layout.YTicks)
			{
				double y = Math.Round(tick.Position) + half;
				dc.DrawLine(PenOf(tick.IsZero ? ZeroBrush : GridBrush, t), new Point(plotLeft, y), new Point(plotRight, y));
				if (tick.Label.Length == 0)
				{
					continue;
				}
				// right-aligned by position, not by a text width limit, which would wrap the label onto a second line
				FormattedText label = Formatted(tick.Label, fontPx, LabelBrush);
				double lx = Math.Max(layout.LabelLeft, plotLeft - ProbeGraph.LabelGap - label.Width);
				dc.DrawText(label, new Point(lx, Math.Clamp(tick.Position - label.Height / 2, plotTop - label.Height / 2, plotBottom - label.Height / 2)));
			}
			// step axis (point probe) or position on the line (line probe): labels centred below the plot, kept inside the box
			foreach (GraphTick tick in layout.XTicks)
			{
				double x = Math.Round(tick.Position) + half;
				dc.DrawLine(PenOf(GridBrush, t), new Point(x, plotTop), new Point(x, plotBottom));
				if (tick.Label.Length == 0)
				{
					continue;
				}
				FormattedText label = Formatted(tick.Label, fontPx, LabelBrush);
				double lx = Math.Clamp(tick.Position - label.Width / 2, ProbeGraph.EdgePad, Math.Max(ProbeGraph.EdgePad, w - label.Width - ProbeGraph.EdgePad));
				dc.DrawText(label, new Point(lx, plotBottom + 2));
			}
			dc.DrawRectangle(null, PenOf(FrameBrush, t), new Rect(plotLeft + half, plotTop + half, layout.PlotWidth, layout.PlotHeight));
			DrawCurve(dc, layout);
		}
		// the title strip: the readout right-aligned in its own colour, the title in the remaining width
		double readoutWidth = 0;
		if (layout.Readout.Length > 0)
		{
			FormattedText readout = Formatted(layout.Readout, fontPx, CurveBrush);
			readoutWidth = readout.Width;
			dc.DrawText(readout, new Point(w - readoutWidth - 8, layout.TitleTop));
		}
		// the title is built afresh: its width limit and trimming are set on the object, which a cached one would carry over
		var title = new FormattedText(layout.Title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, fontPx, TitleBrush)
		{
			MaxLineCount = 1,
			MaxTextWidth = Math.Max(10, w - readoutWidth - 16),
			Trimming = TextTrimming.CharacterEllipsis,
		};
		dc.DrawText(title, new Point(ProbeGraph.EdgePad + 3, layout.TitleTop));
	}

	/// <summary>An immutable pen of a brush and a thickness, made once per pair; the thicknesses are the hairlines of the scales in use.</summary>
	private static IPen PenOf(IImmutableBrush brush, double thickness)
	{
		if (!Pens.TryGetValue((brush, thickness), out IPen? pen))
		{
			pen = new ImmutablePen(brush, thickness);
			Pens[(brush, thickness)] = pen;
		}
		return pen;
	}

	/// <summary>Strokes the curve clipped to the plot area; a single point is a small dot.</summary>
	private static void DrawCurve(DrawingContext dc, ProbeGraphLayout layout)
	{
		if (layout.Curve.Length == 0)
		{
			return;
		}
		if (layout.Curve.Length == 1)
		{
			dc.DrawEllipse(CurveBrush, null, new Point(layout.Curve[0].X, layout.Curve[0].Y), 2, 2);
			return;
		}
		var geometry = new StreamGeometry();
		using (StreamGeometryContext context = geometry.Open())
		{
			context.BeginFigure(new Point(layout.Curve[0].X, layout.Curve[0].Y), false);
			for (int i = 1; i < layout.Curve.Length; i++)
			{
				context.LineTo(new Point(layout.Curve[i].X, layout.Curve[i].Y));
			}
			context.EndFigure(false);
		}
		using (dc.PushClip(new Rect(layout.PlotX, layout.PlotY, layout.PlotWidth, layout.PlotHeight)))
		{
			dc.DrawGeometry(null, CurvePen(layout.LineWidth), geometry);
		}
	}

	/// <summary>One line of text in the graph font, cached by its text, size, brush and culture.</summary>
	public static FormattedText Formatted(string text, double size, IBrush brush)
	{
		var key = (text, size, brush, CultureInfo.CurrentUICulture.Name);
		if (!TextCache.TryGetValue(key, out FormattedText? formatted))
		{
			if (TextCache.Count >= TextCacheLimit)
			{
				TextCache.Clear();
			}
			formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, size, brush) { MaxLineCount = 1 };
			TextCache[key] = formatted;
		}
		return formatted;
	}

	private static IPen CurvePen(double width)
	{
		if (!CurvePens.TryGetValue(width, out IPen? pen))
		{
			pen = new ImmutablePen(CurveBrush, width, lineJoin: PenLineJoin.Round);
			CurvePens[width] = pen;
		}
		return pen;
	}
}
