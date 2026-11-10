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
using Avalonia;
using Avalonia.Data.Converters;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The description of a part's kind for the top of its form: the string table's <c>Kind.{type}</c> entry, the one the
/// Add part menu shows as the entry's tooltip; "" for a kind without one. As a multi-value converter the first input
/// is the part; a second input that changes with the language re-reads the text on a switch.
/// </summary>
public sealed class KindDescriptionConverter : IMultiValueConverter, IValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
		values.Count == 0 ? "" : Convert(values[0], targetType, parameter, culture);

	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is OpticalPart part && Application.Current?.TryGetResource("Kind." + part.GetType().Name, null, out object? text) == true && text is string description ? description : "";

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
