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

namespace WaveDudeNext.UI;

/// <summary>
/// The main window's state as the user settings keep it (<see cref="Settings.WindowState"/>): the members carry the
/// names WPF's and Avalonia's own window state enums use, which is what the settings file holds, so each head converts
/// by name.
/// </summary>
public enum WindowMode
{
	/// <summary>Restored: the window has its own size and place.</summary>
	Normal,
	/// <summary>Minimized; never saved, a start shows the window.</summary>
	Minimized,
	/// <summary>Maximized.</summary>
	Maximized,
}
