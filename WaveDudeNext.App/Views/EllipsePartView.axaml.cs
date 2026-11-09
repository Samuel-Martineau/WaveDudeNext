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

using Avalonia.Controls;
using Avalonia.VisualTree;
using WaveDudeNext.App.Controls;

namespace WaveDudeNext.App.Views;

/// <summary>Editor for an <see cref="WaveDudeNext.Model.EllipsePart"/>: length and width, with the chain that keeps them equal.</summary>
public partial class EllipsePartView : UserControl
{
	/// <summary>Creates the view; the size grid's first column takes the form's label width once the view is in the tree.</summary>
	public EllipsePartView()
	{
		InitializeComponent();
		// the two sizes with the chain beside them are one hand-made row of the form, laid out to the panel's LabelWidth
		// (the property, 110, not the measured label column); a grid column cannot bind to an ancestor, so it is set here
		AttachedToVisualTree += (_, _) =>
		{
			if (sizeGrid.FindAncestorOfType<FormPanel>() is { } panel)
			{
				sizeGrid.ColumnDefinitions[0].Width = new GridLength(panel.LabelWidth);
			}
		};
	}
}
