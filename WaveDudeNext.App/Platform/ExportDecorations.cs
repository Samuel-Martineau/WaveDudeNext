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

using NLog;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// Composes the decoration of one exported picture from what the project's <see cref="Display"/> asks for, as the WPF
/// application's export did (its MainWindow.Export): the source markers first, on both pictures, under the probes'
/// markers and graph boxes (as in the preview, where the boxes lie over every handle), then the probes of that layer.
/// The result goes into <see cref="ExportLayer.Decorate"/> and runs on the export's render thread after the outline.
/// </summary>
public static class ExportDecorations
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>
	/// The decoration of <paramref name="layer"/>'s picture, or null when there is nothing to draw: the display wants
	/// none of <see cref="Display.ProbeGraphsInExport"/>, <see cref="Display.ProbeMarkersInExport"/> and
	/// <see cref="Display.SourceMarkersInExport"/>, or those it wants have no subject (no active probe on the layer, no
	/// enabled source).
	/// </summary>
	/// <param name="project">The project: its display settings select the decorations, its probes and sources are drawn.</param>
	/// <param name="spec">The run exported: the frame size, the steps and the frame interval.</param>
	/// <param name="traces">The run's probe recordings (<see cref="SimulationRun.Traces"/>), in the spec's probe order.</param>
	/// <param name="layer">The picture decorated.</param>
	public static Action<int, Memory<int>>? For(Project project, RunSpec spec, IReadOnlyList<ProbeTrace> traces, FrameLayer layer)
	{
		Display display = project.Display;
		ProbeGraphPainter? painter = display.ProbeGraphsInExport || display.ProbeMarkersInExport
			? new ProbeGraphPainter(project, spec, traces, display.ProbeGraphsInExport, display.ProbeMarkersInExport)
			: null;
		SourceMarkerPainter? sources = display.SourceMarkersInExport ? new SourceMarkerPainter(project, spec.Width, spec.Height) : null;
		if (sources is { IsEmpty: true })
		{
			Logger.Debug("Export source markers: none (no enabled source)");
			sources = null;
		}
		Action<int, Memory<int>>? probes = painter?.For(layer == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
		Logger.Debug("Export decorations for {Layer}: probe boxes {Boxes}, markers {Markers}, sources {Sources}",
			layer, display.ProbeGraphsInExport && probes != null ? "on" : "off", display.ProbeMarkersInExport && probes != null ? "on" : "off", sources != null ? "on" : "off");
		if (sources == null)
		{
			return probes;
		}
		return (frame, pixels) =>
		{
			sources.Paint(pixels);
			probes?.Invoke(frame, pixels);
		};
	}
}
