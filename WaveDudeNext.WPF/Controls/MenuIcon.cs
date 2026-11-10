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

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// A menu entry's icon, an <see cref="IconView"/> made from an attached property: <c>controls:MenuIcon.Drawing="Lens"</c>
/// names one of the app's own drawings (2026-10-23). A second property, Glyph, had given an entry a character of Windows' icon
/// font (2026-10-22, the user's request for the standard icons); it went on 2026-11-10 with the drawings shared between the
/// heads, every standard icon drawn (the user's choice, since Linux and macOS have no Segoe). UI thread.
/// </summary>
public static class MenuIcon
{
	/// <summary>Identifies the attached Drawing property: the name of one of the app's 16 px drawings (<see cref="IconView.Drawing"/>), or null for none.</summary>
	public static readonly DependencyProperty DrawingProperty = DependencyProperty.RegisterAttached(
		"Drawing", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnDrawingChanged));

	/// <summary>Gets the drawing of <paramref name="item"/>.</summary>
	public static string? GetDrawing(MenuItem item) => (string?)item.GetValue(DrawingProperty);

	/// <summary>Sets the drawing of <paramref name="item"/>, which becomes its icon.</summary>
	public static void SetDrawing(MenuItem item, string? name) => item.SetValue(DrawingProperty, name);

	private static void OnDrawingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is MenuItem item)
		{
			item.Icon = e.NewValue is string name ? new IconView { Drawing = name } : null;
		}
	}
}
