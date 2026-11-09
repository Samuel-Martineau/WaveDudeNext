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

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// The build's converter of the WPF string tables into Avalonia resource dictionaries (StringTables.targets, 2026-11-09).
/// A table is a WPF ResourceDictionary of s:String entries and nothing else, so three textual substitutions make it an
/// Avalonia one: the default namespace, the System namespace declaration (Avalonia's x:String needs none) and the
/// element's prefix. Texts, entities and comments come through untouched. An inline MSBuild task: C# 7.3 and
/// netstandard2.0, for Visual Studio's MSBuild; not part of the application.
/// </summary>
public class ConvertStringTables : Task
{
	private const string WpfNamespace = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
	private const string AvaloniaNamespace = "xmlns=\"https://github.com/avaloniaui\"";
	private static readonly Regex SystemNamespace = new Regex("\\s+xmlns:s=\"clr-namespace:System;assembly=mscorlib\"");

	/// <summary>The WPF tables.</summary>
	[Required]
	public ITaskItem[] Sources { get; set; }

	/// <summary>The folder the Avalonia dictionaries are written to, one per table under the table's file name with the .axaml extension.</summary>
	[Required]
	public string Destination { get; set; }

	/// <inheritdoc/>
	public override bool Execute()
	{
		Directory.CreateDirectory(Destination);
		foreach (ITaskItem source in Sources)
		{
			string text = File.ReadAllText(source.ItemSpec);
			if (!text.Contains(WpfNamespace))
			{
				Log.LogError("{0} is not a WPF resource dictionary: the presentation namespace is missing", source.ItemSpec);
				return false;
			}
			text = text.Replace(WpfNamespace, AvaloniaNamespace);
			text = SystemNamespace.Replace(text, "", 1);
			text = text.Replace("<s:String ", "<x:String ").Replace("</s:String>", "</x:String>");
			if (text.Contains("s:String"))
			{
				Log.LogError("{0}: an s:String survived the conversion", source.ItemSpec);
				return false;
			}
			string target = Path.Combine(Destination, Path.GetFileNameWithoutExtension(source.ItemSpec) + ".axaml");
			// written only when it differs, so an unchanged table leaves Avalonia's inputs alone
			if (!File.Exists(target) || File.ReadAllText(target) != text)
			{
				File.WriteAllText(target, text, new UTF8Encoding(false));
				Log.LogMessage(MessageImportance.Normal, "String table converted: {0}", target);
			}
		}
		return true;
	}
}
