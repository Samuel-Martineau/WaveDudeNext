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

using SkiaSharp;
using WaveDudeNext.Probes;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// Draws a probe graph box from its <see cref="ProbeGraphLayout"/> with SkiaSharp: the translucent dark box, the axes
/// with their labels, the curve and the title strip, the port of the WPF application's <c>ProbeGraphDrawing</c> with
/// the author's colours (fixed, not themed: the box sits on the picture). Shared by the export's
/// <see cref="ProbeGraphPainter"/> (canvas pixels, rendered into the frame) and, later, the preview's overlay (screen
/// pixels, the box scaled with the picture). The text is set in SkiaSharp's default typeface
/// (<see cref="SKTypeface.Default"/>: on Linux the sans-serif face fontconfig names, on Windows Segoe UI) at 11 px.
/// The WPF version named Segoe UI, which exists only on Windows; a box laid out with one typeface and drawn with another
/// would put its labels in the wrong columns, so <see cref="MakeStyle"/> measures with the very typeface
/// <see cref="DrawBox"/> draws with. No shared mutable state: every call makes its own fonts and paints and disposes
/// them, so the export thread and the preview may draw at the same time.
/// </summary>
public static class ProbeGraphSkia
{
	/// <summary>The fixed label size in pixels (text does not scale with the box).</summary>
	public const float FontSize = 11f;

	/// <summary>The graph font's typeface: SkiaSharp's default, read-only and shared.</summary>
	public static SKTypeface Typeface => SKTypeface.Default;

	/// <summary>What WPF's CharacterEllipsis trimming appended to a cut title.</summary>
	private const string Ellipsis = "…";

	// the author's colours, from ProbeGraphDrawing's ARGB brushes; SKColor takes (r, g, b, a)
	private static readonly SKColor BackgroundColor = new(14, 16, 22, 235);
	private static readonly SKColor BorderColor = new(200, 200, 210, 170);
	private static readonly SKColor GridColor = new(255, 255, 255, 46);
	private static readonly SKColor ZeroColor = new(255, 255, 255, 120);
	private static readonly SKColor FrameColor = new(200, 200, 210, 130);
	private static readonly SKColor LabelColor = new(225, 225, 230, 225);
	private static readonly SKColor TitleColor = new(240, 240, 245, 240);
	private static readonly SKColor CurveColor = new(255, 208, 64, 255);

	/// <summary>
	/// The text metrics of the graph font for <see cref="ProbeGraph.Layout"/>: the line height (the font's line
	/// spacing, what WPF's FormattedText reported as the height of "0") and a measure of label widths (advance widths),
	/// both in the box's own units. Skia's metrics at 11 px do not move with the DPI (a preview scales its canvas
	/// instead), so <paramref name="pixelsPerDip"/> only names the scale the style was made for, as the WPF signature did.
	/// </summary>
	/// <param name="pixelsPerDip">The scale the box will be drawn at: 1 for an export, the DPI scale of a preview.</param>
	public static ProbeGraphStyle MakeStyle(float pixelsPerDip)
	{
		_ = pixelsPerDip;
		using var font = new SKFont(Typeface, FontSize);
		return new ProbeGraphStyle(FontSize, font.Spacing, MeasureWidth);
	}

	/// <summary>The advance width of one label in the graph font, with a font of its own so any thread may measure.</summary>
	private static float MeasureWidth(string text)
	{
		if (text.Length == 0)
		{
			return 0f;
		}
		using var font = new SKFont(Typeface, FontSize);
		return font.MeasureText(text);
	}

