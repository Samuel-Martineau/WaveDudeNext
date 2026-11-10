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
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Rendering;

/// <summary>
/// The preview's pictures made from a scene image: the image itself, shown where there is no frame yet, and the
/// glowing-edges overlay (<see cref="GlowingEdges"/>) as a translucent layer. The compositor blends source-over, not
/// additively like the export, so the overlay's alpha carries the edge strength times the gain and its colour is the
/// edge colour at full brightness: a black (no edge) pixel leaves the frame alone, a faint edge tints it a little, a
/// hard edge paints its colour over it. The bitmaps are written once and then only read. UI thread.
/// </summary>
public static class SceneBitmaps
{
	/// <summary>The scene image as a bitmap (alpha ignored).</summary>
	public static WriteableBitmap Image(ScenePixels pixels)
	{
		var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
		using ILockedFramebuffer buffer = bitmap.Lock();
		int rowBytes = pixels.Width * 4;
		for (int y = 0; y < pixels.Height; y++)
		{
			Marshal.Copy(pixels.Bgra, y * pixels.StrideBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
		}
		return bitmap;
	}

	/// <summary>
	/// The overlay layer for one picture: alpha = min(255, strongest channel × <paramref name="gain"/>),
	/// colour = the edge colour scaled so its strongest channel is 255. Null when the gain is 0.
	/// </summary>
	/// <param name="edgesBgra">The picture from <see cref="GlowingEdges.Compute"/>, tightly packed.</param>
	/// <param name="width">Width in pixels.</param>
	/// <param name="height">Height in pixels.</param>
	/// <param name="gain">The layer's overlay gain (<c>DisplayLayer.OverlayGain</c>).</param>
	public static WriteableBitmap? Overlay(byte[] edgesBgra, int width, int height, double gain)
	{
		if (gain <= 0.0)
		{
			return null;
		}
		var layer = new byte[edgesBgra.Length];
		for (int i = 0; i < layer.Length; i += 4)
		{
			int b = edgesBgra[i];
			int g = edgesBgra[i + 1];
			int r = edgesBgra[i + 2];
			int max = Math.Max(b, Math.Max(g, r));
			if (max == 0)
			{
				continue; // transparent black
			}
			layer[i] = (byte)(b * 255 / max);
			layer[i + 1] = (byte)(g * 255 / max);
			layer[i + 2] = (byte)(r * 255 / max);
			layer[i + 3] = (byte)Math.Min(255.0, max * gain);
		}
		// straight (unpremultiplied) alpha, as the bytes above are; the compositor premultiplies when it draws
		var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
		using ILockedFramebuffer buffer = bitmap.Lock();
		int rowBytes = width * 4;
		for (int y = 0; y < height; y++)
		{
			Marshal.Copy(layer, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
		}
		return bitmap;
	}
}
