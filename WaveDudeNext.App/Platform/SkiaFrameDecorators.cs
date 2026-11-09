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

using WaveDudeNext.Model;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// What the Avalonia head draws over an export's frames (<see cref="IFrameDecorators"/>): the probe graph boxes and
/// markers and the source markers, each when the project's display settings ask for it, composed by
/// <see cref="ExportDecorations"/> as the WPF head's <c>WpfFrameDecorators</c> composes its own; the painters rasterise
/// with SkiaSharp on the export's own thread. The counterpart of the WPF head's painters, which rasterise with WPF.
/// </summary>
public sealed class SkiaFrameDecorators : IFrameDecorators
{
	/// <inheritdoc/>
	public IFrameDecorator Create(Project project, SimulationRun run) => new Decorator(project, run);

	private sealed class Decorator(Project project, SimulationRun run) : IFrameDecorator
	{
		public Action<int, Memory<int>>? For(FrameLayer layer) => ExportDecorations.For(project, run.Spec, run.Traces, layer);
	}
}
