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
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;
using WaveDudeNext.WPF.Rendering;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// What WPF draws over an export's frames (<see cref="IFrameDecorators"/>): the probe graph boxes and markers
/// (<see cref="ProbeGraphPainter"/>) and the source markers (<see cref="SourceMarkerPainter"/>), each when the project's
/// display settings ask for it; the sources under the probes' markers and boxes, as in the preview, where the boxes lie
/// over every handle. Both painters rasterise with WPF on the export's own thread, a single-threaded apartment.
/// </summary>
public sealed class WpfFrameDecorators : IFrameDecorators
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <inheritdoc/>
	public IFrameDecorator Create(Project project, SimulationRun run) => new Decorator(project, run);

	private sealed class Decorator : IFrameDecorator
	{
		private readonly ProbeGraphPainter? _probes;
		private readonly SourceMarkerPainter? _sources;

		public Decorator(Project project, SimulationRun run)
		{
			Display display = project.Display;
			_probes = display.ProbeGraphsInExport || display.ProbeMarkersInExport ? new ProbeGraphPainter(project, run, display.ProbeGraphsInExport, display.ProbeMarkersInExport) : null;
			_sources = display.SourceMarkersInExport ? new SourceMarkerPainter(project, run.Spec.Width, run.Spec.Height) : null;
			if (_sources is { IsEmpty: true })
			{
				Logger.Debug("Export source markers: none (no enabled source)");
				_sources = null;
			}
		}

		public Action<int, Memory<int>>? For(FrameLayer layer)
		{
			Action<int, Memory<int>>? probes = _probes?.For(layer == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
			SourceMarkerPainter? sources = _sources;
			return sources == null ? probes : (frame, pixels) =>
			{
				sources.Paint(pixels);
				probes?.Invoke(frame, pixels);
			};
		}
	}
}
