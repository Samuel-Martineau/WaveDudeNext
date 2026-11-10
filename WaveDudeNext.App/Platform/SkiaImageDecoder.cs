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

using System.Runtime.InteropServices;
using SkiaSharp;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// The Avalonia head's image decoder for the models of <c>WaveDudeNext.UI</c> (<see cref="IImageDecoder"/>): a scene
/// image (PNG, JPEG, BMP, GIF, WebP) as the library's <see cref="ScenePixels"/>, 32-bit BGRA, non-premultiplied, so the
/// red, green and blue levels the scene rules read come back exactly as stored. The counterpart of the WPF head's
/// <c>WpfImageDecoder</c>, which uses WPF's decoders; this one uses SkiaSharp, which Avalonia brings along. Any thread.
/// </summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
	/// <inheritdoc/>
	public ScenePixels Decode(byte[] bytes) => Load(bytes);

	/// <summary>Decodes the image file at <paramref name="path"/>.</summary>
	/// <exception cref="FileNotFoundException">The file does not exist.</exception>
	/// <exception cref="NotSupportedException">The file is not an image SkiaSharp can decode.</exception>
	public static ScenePixels Load(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Load(stream);
	}

	/// <summary>Decodes an image file's bytes (an embedded scene image, <see cref="Model.Scene.ImageData"/>).</summary>
	/// <exception cref="NotSupportedException">The bytes are not an image SkiaSharp can decode.</exception>
	public static ScenePixels Load(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, writable: false);
		return Load(stream);
	}

	/// <summary>Decodes an image stream into BGRA pixels.</summary>
	/// <exception cref="NotSupportedException">The stream is not an image SkiaSharp can decode.</exception>
	public static ScenePixels Load(Stream stream)
	{
		using SKCodec codec = SKCodec.Create(stream) ?? throw new NotSupportedException("The data is not an image that can be decoded.");
		var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		using SKBitmap bitmap = SKBitmap.Decode(codec, info) ?? throw new NotSupportedException("The image could not be decoded.");
		int width = bitmap.Width;
		int height = bitmap.Height;
		int stride = width * 4;
		var bgra = new byte[stride * height];
		IntPtr pixels = bitmap.GetPixels();
		for (int y = 0; y < height; y++)
		{
			Marshal.Copy(IntPtr.Add(pixels, y * bitmap.RowBytes), bgra, y * stride, stride);
		}
		return new ScenePixels(width, height, stride, bgra);
	}
}
