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

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// The WPF application's image decoder for the models of <c>WaveDudeNext.UI</c> (<see cref="IImageDecoder"/>): WPF's
/// imaging, any format it has a decoder for. A file that is no image raises <see cref="NotSupportedException"/>, a
/// damaged one <see cref="FileFormatException"/>, which is a <see cref="FormatException"/>. UI thread.
/// </summary>
public sealed class WpfImageDecoder : IImageDecoder
{
	/// <inheritdoc/>
	public ScenePixels Decode(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, writable: false);
		BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
		int width = converted.PixelWidth;
		int height = converted.PixelHeight;
		int stride = width * 4;
		var bgra = new byte[stride * height];
		converted.CopyPixels(bgra, stride, 0);
		return new ScenePixels(width, height, stride, bgra);
	}
}
