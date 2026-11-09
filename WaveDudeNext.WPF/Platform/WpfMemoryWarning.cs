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

using System.Windows;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;
using WaveDudeNext.WPF.Dialogs;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// The memory warning for the models of <c>WaveDudeNext.UI</c> (<see cref="IMemoryWarning"/>): <see cref="MemoryWarningWindow"/>,
/// modal over <paramref name="owner"/>. The window blocks until it is answered, so the task is finished when it is
/// returned and the model goes on within the call.
/// </summary>
/// <param name="owner">The window the dialog belongs to.</param>
public sealed class WpfMemoryWarning(Window owner) : IMemoryWarning
{
	/// <inheritdoc/>
	public Task<MemoryWarningChoice?> AskAsync(RunSpec spec, long budget, long autoBudget, PreviewQuality current) =>
		Task.FromResult(MemoryWarningWindow.Show(owner, spec, budget, autoBudget, current));
}
