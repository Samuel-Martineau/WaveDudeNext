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
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NLog;

namespace WaveDudeNext.App.Help;

/// <summary>
/// One of the manual's figures, an SVG of the kind upstream's <c>_tools/ui_check/Screenshots.ps1</c> writes, as Avalonia
/// controls: a canvas the size of the SVG holding its embedded PNG or JPEG pictures, its circles, its texts and its
/// groups, a group's title its tooltip, a group of a circle and a text centred on it a round badge. The subset is
/// upstream's build-time SvgReader's (svg, g, title, image with a data URI, circle, text; fill, stroke, font-*,
/// text-anchor, dy), read here at run time with System.Xml, no SVG package. Where upstream failed the build on anything
/// else, this logs a warning and leaves the element out, so a figure never stops the manual from opening. The view box
/// scaled to the SVG's size is drawn at that scale (the harness's figures at a zoom: the pixels in the box, the size in
/// CSS pixels), and the figure is placed in a <see cref="Viewbox"/> that only shrinks it. UI thread (it makes controls).
/// </summary>
public sealed partial class SvgFigure
{
	private const string SvgNamespace = "http://www.w3.org/2000/svg";
	private const string XLinkNamespace = "http://www.w3.org/1999/xlink";
	/// <summary>Half the width of the box a figure's text is centred in: a canvas places a box, not a text anchor.</summary>
	private const double TextReach = 1000;
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly string[] GenericFamilies = ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui"];

	private readonly string _name;
	private readonly List<Bitmap> _bitmaps = [];
	private double _left;
	private double _top;
	/// <summary>Device-independent pixels per unit of the view box: 1, or a half for a figure at zoom 2.</summary>
	private double _unit = 1;
	private int _badges;

	[GeneratedRegex(@"^data:image/(png|jpeg);base64,(.*)$", RegexOptions.Singleline)]
	private static partial Regex DataUri();

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	private SvgFigure(string name, Canvas root)
	{
		_name = name;
		Root = root;
	}

	/// <summary>The figure: a canvas of the SVG's size (CSS pixels) with the pictures, badges, circles and texts placed in it.</summary>
	public Canvas Root { get; }

	/// <summary>The embedded pictures, decoded, in document order; the first is the screenshot.</summary>
	public IReadOnlyList<Bitmap> Bitmaps => _bitmaps;

	/// <summary>How many round badges (a circle with a number) the figure has.</summary>
	public int Badges => _badges;

	/// <summary>
	/// The figure embedded under <paramref name="logicalName"/> ("Assets/main-window.svg", as the project file embeds
	/// <c>upstream/Assets/*.svg</c>); null, with a warning logged, when the resource is missing or is no SVG this reads.
	/// </summary>
	public static SvgFigure? FromResource(string logicalName)
	{
		using Stream? stream = typeof(SvgFigure).Assembly.GetManifestResourceStream(logicalName);
		if (stream == null)
		{
			Logger.Warn("Figure {Name}: not embedded in this build", logicalName);
			return null;
		}
		using var reader = new StreamReader(stream);
		return Parse(reader.ReadToEnd(), logicalName);
	}

	/// <summary>The figure of an SVG text, <paramref name="name"/> naming it in log lines; null, with a warning logged, when it is no SVG this reads.</summary>
	public static SvgFigure? Parse(string svg, string name)
	{
		XDocument document;
		try
		{
			document = XDocument.Parse(svg, LoadOptions.SetLineInfo);
		}
		catch (XmlException ex)
		{
			Logger.Warn(ex, "Figure {Name}: not well-formed XML", name);
			return null;
		}
		XElement? root = document.Root;
		if (root == null || root.Name != XName.Get("svg", SvgNamespace))
		{
			Logger.Warn("Figure {Name}: the root element is not an SVG <svg>", name);
			return null;
		}
		var canvas = new Canvas();
		var figure = new SvgFigure(name, canvas);
		return figure.Read(root) ? figure : null;
	}

	/// <summary>The embedded resource names of every figure in this build, for the tests.</summary>
	public static IEnumerable<string> EmbeddedNames() =>
		typeof(SvgFigure).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Assets/", StringComparison.Ordinal) && n.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));

