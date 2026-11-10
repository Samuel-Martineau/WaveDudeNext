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

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// The build's converter of the WPF resource dictionaries both heads share into Avalonia ones (SharedXaml.targets;
/// 2026-11-09 for the string tables, 2026-11-10 for the icon geometries and the colour tables). Such a file holds
/// s:String, StreamGeometry or SolidColorBrush entries and nothing else, so three textual substitutions make it an
/// Avalonia dictionary: the default namespace, the System namespace declaration (Avalonia's x:String needs none) and
/// the string element's prefix; the path markup and the colours are the same in both frameworks. Texts, entities and
/// comments come through untouched. Each source lands under its Folder metadata (Strings, Themes) with the .axaml
/// extension. An inline MSBuild task: C# 7.3 and netstandard2.0, for Visual Studio's MSBuild; not part of the
/// application.
/// </summary>
public class ConvertSharedXaml : Task
{
	private const string WpfNamespace = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
	private const string AvaloniaNamespace = "xmlns=\"https://github.com/avaloniaui\"";
	private static readonly Regex SystemNamespace = new Regex("\\s+xmlns:s=\"clr-namespace:System;assembly=mscorlib\"");

	/// <summary>The WPF dictionaries, each with a Folder metadata naming the folder it is compiled under.</summary>
	[Required]
	public ITaskItem[] Sources { get; set; }

	/// <summary>The folder the Avalonia dictionaries are written to, one per source under its Folder and file name with the .axaml extension.</summary>
	[Required]
	public string Destination { get; set; }

	/// <inheritdoc/>
	public override bool Execute()
	{
		foreach (ITaskItem source in Sources)
		{
			string folder = source.GetMetadata("Folder");
			if (string.IsNullOrEmpty(folder))
			{
				Log.LogError("{0} names no Folder to be compiled under", source.ItemSpec);
				return false;
			}
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
			string directory = Path.Combine(Destination, folder);
			Directory.CreateDirectory(directory);
			string target = Path.Combine(directory, Path.GetFileNameWithoutExtension(source.ItemSpec) + ".axaml");
			// written only when it differs, so an unchanged source leaves Avalonia's inputs alone
			if (!File.Exists(target) || File.ReadAllText(target) != text)
			{
				File.WriteAllText(target, text, new UTF8Encoding(false));
				Log.LogMessage(MessageImportance.Normal, "Shared dictionary converted: {0}", target);
			}
		}
		return true;
	}
}
