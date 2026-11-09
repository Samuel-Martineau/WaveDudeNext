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

using System.Collections.Immutable;
using System.Globalization;
using SkiaSharp;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// Draws the probes into exported frames at canvas scale, the port of the WPF application's painter of the same name:
/// the markers in the author's style (the strokes by <see cref="ProbeMarkers.DrawStrokes"/> on the pixels, the numbers
/// as bold text with a drop shadow rendered here) and the graph boxes as the preview shows them (for every active
/// probe of a layer, the box where the probe says, with the run's recording as it stood at the frame,
/// <see cref="ProbeGraphData.At"/>). Built before the export from the project (the probes are cloned, so edits during
/// the export move nothing), the run's <see cref="RunSpec"/> (the frame size, the steps and the frame interval) and its
/// traces; <see cref="For"/> gives the export job a decoration that runs on the export's render thread, where each box
/// and label is rendered through <see cref="ProbeGraphSkia"/> into an <see cref="SKBitmap"/> of its size and blended,
/// premultiplied, over the frame's pixels. Boxes are 1:1 canvas pixels with 11 px text at any frame height, as upstream
/// exports them. Probes are numbered in project order, active ones only, as the author numbered his run list. The
/// numbers' bold face is the default typeface's bold cut, or the default typeface emboldened by Skia when the system
/// has no bold cut of it.
/// </summary>
public sealed class ProbeGraphPainter
{
	/// <summary>The drop shadow under a number (ProbeGraphPainter's ShadowBrush, ARGB 220,0,0,0).</summary>
	private static readonly SKColor Shadow = new(0, 0, 0, 220);

