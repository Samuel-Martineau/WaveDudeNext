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
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The status bar's text of the run, as the WPF head's converter of the same name: "State: steps / total, engine" from
/// the string table, with the stale suffix when the project was edited since; empty while there is no run. The
/// inputs are the run's state, steps done, total steps and engine description, the stale flag and the language, so a
/// switch re-converts.
/// </summary>
public sealed class RunStatusConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Count < 4 || values[0] is not RunState state)
		{
			return "";
		}
		string stateText = Dialogs.DialogStrings.Lookup("RunState" + state);
		string format = Dialogs.DialogStrings.Lookup("RunStatusFormat");
		string text = string.Format(culture, format, stateText, values[1], values[2], values[3]);
		if (values.Count > 4 && values[4] is true)
		{
			text += Dialogs.DialogStrings.Lookup("RunStatusStale");
		}
		return text;
	}
}
