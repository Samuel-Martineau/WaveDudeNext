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

using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// Binds one radio button, or an enabled or visible state, to one value of an enum property: true when the property
/// equals the converter parameter, and checking it writes the parameter back. Unchecking (which a radio group does
/// when another button is chosen) writes nothing.
/// </summary>
public sealed class EnumBooleanConverter : IValueConverter
{
	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		Equals(value, parameter);

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is true && parameter != null ? parameter : BindingOperations.DoNothing;
}
