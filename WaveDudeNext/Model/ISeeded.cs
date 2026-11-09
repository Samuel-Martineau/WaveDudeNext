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

namespace WaveDudeNext.Model;

/// <summary>
/// A part with a random decision in it (a diffuser's roughness): it draws from <see cref="Project.Seed"/>, which the
/// project hands it as <see cref="ProjectSeed"/> when the part is added, loaded, pasted or the seed changes, so the same
/// project file gives the same surface, as it gives the same emitters. Not stored with the part. UI thread, like the model.
/// </summary>
public interface ISeeded
{
	/// <summary>The project's seed; every random decision of the part follows from it and the part's name.</summary>
	int ProjectSeed { get; set; }
}
