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

namespace WaveDudeNext.App.Converters;

/// <summary>
/// A folder's item count as words with the right plural (upstream, after "1 entries"): the string table's
/// <c>ItemCountZero</c> ("No entries"), <c>ItemCountOne</c> ("1 entry") or <c>ItemCountMany</c> ("{0} entries", the
/// count formatted in the current culture). As a multi-value converter the first input is the count; a second input
/// that changes with the language only serves to re-read the words on a switch.
/// </summary>
public sealed class ItemCountConverter : IMultiValueConverter, IValueConverter
{
	/// <inheritdoc/>
	public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
		values.Count == 0 ? null : Convert(values[0], targetType, parameter, culture);

	/// <inheritdoc/>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not int count)
		{
			return null;
		}
		string key = count switch
		{
			0 => "ItemCountZero",
			1 => "ItemCountOne",
			_ => "ItemCountMany",
		};
		string format = Application.Current?.TryGetResource(key, null, out object? text) == true && text is string resource ? resource : "{0}";
		return string.Format(culture, format, count);
	}

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