	private bool Read(XElement root)
	{
		Check(root, "width", "height", "viewBox", "version");
		double width = Number(root, "width", double.NaN), height = Number(root, "height", double.NaN);
		double boxWidth = width, boxHeight = height;
		string? box = (string?)root.Attribute("viewBox");
		if (box != null)
		{
			string[] parts = box.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
			double[] values = new double[4];
			bool valid = parts.Length == 4;
			for (int k = 0; valid && k < 4; k++)
			{
				valid = TryNumber(parts[k], out values[k]);
			}
			if (!valid)
			{
				Warn(root, $"the viewBox \"{box}\" is not four numbers");
				return false;
			}
			_left = values[0];
			_top = values[1];
			boxWidth = values[2];
			boxHeight = values[3];
		}
		if (double.IsNaN(width))
		{
			width = boxWidth;
		}
		if (double.IsNaN(height))
		{
			height = boxHeight;
		}
		if (double.IsNaN(width) || double.IsNaN(height))
		{
			Warn(root, "it has neither a size nor a viewBox");
			return false;
		}
		if (boxWidth <= 0 || boxHeight <= 0)
		{
			Warn(root, "its viewBox is empty");
			return false;
		}
		_unit = width / boxWidth;
		if (Math.Abs(height / boxHeight - _unit) > 1e-6)
		{
			Warn(root, "its viewBox is scaled to another aspect, which the reader does not draw");
			return false;
		}
		Root.Width = width;
		Root.Height = height;
		Children(root, new SvgStyle(), Root);
		return true;
	}

	private void Children(XElement parent, SvgStyle style, Panel into)
	{
		foreach (XElement e in parent.Elements())
		{
			if (e.Name.NamespaceName != SvgNamespace)
			{
				Warn(e, $"<{e.Name.LocalName}> is not an SVG element");
				continue;
			}
			switch (e.Name.LocalName)
			{
				case "g":
					Group(e, style, into);
					break;
				case "image":
					Image(e, into);
					break;
				case "circle":
					Circle(e, style, into);
					break;
				case "text":
					Text(e, style, into);
					break;
				case "title":
					// a group's title is its tooltip (Group reads it); the document's own is not drawn
					Check(e);
					break;
				default:
					Warn(e, $"<{e.Name.LocalName}> is not one of the elements the reader draws (g, title, image, circle, text)");
					break;
			}
		}
	}

	private void Group(XElement e, SvgStyle style, Panel into)
	{
		Check(e, "fill", "font-family", "font-size", "font-weight", "text-anchor");
		SvgStyle own = style.With(e, this);
		XElement? title = e.Element(XName.Get("title", SvgNamespace));
		string? tip = title == null ? null : Collapse(title.Value);
		if (Badge(e, own, tip, into))
		{
			return;
		}
		var group = new Canvas();
		if (tip != null)
		{
			ToolTip.SetTip(group, tip);
		}
		Children(e, own, group);
		into.Children.Add(group);
	}

