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
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Views;

/// <summary>
/// The part of every optical part's editor that is the same for each kind: the description of the kind under a toggle,
/// then name, enabled, centre and angle (<see cref="WaveDudeNext.Model.OpticalPart"/>). Open or closed is one user
/// setting for every kind, <see cref="Settings.PartDescriptionExpanded"/>, bound here in code (a compiled binding
/// needs the type of its source, and the settings object is a static).
/// </summary>
public partial class PartCommonView : UserControl
{
	/// <summary>Creates the view and binds the description toggle to the user setting.</summary>
	public PartCommonView()
	{
		InitializeComponent();
		descriptionToggle.Bind(ToggleButton.IsCheckedProperty, CompiledBinding.Create((Settings s) => s.PartDescriptionExpanded, Settings.Default, mode: BindingMode.TwoWay));
		description.Bind(IsVisibleProperty, CompiledBinding.Create((Settings s) => s.PartDescriptionExpanded, Settings.Default));
	}
}
