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
using Avalonia;
using Avalonia.Data.Converters;
using WaveDudeNext.App.Platform;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The embedded scene image's bytes as "1920 × 1080, 312 KB" for the scene view; "" without an image, the string
/// table's <c>ImageUnreadable</c> ("(unreadable)") when no decoder takes the bytes. Decodes through the platform's
/// SkiaSharp decoder, the one the scene itself goes through. As a multi-value converter the first value is the bytes
/// and the rest (the language) only make it convert again, so "(unreadable)" follows a language switch.
/// </summary>
public sealed class ImageInfoConverter : IValueConverter, IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
		values.Count == 0 ? "" : Convert(values[0], targetType, parameter, culture);

	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not byte[] bytes)
		{
			return "";
		}
		try
		{
			ScenePixels pixels = SkiaImageDecoder.Load(bytes);
			return string.Format(culture, "{0} × {1}, {2:N0} KB", pixels.Width, pixels.Height, Math.Max(1, bytes.Length / 1024));
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or ArgumentException)
		{
			return Application.Current?.TryGetResource("ImageUnreadable", null, out object? text) == true && text is string unreadable ? unreadable : "(unreadable)";
		}
	}

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
