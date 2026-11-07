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

using System.Xml;
using System.Xml.Serialization;
using NLog;

namespace WaveDudeNext.UI.Docking;

/// <summary>
/// Serializable snapshot of a dock host's layout tree. Items are referenced by their ids; unknown ids are ignored on
/// load. Persisted with <see cref="XmlSerializer"/>; a host's default arrangement is written in XAML (the node classes
/// are top-level, as an XML name cannot hold a nested type's "+", 2026-10-26). Plain data that no UI framework anchors
/// (2026-11-06, the user's choice, so both heads save and declare layouts with one set of classes): no content
/// property, which is an attribute of WPF's or of Avalonia's, so the XAML names <c>SplitLayout.Children</c> and
/// <c>GroupLayout.Items</c> as property elements, and a split's direction is a <see cref="LayoutDirection"/>.
/// </summary>
[XmlRoot("DockLayout")]
public sealed class DockLayout
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private static readonly XmlSerializer Serializer = new(typeof(DockLayout));

	/// <summary>Root of the tree: a split, a group, or the document node.</summary>
	[XmlElement("Split", typeof(SplitLayout))]
	[XmlElement("Group", typeof(GroupLayout))]
	[XmlElement("Document", typeof(DocumentLayout))]
	public NodeLayout? Root { get; set; }

	/// <summary>Writes the layout as indented XML.</summary>
	public void Save(Stream stream)
	{
		using var writer = XmlWriter.Create(stream, new XmlWriterSettings
		{
			Indent = true,
			IndentChars = "\t",
			Encoding = new System.Text.UTF8Encoding(false),
		});
		Serializer.Serialize(writer, this, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));
	}

	/// <summary>Reads a layout written by <see cref="Save"/>. Returns null on malformed input.</summary>
	public static DockLayout? Load(Stream stream)
	{
		try
		{
			using var reader = XmlReader.Create(stream);
			return Serializer.Deserialize(reader) as DockLayout;
		}
		catch (InvalidOperationException ex)
		{
			Logger.Warn(ex, "The layout file does not hold a dock layout");
			return null;
		}
		catch (XmlException ex)
		{
			Logger.Warn(ex, "The layout file is not well-formed XML");
			return null;
		}
	}
}

/// <summary>A node of a <see cref="DockLayout"/>: a split, a group or the document area; <see cref="Length"/> is its share in the parent split.</summary>
public abstract class NodeLayout
{
	/// <summary>The node's share of its parent split along the split's direction; only the ratio between siblings matters.</summary>
	[XmlAttribute]
	public double Length { get; set; } = 1.0;
}

/// <summary>A split of a <see cref="DockLayout"/>, its children in visual order.</summary>
public sealed class SplitLayout : NodeLayout
{
	/// <summary>Horizontal: the children are columns. Vertical: the children are rows.</summary>
	[XmlAttribute]
	public LayoutDirection Orientation { get; set; }

	/// <summary>The children, left to right or top to bottom.</summary>
	[XmlElement("Split", typeof(SplitLayout))]
	[XmlElement("Group", typeof(GroupLayout))]
	[XmlElement("Document", typeof(DocumentLayout))]
	public List<NodeLayout> Children { get; set; } = new();
}

/// <summary>A tabbed group of a <see cref="DockLayout"/>: the item ids in tab order, the active one named.</summary>
public sealed class GroupLayout : NodeLayout
{
	/// <summary>The id of the item whose content is shown; null for the last one.</summary>
	[XmlAttribute]
	public string? Active { get; set; }

	/// <summary>The ids of the group's items in tab order.</summary>
	[XmlElement("Item")]
	public List<string> Items { get; set; } = new();
}

/// <summary>The document area of a <see cref="DockLayout"/>.</summary>
public sealed class DocumentLayout : NodeLayout
{
}