	/// <summary>Draws one box from its layout, with the origin at the box's top-left corner.</summary>
	/// <param name="canvas">The canvas, translated to the box (and scaled for the screen by a preview).</param>
	/// <param name="layout">The box's geometry and text.</param>
	/// <param name="selectionBorder">The border colour of a selected box, or null for the usual one.</param>
	/// <param name="scale">Device pixels per layout unit: 1 for an export, the DPI scale of a preview.</param>
	public static void DrawBox(SKCanvas canvas, ProbeGraphLayout layout, SKColor? selectionBorder, float scale)
	{
		float w = layout.Width;
		float h = layout.Height;
		// a line is a whole number of device pixels thick (one at 100 %, two at 150 and 200 %) and every line drawn
		// below is centred on a half-pixel row or column of that grid, which Skia renders without smearing it over two,
		// so the box's lines come out whole at a UI zoom and on a 150 % monitor as at 100 % (the user's rule, which WPF
		// met with guidelines); at scale 1, in an export, the arithmetic is the WPF one pixel for pixel
		float t = MathF.Max(1f, MathF.Round(scale)) / scale;
		float half = t / 2;
		using var font = new SKFont(Typeface, layout.FontSize);
		using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
		using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = t };
		// Skia draws text from its baseline, WPF from the line's top: the ascent (negative, upwards) converts
		float ascent = font.Metrics.Ascent;
		float lineHeight = font.Spacing;
		fill.Color = BackgroundColor;
		canvas.DrawRect(0, 0, w, h, fill);
		stroke.Color = selectionBorder ?? BorderColor;
		canvas.DrawRect(half, half, w - t, h - t, stroke);
		if (layout.HasPlot)
		{
			float plotLeft = layout.PlotX;
			float plotTop = layout.PlotY;
			float plotRight = layout.PlotX + layout.PlotWidth;
			float plotBottom = layout.PlotY + layout.PlotHeight;
			// value axis: grid lines (the zero line brighter) with right-aligned labels in the left margin
			foreach (GraphTick tick in layout.YTicks)
			{
				float y = MathF.Round(tick.Position) + half;
				stroke.Color = tick.IsZero ? ZeroColor : GridColor;
				canvas.DrawLine(plotLeft, y, plotRight, y, stroke);
				if (tick.Label.Length == 0)
				{
					continue;
				}
				// right-aligned by position, kept inside the plot's rows
				float labelWidth = font.MeasureText(tick.Label);
				float lx = MathF.Max(layout.LabelLeft, plotLeft - ProbeGraph.LabelGap - labelWidth);
				float top = Math.Clamp(tick.Position - lineHeight / 2, plotTop - lineHeight / 2, plotBottom - lineHeight / 2);
				fill.Color = LabelColor;
				canvas.DrawText(tick.Label, lx, top - ascent, SKTextAlign.Left, font, fill);
			}
			// step axis (point probe) or position on the line (line probe): labels centred below the plot, kept inside the box
			foreach (GraphTick tick in layout.XTicks)
			{
				float x = MathF.Round(tick.Position) + half;
				stroke.Color = GridColor;
				canvas.DrawLine(x, plotTop, x, plotBottom, stroke);
				if (tick.Label.Length == 0)
				{
					continue;
				}
				float labelWidth = font.MeasureText(tick.Label);
				float lx = Math.Clamp(tick.Position - labelWidth / 2, ProbeGraph.EdgePad, MathF.Max(ProbeGraph.EdgePad, w - labelWidth - ProbeGraph.EdgePad));
				fill.Color = LabelColor;
				canvas.DrawText(tick.Label, lx, plotBottom + 2 - ascent, SKTextAlign.Left, font, fill);
			}
			stroke.Color = FrameColor;
			canvas.DrawRect(plotLeft + half, plotTop + half, layout.PlotWidth, layout.PlotHeight, stroke);
			DrawCurve(canvas, layout);
		}
		// the title strip: the readout right-aligned in its own colour, the title in the remaining width
		float readoutWidth = 0f;
		if (layout.Readout.Length > 0)
		{
			readoutWidth = font.MeasureText(layout.Readout);
			fill.Color = CurveColor;
			canvas.DrawText(layout.Readout, w - readoutWidth - 8, layout.TitleTop - ascent, SKTextAlign.Left, font, fill);
		}
		fill.Color = TitleColor;
		string title = Trim(layout.Title, font, MathF.Max(10, w - readoutWidth - 16));
		canvas.DrawText(title, ProbeGraph.EdgePad + 3, layout.TitleTop - ascent, SKTextAlign.Left, font, fill);
	}

	/// <summary>Strokes the curve clipped to the plot area; a single point is a small dot (unclipped, as in WPF).</summary>
	private static void DrawCurve(SKCanvas canvas, ProbeGraphLayout layout)
	{
		if (layout.Curve.Length == 0)
		{
			return;
		}
		using var paint = new SKPaint { IsAntialias = true, Color = CurveColor };
		if (layout.Curve.Length == 1)
		{
			paint.Style = SKPaintStyle.Fill;
			canvas.DrawCircle(layout.Curve[0].X, layout.Curve[0].Y, 2f, paint);
			return;
		}
		using var path = new SKPath();
		path.MoveTo(layout.Curve[0].X, layout.Curve[0].Y);
		for (int i = 1; i < layout.Curve.Length; i++)
		{
			path.LineTo(layout.Curve[i].X, layout.Curve[i].Y);
		}
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = layout.LineWidth;
		paint.StrokeJoin = SKStrokeJoin.Round;
		canvas.Save();
		canvas.ClipRect(SKRect.Create(layout.PlotX, layout.PlotY, layout.PlotWidth, layout.PlotHeight));
		canvas.DrawPath(path, paint);
		canvas.Restore();
	}

	/// <summary>
	/// The title cut to <paramref name="maxWidth"/> with an ellipsis, what WPF's MaxTextWidth with CharacterEllipsis
	/// did: as many characters as fit before the ellipsis, the ellipsis alone when not even one does.
	/// </summary>
	private static string Trim(string text, SKFont font, float maxWidth)
	{
		if (text.Length == 0 || font.MeasureText(text) <= maxWidth)
		{
			return text;
		}
		float ellipsisWidth = font.MeasureText(Ellipsis);
		if (ellipsisWidth >= maxWidth)
		{
			return Ellipsis;
		}
		int count = font.BreakText(text, maxWidth - ellipsisWidth, out _);
		return text[..Math.Clamp(count, 0, text.Length)] + Ellipsis;
	}
}