	/// <summary>The bold face of the default typeface; <see cref="SKTypeface.FromFamilyName(string, SKFontStyle)"/> falls back to a regular face when no bold cut exists, which <see cref="DrawLabel"/> emboldens.</summary>
	private static readonly SKTypeface LabelTypeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Bold) ?? SKTypeface.Default;

	private readonly (Probe Probe, ProbeTrace? Trace, int Number)[] _items;
	private readonly bool _graphs;
	private readonly bool _markers;
	private readonly int _every;
	private readonly int _totalSteps;
	private readonly int _width;
	private readonly int _height;
	private readonly ProbeGraphStyle _style = ProbeGraphSkia.MakeStyle(1f);

	/// <param name="project">The project whose active probes are drawn.</param>
	/// <param name="spec">The run exported: its size is the frame size, its steps and frame interval place the data.</param>
	/// <param name="traces">The run's recordings (<see cref="SimulationRun.Traces"/>), one per probe of the spec, in its order.</param>
	/// <param name="graphs">Draw the graph boxes.</param>
	/// <param name="markers">Draw the markers and their numbers.</param>
	public ProbeGraphPainter(Project project, RunSpec spec, IReadOnlyList<ProbeTrace> traces, bool graphs, bool markers)
	{
		_graphs = graphs;
		_markers = markers;
		_items = [.. project.Probes.Where(probe => probe.Active).Select((probe, i) => (probe.Clone(), TraceOf(spec.Probes, traces, probe), i + 1))];
		_every = spec.VisualizeEvery;
		_totalSteps = Math.Max(1, spec.Steps);
		_width = spec.Width;
		_height = spec.Height;
	}

	/// <summary>The decoration for a layer: null when nothing is to be drawn or no active probe sits on it.</summary>
	public Action<int, Memory<int>>? For(ProbeLayer layer) =>
		(_graphs || _markers) && _items.Any(item => item.Probe.Layer == layer) ? (frame, pixels) => Paint(layer, frame, pixels) : null;

	/// <summary>Draws the layer's markers, then its boxes, over one frame. Export thread.</summary>
	private void Paint(ProbeLayer layer, int frame, Memory<int> pixels)
	{
		if (_markers)
		{
			foreach ((Probe probe, _, _) in _items)
			{
				if (probe.Layer == layer)
				{
					ProbeMarkers.DrawStrokes(pixels.Span, _width, _height, probe);
				}
			}
			foreach ((Probe probe, _, int number) in _items)
			{
				if (probe.Layer != layer)
				{
					continue;
				}
				bool line = probe.Kind == ProbeKind.Line;
				DrawLabel(pixels.Span, ProbeMarkers.Label(number, line, endB: false), probe.AX, probe.AY);
				if (line)
				{
					DrawLabel(pixels.Span, ProbeMarkers.Label(number, line, endB: true), probe.BX, probe.BY);
				}
			}
		}
		if (!_graphs)
		{
			return;
		}
		foreach ((Probe probe, ProbeTrace? trace, _) in _items)
		{
			if (probe.Layer != layer)
			{
				continue;
			}
			BoxRect box = ProbeGraph.BoxInCanvas(probe, _width, _height);
			if (box.Width < 1 || box.Height < 1)
			{
				continue;
			}
			ProbeGraphData data = trace != null ? ProbeGraphData.At(trace, frame * _every, frame) : default;
			ProbeGraphLayout layout = ProbeGraph.Layout(probe, data, _totalSteps, box.Width, box.Height, _style, CultureInfo.CurrentCulture);
			if (!layout.IsVisible)
			{
				continue;
			}
			using var bitmap = new SKBitmap(box.Width, box.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
			using (var canvas = new SKCanvas(bitmap))
			{
				canvas.Clear(SKColors.Transparent);
				ProbeGraphSkia.DrawBox(canvas, layout, null, 1f);
			}
			Blend(bitmap, box, pixels.Span, _width, _height);
		}
	}

	/// <summary>A marker's label above and to the right of the cross at (<paramref name="x"/>, <paramref name="y"/>): bold, white over a shadow one pixel further in, like the author's.</summary>
	private void DrawLabel(Span<int> frame, string text, int x, int y)
	{
		float fontPx = ProbeMarkers.FontSize(_height);
		using var font = new SKFont(LabelTypeface, fontPx) { Embolden = !LabelTypeface.IsBold };
		// Skia draws from the baseline; the WPF code placed the text's top at 2 and the shadow's at 3
		float ascent = font.Metrics.Ascent;
		int boxWidth = (int)MathF.Ceiling(font.MeasureText(text)) + 4;
		int boxHeight = (int)MathF.Ceiling(font.Spacing) + 4;
		using var bitmap = new SKBitmap(boxWidth, boxHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
		using (var canvas = new SKCanvas(bitmap))
		using (var paint = new SKPaint { IsAntialias = true, Color = Shadow })
		{
			canvas.Clear(SKColors.Transparent);
			canvas.DrawText(text, 3f, 3f - ascent, SKTextAlign.Left, font, paint);
			paint.Color = SKColors.White;
			canvas.DrawText(text, 2f, 2f - ascent, SKTextAlign.Left, font, paint);
		}
		(int left, int top) = ProbeMarkers.LabelOrigin(x, y, _height);
		Blend(bitmap, new BoxRect(left, top, boxWidth, boxHeight), frame, _width, _height);
	}

	/// <summary>
	/// Composites a premultiplied BGRA bitmap over a frame of packed ARGB pixels at <paramref name="rect"/>'s position,
	/// clipped to the frame: the WPF painter's arithmetic on a Pbgra32 buffer, which Skia's premultiplied BGRA8888
	/// matches byte for byte (B, G, R, A in memory).
	/// </summary>
	/// <param name="box">The rendered box or label, premultiplied BGRA8888.</param>
	/// <param name="rect">Where the bitmap's top-left corner goes; its size is the bitmap's.</param>
	/// <param name="frame">The frame, <paramref name="width"/> × <paramref name="height"/>, modified in place.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	internal static void Blend(SKBitmap box, BoxRect rect, Span<int> frame, int width, int height)
	{
		ReadOnlySpan<byte> bytes = box.GetPixelSpan();
		int rowBytes = box.RowBytes;
		for (int y = 0; y < rect.Height; y++)
		{
			int fy = rect.Y + y;
			if (fy < 0 || fy >= height)
			{
				continue;
			}
			for (int x = 0; x < rect.Width; x++)
			{
				int fx = rect.X + x;
				if (fx < 0 || fx >= width)
				{
					continue;
				}
				int i = y * rowBytes + x * 4;
				int a = bytes[i + 3];
				if (a == 0)
				{
					continue;
				}
				int argb = frame[fy * width + fx];
				int inverse = 255 - a;
				int b = bytes[i] + ((argb & 0xFF) * inverse + 127) / 255;
				int g = bytes[i + 1] + (((argb >> 8) & 0xFF) * inverse + 127) / 255;
				int r = bytes[i + 2] + (((argb >> 16) & 0xFF) * inverse + 127) / 255;
				frame[fy * width + fx] = unchecked((int)0xFF000000) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
			}
		}
	}

	/// <summary>The trace of the model probe among a spec's probes (matched by <see cref="ProbeSpec.Origin"/>), or null when it was not recorded.</summary>
	private static ProbeTrace? TraceOf(ImmutableArray<ProbeSpec> specs, IReadOnlyList<ProbeTrace> traces, Probe probe)
	{
		for (int i = 0; i < specs.Length && i < traces.Count; i++)
		{
			if (ReferenceEquals(specs[i].Origin, probe))
			{
				return traces[i];
			}
		}
		return null;
	}
}
