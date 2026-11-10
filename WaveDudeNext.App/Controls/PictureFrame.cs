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
/// Lays out a label and a picture so the label takes as little of the picture's room as possible: above the picture
/// when the available space is taller than the picture's aspect ratio (spare room is above and below anyway), or turned
/// by 90° on its left when the space is wider (spare room is at the sides). The frame takes all the room it is given:
/// the label stays at the top (or the left) of the cell, the picture centred in the rest. The first child is the label,
/// the second the picture (a <see cref="CanvasImage"/>, whose canvas gives the aspect ratio, or an <see cref="Image"/>
/// whose source gives it, 16:9 when there is none; or a panel holding one under an overlay, possibly inside the edge
/// line's border). Avalonia has no layout transform on a control, so the turn is a render transform the frame sets
/// itself: the label is measured and arranged with its axes swapped and rotated into the column it occupies. UI thread.
/// </summary>
public sealed class PictureFrame : Panel
{
	/// <summary>Identifies the <see cref="Gap"/> property.</summary>
	public static readonly StyledProperty<double> GapProperty =
		AvaloniaProperty.Register<PictureFrame, double>(nameof(Gap), 4.0);

	/// <summary>Identifies the <see cref="IsLabelOnSide"/> property.</summary>
	public static readonly DirectProperty<PictureFrame, bool> IsLabelOnSideProperty =
		AvaloniaProperty.RegisterDirect<PictureFrame, bool>(nameof(IsLabelOnSide), frame => frame.IsLabelOnSide);

	private static readonly RotateTransform SideTransform = new(90);

	private const double DefaultAspect = 16.0 / 9.0;

	private bool _isLabelOnSide;

	static PictureFrame()
	{
		AffectsMeasure<PictureFrame>(GapProperty);
	}

	/// <summary>Space between the label and the picture, in pixels.</summary>
	public double Gap
	{
		get => GetValue(GapProperty);
		set => SetValue(GapProperty, value);
	}

	/// <summary>Whether the label currently sits turned on the left instead of above.</summary>
	public bool IsLabelOnSide
	{
		get => _isLabelOnSide;
		private set => SetAndRaise(IsLabelOnSideProperty, ref _isLabelOnSide, value);
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
	{
		if (Children.Count < 2)
		{
			Size desired = default;
			foreach (Control child in Children)
			{
				child.Measure(availableSize);
				desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width), Math.Max(desired.Height, child.DesiredSize.Height));
			}
			return desired;
		}
		Control label = Children[0];
		Control picture = Children[1];
		bool side = !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height) && availableSize.Height > 0
			&& availableSize.Width / availableSize.Height > AspectOf(picture);
		if (side != IsLabelOnSide)
		{
			IsLabelOnSide = side;
		}
		// turned on its side the label runs along the picture's height, so it is measured with its axes swapped
		label.Measure(side ? new Size(availableSize.Height, availableSize.Width) : availableSize);
		Size labelSize = side ? new Size(label.DesiredSize.Height, label.DesiredSize.Width) : label.DesiredSize;
		double gap = Gap;
		Size rest = side
			? new Size(Math.Max(0, availableSize.Width - labelSize.Width - gap), availableSize.Height)
			: new Size(availableSize.Width, Math.Max(0, availableSize.Height - labelSize.Height - gap));
		picture.Measure(rest);
		Size content = side
			? new Size(labelSize.Width + gap + picture.DesiredSize.Width, Math.Max(labelSize.Height, picture.DesiredSize.Height))
			: new Size(Math.Max(labelSize.Width, picture.DesiredSize.Width), labelSize.Height + gap + picture.DesiredSize.Height);
		return new Size(double.IsInfinity(availableSize.Width) ? content.Width : Math.Max(content.Width, availableSize.Width),
			double.IsInfinity(availableSize.Height) ? content.Height : Math.Max(content.Height, availableSize.Height));
	}

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize)
	{
		if (Children.Count < 2)
		{
			foreach (Control child in Children)
			{
				child.Arrange(new Rect(finalSize));
			}
			return finalSize;
		}
		Control label = Children[0];
		Control picture = Children[1];
		double gap = Gap;
		if (IsLabelOnSide)
		{
			// the label's own width runs down the side (its length), its height is the column's width; rotated by 90°
			// about its centre, an element arranged at (x, y, length, thickness) covers (x + length/2 - thickness/2,
			// y + thickness/2 - length/2, thickness, length), so it is placed where that rectangle is the left column
			double thickness = Math.Min(label.DesiredSize.Height, finalSize.Width);
			double length = finalSize.Height;
			label.RenderTransform = SideTransform;
			label.RenderTransformOrigin = RelativePoint.Center;
			label.Arrange(new Rect(thickness / 2 - length / 2, length / 2 - thickness / 2, length, thickness));
			picture.Arrange(new Rect(thickness + gap, 0, Math.Max(0, finalSize.Width - thickness - gap), finalSize.Height));
		}
		else
		{
			if (label.RenderTransform != null)
			{
				label.RenderTransform = null;
			}
			double h = Math.Min(label.DesiredSize.Height, finalSize.Height);
			label.Arrange(new Rect(0, 0, finalSize.Width, h));
			picture.Arrange(new Rect(0, h + gap, finalSize.Width, Math.Max(0, finalSize.Height - h - gap)));
		}
		return finalSize;
	}

	/// <summary>
	/// Width over height of the picture: a <see cref="CanvasImage"/>'s canvas, else the source's, or 16:9 without one. The
	/// picture is an <see cref="Image"/>, or a panel whose first <see cref="Image"/> child is it (an overlay on top), either
	/// possibly inside a <see cref="Decorator"/> (the edge line's border).
	/// </summary>
	private static double AspectOf(Control picture)
	{
		Control inner = picture is Decorator { Child: { } child } ? child : picture;
		Image? image = inner as Image ?? (inner as Panel)?.Children.OfType<Image>().FirstOrDefault();
		return image switch
		{
			CanvasImage canvas => canvas.Aspect,
			{ Source: { Size: { Width: > 0, Height: > 0 } size } } => size.Width / size.Height,
			_ => DefaultAspect,
		};
	}
}
