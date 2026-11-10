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

using Avalonia;
using Avalonia.Controls;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// A menu entry's icon, an <see cref="IconView"/> made from an attached property, as the WPF head's <c>MenuIcon</c>:
/// <c>controls:MenuIcon.Drawing="Save"</c> names one of the drawings of the shared Themes/Icons.axaml (2026-11-10, with
/// the drawings for every standard command, which the WPF head had taken from Windows' icon font until then). Not a
/// static class, because an attached property's owner is a generic type argument. UI thread.
/// </summary>
public sealed class MenuIcon
{
	/// <summary>Identifies the attached Drawing property: the name of one of the 16 px drawings (<see cref="IconView.Drawing"/>), or null for none.</summary>
	public static readonly AttachedProperty<string?> DrawingProperty =
		AvaloniaProperty.RegisterAttached<MenuIcon, MenuItem, string?>("Drawing");

	static MenuIcon()
	{
		DrawingProperty.Changed.AddClassHandler<MenuItem>(OnDrawingChanged);
	}

	private MenuIcon()
	{
	}

	/// <summary>Gets the drawing of <paramref name="item"/>.</summary>
	public static string? GetDrawing(MenuItem item) => item.GetValue(DrawingProperty);

	/// <summary>Sets the drawing of <paramref name="item"/>, which becomes its icon.</summary>
	public static void SetDrawing(MenuItem item, string? name) => item.SetValue(DrawingProperty, name);

	private static void OnDrawingChanged(MenuItem item, AvaloniaPropertyChangedEventArgs e) =>
		item.Icon = e.NewValue is string name ? new IconView { Drawing = name } : null;
}
