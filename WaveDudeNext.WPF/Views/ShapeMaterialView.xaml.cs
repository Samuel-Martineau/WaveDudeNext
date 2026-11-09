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
using System.Windows.Controls;

namespace WaveDudeNext.WPF.Views;

/// <summary>The material section shared by the shape parts (<see cref="WaveDudeNext.Model.ShapePart"/>): mirror, index, coating.</summary>
public partial class ShapeMaterialView : UserControl
{
	/// <summary>Identifies the <see cref="BackOnly"/> property.</summary>
	public static readonly DependencyProperty BackOnlyProperty = DependencyProperty.Register(
		nameof(BackOnly), typeof(bool), typeof(ShapeMaterialView), new PropertyMetadata(false));

	/// <summary>Identifies the <see cref="CoatingShown"/> property.</summary>
	public static readonly DependencyProperty CoatingShownProperty = DependencyProperty.Register(
		nameof(CoatingShown), typeof(bool), typeof(ShapeMaterialView), new PropertyMetadata(true));

	public ShapeMaterialView()
	{
		InitializeComponent();
	}

	/// <summary>Identifies the <see cref="SidesHidden"/> property.</summary>
	public static readonly DependencyProperty SidesHiddenProperty = DependencyProperty.Register(
		nameof(SidesHidden), typeof(bool), typeof(ShapeMaterialView), new PropertyMetadata(false));

	/// <summary>Whether the sides box alone is hidden: a meta lens's band goes on its pattern's tops and its plate's back (2026-11-08).</summary>
	public bool SidesHidden
	{
		get => (bool)GetValue(SidesHiddenProperty);
		set => SetValue(SidesHiddenProperty, value);
	}

	/// <summary>Whether the coating rows (thickness, profile, faces) are shown; a kind whose shape takes no band hides them (the simple grating's dots, 2026-11-07).</summary>
	public bool CoatingShown
	{
		get => (bool)GetValue(CoatingShownProperty);
		set => SetValue(CoatingShownProperty, value);
	}

	/// <summary>Whether the coating goes on the back alone, so the front and side boxes are hidden (an interference filter's plate, whose front carries the layers); set by the kind's form.</summary>
	public bool BackOnly
	{
		get => (bool)GetValue(BackOnlyProperty);
		set => SetValue(BackOnlyProperty, value);
	}
}