	// a badge, a group of a circle and a text centred on it (Screenshots.ps1's numbers), as one round Border holding the
	// text: two controls where the general form takes four, and the text centred by the Border rather than by a box;
	// anything else (or anything odd, which the general form then reports) is left to the general form
	private bool Badge(XElement g, SvgStyle style, string? tip, Panel into)
	{
		List<XElement> parts = g.Elements().Where(c => c.Name != XName.Get("title", SvgNamespace)).ToList();
		if (parts.Count != 2 || parts[0].Name != XName.Get("circle", SvgNamespace) || parts[1].Name != XName.Get("text", SvgNamespace)
			|| parts[1].Elements().Any() || Unknown(parts[0], "cx", "cy", "r", "fill", "stroke", "stroke-width") != null
			|| Unknown(parts[1], "x", "y", "dy", "fill", "font-family", "font-size", "font-weight", "text-anchor") != null)
		{
			return false;
		}
		XElement circle = parts[0], text = parts[1];
		double size = Plain(text, "font-size", style.FontSize);
		string dy = ((string?)text.Attribute("dy") ?? "0").Trim();
		double shift = dy.EndsWith("em", StringComparison.Ordinal) ? Plain(dy[..^2], double.NaN) * size : Plain(dy, double.NaN);
		double cx = Plain(circle, "cx", 0), cy = Plain(circle, "cy", 0), r = Plain(circle, "r", 0);
		double x = Plain(text, "x", 0), y = Plain(text, "y", 0);
		if (((string?)text.Attribute("text-anchor") ?? style.Anchor) != "middle" || double.IsNaN(cx + cy + r + x + y + size + shift)
			|| Math.Abs(x - cx) > 0.01 || Math.Abs(y + shift - 0.36 * size - cy) > 0.01)
		{
			return false;
		}
		SvgStyle own = style.With(text, this);
		IBrush? fill = Paint(circle, (string?)circle.Attribute("fill") ?? style.Fill ?? "black");
		IBrush? stroke = Paint(circle, (string?)circle.Attribute("stroke") ?? "none");
		double strokeWidth = stroke == null ? 0 : Number(circle, "stroke-width", 1);
		IBrush? ink = Paint(text, own.Fill ?? "black");
		// the Border's edge, like SVG's stroke, is centred on the radius: the box grows by the stroke
		double boxSize = (2 * r + strokeWidth) * _unit;
		var label = new TextBlock
		{
			Text = Collapse(text.Value),
			FontSize = own.FontSize * _unit,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		if (own.FontFamily != null)
		{
			label.FontFamily = own.FontFamily;
		}
		if (own.FontWeight is { } weight)
		{
			label.FontWeight = weight;
		}
		if (ink != null)
		{
			label.Foreground = ink;
		}
		var badge = new Border
		{
			Width = boxSize,
			Height = boxSize,
			CornerRadius = new CornerRadius(boxSize / 2),
			Background = fill,
			Child = label,
		};
		if (stroke != null)
		{
			badge.BorderBrush = stroke;
			badge.BorderThickness = new Thickness(strokeWidth * _unit);
		}
		if (tip != null)
		{
			ToolTip.SetTip(badge, tip);
		}
		Canvas.SetLeft(badge, (cx - _left) * _unit - boxSize / 2);
		Canvas.SetTop(badge, (cy - _top) * _unit - boxSize / 2);
		into.Children.Add(badge);
		_badges++;
		return true;
	}

	// a number of pixels for the badge's test, NaN when it is not one (the general form then reports it)
	private static double Plain(XElement e, string name, double fallback)
	{
		string? value = (string?)e.Attribute(name);
		if (value == null)
		{
			return fallback;
		}
		string v = value.Trim();
		return Plain(v.EndsWith("px", StringComparison.Ordinal) ? v[..^2] : v, double.NaN);
	}

	private static double Plain(string value, double fallback) => TryNumber(value.Trim(), out double number) ? number : fallback;

	private void Image(XElement e, Panel into)
	{
		Check(e, "x", "y", "width", "height", "href", "xlink:href");
		string? href = (string?)e.Attribute(XName.Get("href", XLinkNamespace)) ?? (string?)e.Attribute("href");
		Match match = href == null ? Match.Empty : DataUri().Match(href.Trim());
		if (!match.Success)
		{
			Warn(e, "the <image>'s picture is not embedded as data:image/png;base64 or data:image/jpeg;base64");
			return;
		}
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(Whitespace().Replace(match.Groups[2].Value, ""));
		}
		catch (FormatException)
		{
			Warn(e, "the <image>'s base64 data is broken");
			return;
		}
		double x = Number(e, "x", 0), y = Number(e, "y", 0), width = Number(e, "width", double.NaN), height = Number(e, "height", double.NaN);
		if (double.IsNaN(width) || double.IsNaN(height))
		{
			Warn(e, "the <image> needs a width and a height");
			return;
		}
		Bitmap bitmap;
		try
		{
			bitmap = new Bitmap(new MemoryStream(bytes));
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
		{
			Warn(e, "the <image>'s picture cannot be decoded: " + ex.Message);
			return;
		}
		_bitmaps.Add(bitmap);
		var image = new Avalonia.Controls.Image
		{
			Source = bitmap,
			Width = width * _unit,
			Height = height * _unit,
			Stretch = Stretch.Fill,
		};
		RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
		Canvas.SetLeft(image, (x - _left) * _unit);
		Canvas.SetTop(image, (y - _top) * _unit);
		into.Children.Add(image);
	}

	// SVG strokes a circle on its radius; the Ellipse keeps the stroke inside its box, so the box grows by the stroke
	private void Circle(XElement e, SvgStyle style, Panel into)
	{
		Check(e, "cx", "cy", "r", "fill", "stroke", "stroke-width");
		double cx = Number(e, "cx", 0), cy = Number(e, "cy", 0), r = Number(e, "r", 0);
		IBrush? fill = Paint(e, (string?)e.Attribute("fill") ?? style.Fill ?? "black");
		IBrush? stroke = Paint(e, (string?)e.Attribute("stroke") ?? "none");
		double strokeWidth = stroke == null ? 0 : Number(e, "stroke-width", 1);
		double size = (2 * r + strokeWidth) * _unit;
		var ellipse = new Ellipse { Width = size, Height = size, Fill = fill };
		if (stroke != null)
		{
			ellipse.Stroke = stroke;
			ellipse.StrokeThickness = strokeWidth * _unit;
		}
		Canvas.SetLeft(ellipse, (cx - _left) * _unit - size / 2);
		Canvas.SetTop(ellipse, (cy - _top) * _unit - size / 2);
		into.Children.Add(ellipse);
	}

	// a canvas places a text by its box, not by a baseline: the box is centred where the digits and capitals of SVG's
	// text sit, 0.36 em above its baseline, which centres a badge's number on its circle (dy=".36em")
	private void Text(XElement e, SvgStyle style, Panel into)
	{
		Check(e, "x", "y", "dy", "fill", "font-family", "font-size", "font-weight", "text-anchor");
		if (e.Elements().Any())
		{
			Warn(e, "the <text> has child elements, which the reader does not draw");
			return;
		}
		SvgStyle own = style.With(e, this);
		double size = own.FontSize * _unit;
		double x = Number(e, "x", 0), y = Number(e, "y", 0), dy = Length(e, "dy", own.FontSize);
		double middle = (y + dy - 0.36 * own.FontSize - _top) * _unit;
		IBrush? fill = Paint(e, own.Fill ?? "black");
		double left = (x - _left) * _unit;
		HorizontalAlignment alignment;
		switch (own.Anchor)
		{
			case "middle":
				alignment = HorizontalAlignment.Center;
				left -= TextReach;
				break;
			case "end":
				alignment = HorizontalAlignment.Right;
				left -= 2 * TextReach;
				break;
			default:
				alignment = HorizontalAlignment.Left;
				break;
		}
		var label = new TextBlock
		{
			Text = Collapse(e.Value),
			FontSize = size,
			HorizontalAlignment = alignment,
			VerticalAlignment = VerticalAlignment.Center,
		};
		if (own.FontFamily != null)
		{
			label.FontFamily = own.FontFamily;
		}
		if (own.FontWeight is { } weight)
		{
			label.FontWeight = weight;
		}
		if (fill != null)
		{
			label.Foreground = fill;
		}
		var box = new Grid { Height = 4 * size };
		if (alignment != HorizontalAlignment.Left)
		{
			box.Width = 2 * TextReach;
		}
		box.Children.Add(label);
		Canvas.SetLeft(box, left);
		Canvas.SetTop(box, middle - 2 * size);
		into.Children.Add(box);
	}

	/// <summary>A colour as a brush (#RGB, #RRGGBB or a name); null for "none" or, with a warning, for a colour Avalonia cannot read.</summary>
	private IBrush? Paint(XElement e, string value)
	{
		string v = value.Trim();
		if (v == "none")
		{
			return null;
		}
		if (Color.TryParse(v, out Color color))
		{
			return new SolidColorBrush(color);
		}
		Warn(e, $"the colour {v} is neither #RGB, #RRGGBB nor a name");
		return null;
	}

	private double Number(XElement e, string name, double fallback)
	{
		string? value = (string?)e.Attribute(name);
		if (value == null)
		{
			return fallback;
		}
		string v = value.Trim();
		if (TryNumber(v.EndsWith("px", StringComparison.Ordinal) ? v[..^2] : v, out double number))
		{
			return number;
		}
		Warn(e, $"the {name} \"{value}\" is not a number of pixels");
		return fallback;
	}

	// a length in pixels or ems
	private double Length(XElement e, string name, double em)
	{
		string? value = ((string?)e.Attribute(name))?.Trim();
		if (value != null && value.EndsWith("em", StringComparison.Ordinal) && TryNumber(value[..^2], out double number))
		{
			return number * em;
		}
		return Number(e, name, 0);
	}

	// every attribute should be one the reader draws (namespace declarations aside); another is reported and ignored
	private void Check(XElement e, params string[] allowed)
	{
		foreach (string name in Names(e).Where(n => Array.IndexOf(allowed, n) < 0))
		{
			Warn(e, $"the <{e.Name.LocalName}> attribute {name} is not one the reader draws");
		}
	}

	// the first attribute not among the allowed ones, or null
	private static string? Unknown(XElement e, params string[] allowed) => Names(e).FirstOrDefault(n => Array.IndexOf(allowed, n) < 0);

	// the attributes' names, xlink's with its prefix, namespace declarations aside
	private static IEnumerable<string> Names(XElement e) => e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a =>
		a.Name.NamespaceName == XLinkNamespace ? "xlink:" + a.Name.LocalName
		: a.Name.NamespaceName.Length == 0 ? a.Name.LocalName
		: "{" + a.Name.NamespaceName + "}" + a.Name.LocalName);

