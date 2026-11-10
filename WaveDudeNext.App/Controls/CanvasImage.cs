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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// A preview picture that takes its size from the canvas, never from its bitmap (upstream's design): the canvas, in
/// DIPs, fitted uniformly into the room it is given (the canvas size itself without a bound), and the bitmap drawn into
/// that rectangle, whatever its own pixel size. So a new bitmap never changes the layout: a 16-bit picture rendered at
/// the rectangle's size in screen pixels lands on them 1:1, a picture of palette indices at the store's size is scaled,
/// and no loop runs from the layout to the bitmap's size and back. The stretch is Fill, since the rectangle already has
/// the canvas's aspect. UI thread, like every control.
/// </summary>
public sealed class CanvasImage : Image
{
	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly StyledProperty<int> CanvasWidthProperty =
		AvaloniaProperty.Register<CanvasImage, int>(nameof(CanvasWidth), 1);

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly StyledProperty<int> CanvasHeightProperty =
		AvaloniaProperty.Register<CanvasImage, int>(nameof(CanvasHeight), 1);

	static CanvasImage()
	{
		AffectsMeasure<CanvasImage>(CanvasWidthProperty, CanvasHeightProperty);
	}

	/// <summary>Creates the picture, filling the rectangle the canvas fits.</summary>
	public CanvasImage()
	{
		Stretch = Stretch.Fill;
	}

	/// <summary>Width of the canvas the picture shows, in canvas pixels (one DIP each at its natural size); at least 1.</summary>
	public int CanvasWidth
	{
		get => GetValue(CanvasWidthProperty);
		set => SetValue(CanvasWidthProperty, value);
	}

	/// <summary>Height of the canvas the picture shows, in canvas pixels; at least 1.</summary>
	public int CanvasHeight
	{
		get => GetValue(CanvasHeightProperty);
		set => SetValue(CanvasHeightProperty, value);
	}

	/// <summary>Width over height of the canvas, the picture's aspect ratio.</summary>
	public double Aspect => Math.Max(1, CanvasWidth) / (double)Math.Max(1, CanvasHeight);

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize) => Fit(availableSize);

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize) => Fit(finalSize);

	/// <summary>The canvas fitted uniformly into <paramref name="room"/>; its own size along an unbounded side, or with no bound at all.</summary>
	private Size Fit(Size room)
	{
		double width = Math.Max(1, CanvasWidth);
		double height = Math.Max(1, CanvasHeight);
		bool freeWidth = double.IsInfinity(room.Width);
		bool freeHeight = double.IsInfinity(room.Height);
		if (freeWidth && freeHeight)
		{
			return new Size(width, height);
		}
		double scale = freeWidth ? room.Height / height : freeHeight ? room.Width / width : Math.Min(room.Width / width, room.Height / height);
		return new Size(width * scale, height * scale);
	}
}
