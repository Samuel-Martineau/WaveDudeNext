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

using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// An <see cref="Rgb"/> as its <c>#RRGGBB</c> text and back (<see cref="Rgb.TryParse"/> also takes <c>RRGGBB</c>,
/// <c>#RGB</c> and <c>[r,g,b]</c>): the hex box of a colour stop and of the colour picker. Text that does not parse yet
/// leaves the colour alone instead of raising a binding error. Upstream bound the colour straight through its
/// TypeConverter; the explicit converter keeps the compiled binding's conversion out of reflection.
/// </summary>
public sealed class RgbTextConverter : IValueConverter
{
	/// <inheritdoc/>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is Rgb color ? color.ToString() : null;

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		Rgb.TryParse(value as string, out Rgb color) ? color : BindingOperations.DoNothing;
}