	private void Warn(XElement e, string message)
	{
		var info = (IXmlLineInfo)e;
		string where = info.HasLineInfo() ? $"{_name}({info.LineNumber.ToString(CultureInfo.InvariantCulture)})" : _name;
		Logger.Warn("Figure {Where}: {Message}", where, message);
	}

	private static bool TryNumber(string s, out double value) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

	private static string Collapse(string s) => Whitespace().Replace(s, " ").Trim();

	/// <summary>The presentation attributes a group passes down to its texts and circles.</summary>
	private sealed class SvgStyle
	{
		/// <summary>The font list, generic families dropped; null for the document's font.</summary>
		public FontFamily? FontFamily;
		public double FontSize = 16;
		/// <summary>A font weight; null for the inherited weight.</summary>
		public FontWeight? FontWeight;
		public string Anchor = "start";
		public string? Fill;

		public SvgStyle With(XElement e, SvgFigure reader)
		{
			var style = (SvgStyle)MemberwiseClone();
			string? family = (string?)e.Attribute("font-family");
			if (family != null)
			{
				string list = string.Join(", ", family.Split(',').Select(f => f.Trim().Trim('"', '\'')).Where(f => f.Length > 0 && Array.IndexOf(GenericFamilies, f) < 0));
				style.FontFamily = list.Length > 0 ? new FontFamily(list) : null;
			}
			if (e.Attribute("font-size") != null)
			{
				style.FontSize = reader.Number(e, "font-size", style.FontSize);
			}
			string? weight = (string?)e.Attribute("font-weight");
			if (weight != null)
			{
				switch (weight.Trim())
				{
					case "normal": case "400": style.FontWeight = Avalonia.Media.FontWeight.Normal; break;
					case "bold": case "bolder": case "700": style.FontWeight = Avalonia.Media.FontWeight.Bold; break;
					case "lighter": case "300": style.FontWeight = Avalonia.Media.FontWeight.Light; break;
					case "100": style.FontWeight = Avalonia.Media.FontWeight.Thin; break;
					case "200": style.FontWeight = Avalonia.Media.FontWeight.ExtraLight; break;
					case "500": style.FontWeight = Avalonia.Media.FontWeight.Medium; break;
					case "600": style.FontWeight = Avalonia.Media.FontWeight.SemiBold; break;
					case "800": style.FontWeight = Avalonia.Media.FontWeight.ExtraBold; break;
					case "900": style.FontWeight = Avalonia.Media.FontWeight.Black; break;
					default: reader.Warn(e, $"the font-weight {weight} is not one the reader knows"); break;
				}
			}
			string? anchor = (string?)e.Attribute("text-anchor");
			if (anchor != null)
			{
				if (anchor is "start" or "middle" or "end")
				{
					style.Anchor = anchor;
				}
				else
				{
					reader.Warn(e, $"the text-anchor {anchor} is not start, middle or end");
				}
			}
			string? fill = (string?)e.Attribute("fill");
			if (fill != null)
			{
				style.Fill = fill;
			}
			return style;
		}
	}
}
