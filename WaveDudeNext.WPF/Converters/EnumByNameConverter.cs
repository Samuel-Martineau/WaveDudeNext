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

using System.Globalization;
using System.Windows.Data;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// An enum value as the member of the same name in the enum the other side of a binding wants, both ways: the user
/// settings keep the main window's state and the preview's direction as enums of their own library, which no UI
/// framework anchors (<see cref="UI.WindowMode"/>, <see cref="UI.LayoutDirection"/>, 2026-11-06), and their members
/// carry the names of WPF's <see cref="System.Windows.WindowState"/> and <see cref="System.Windows.Controls.Orientation"/>.
/// A value without a member of its name on the other side changes nothing.
/// </summary>
public sealed class EnumByNameConverter : IValueConverter
{
	/// <summary>The one instance, for the window's own WindowState, set before the window's resources exist.</summary>
	public static EnumByNameConverter Instance { get; } = new();

	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ByName(value, targetType);

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => ByName(value, targetType);

	private static object ByName(object? value, Type targetType) =>
		value is Enum member && targetType.IsEnum && Enum.TryParse(targetType, member.ToString(), out object? named) && named != null
			? named
			: Binding.DoNothing;
}
