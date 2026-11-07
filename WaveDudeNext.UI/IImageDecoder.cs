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

using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// Decodes an image file's bytes into the pixels a scene is made of (2026-11-06, the user's design: what only a UI
/// framework can do sits behind an interface each application implements; the first such interface). The WPF
/// application decodes with WPF's imaging, which reads what Windows has a codec for (PNG, JPEG, BMP, GIF, TIFF).
/// Called on the UI thread.
/// </summary>
public interface IImageDecoder
{
	/// <summary>The image's first frame as 32-bit BGRA pixels, row by row from the top.</summary>
	/// <param name="bytes">An image file's content.</param>
	/// <exception cref="NotSupportedException">The bytes are not an image the decoder reads.</exception>
	/// <exception cref="FormatException">The bytes are an image that is damaged or cut short.</exception>
	ScenePixels Decode(byte[] bytes);
}
