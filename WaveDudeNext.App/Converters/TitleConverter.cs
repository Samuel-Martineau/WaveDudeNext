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

using System.Globalization;
using Avalonia.Data.Converters;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// Builds the window title from the application name, the open file, the dirty flag and the suggested file name, as
/// the WPF head's converter of the same name: the non-empty parts joined with " - ", the suggested name standing in for
/// a missing file (an unsaved import or a shipped example), "Untitled" when an unsaved project has no name at all (the
/// word in the current language's strings; the language is the last input, so a switch re-converts), and an asterisk
/// after it for unsaved edits.
/// </summary>
public sealed class TitleConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		string title = values.Count > 0 ? values[0]?.ToString() ?? "" : "";
		string file = values.Count > 1 ? values[1]?.ToString() ?? "" : "";
		bool dirty = values.Count > 2 && values[2] is true;
		if (file.Length == 0 && values.Count > 3)
		{
			file = values[3]?.ToString() ?? "";
		}
		if (file.Length == 0)
		{
			file = Dialogs.DialogStrings.Lookup("Untitled");
		}
		if (dirty)
		{
			file += "*";
		}
		return title + " - " + file;
	}
}
