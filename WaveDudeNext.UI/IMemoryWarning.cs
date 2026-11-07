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

using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>The user's answer to the memory warning: the preview quality to run at, whether the RAM cache size goes to Auto, and whether the warning is to stay away.</summary>
/// <param name="Quality">The preview quality the run is to use; the current one when no lever was ticked.</param>
/// <param name="CacheAuto">Whether Simulation > RAM cache size goes to Auto.</param>
/// <param name="Mute">Whether "Do not warn me again" was ticked.</param>
public sealed record MemoryWarningChoice(PreviewQuality Quality, bool CacheAuto, bool Mute);

/// <summary>
/// The memory warning as the models of this library ask it (2026-11-07; each application implements it, WPF with its
/// <c>MemoryWarningWindow</c>): before a preview run whose frames would not all fit in the RAM cache, the user is told
/// the need and the budget and offered levers (2026-11-03, the user's design; the arithmetic is <see cref="RunSpec.StoreBytesAt"/>).
/// Asked on the UI thread and answered before it returns; the caller applies the choice.
/// </summary>
public interface IMemoryWarning
{
	/// <summary>Shows the warning for <paramref name="spec"/> and waits for the answer.</summary>
	/// <param name="spec">The run as it would be made.</param>
	/// <param name="budget">The RAM cache's budget in bytes as the settings have it.</param>
	/// <param name="autoBudget">The budget with the RAM cache size at Auto; the same as <paramref name="budget"/> when it is Auto already.</param>
	/// <param name="current">The preview quality the run would use.</param>
	/// <returns>The choice, or null for Cancel: no run.</returns>
	MemoryWarningChoice? Ask(RunSpec spec, long budget, long autoBudget, PreviewQuality current);
}
