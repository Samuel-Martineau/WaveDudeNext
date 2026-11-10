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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia.Controls;
using Avalonia.Interactivity;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Views;

/// <summary>Editor for a <see cref="LensPart"/>: a lens or a curved or flat mirror.</summary>
public partial class LensPartView : UserControl
{
	/// <summary>Creates the view.</summary>
	public LensPartView()
	{
		InitializeComponent();
	}

	/// <summary>
	/// The Mirror box was ticked or unticked by the user (a click, Space or Enter), its binding has set the flag, and the
	/// name follows it, Lens becoming Mirror and back (<see cref="LensPart.MirrorName"/>; upstream's design). Here and not
	/// in the setter, so a loaded, pasted or undone name stays what it was; the flag and the name land in one undo step.
	/// </summary>
	private void OnMirrorToggled(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LensPart lens)
		{
			lens.Name = LensPart.MirrorName(lens.Name, lens.Mirror);
		}
	}
}
