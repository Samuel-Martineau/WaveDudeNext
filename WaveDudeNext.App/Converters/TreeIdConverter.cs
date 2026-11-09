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
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The automation id of a project tree row from its node: "Tree:" plus the item's name (sources, parts, probes, colour
/// schemes), a folder's key ("Tree:TreeSources"), or the type name of the fixed nodes ("Tree:Scene", "Tree:Project"), so
/// a harness can reach a row with Tab and act on it. Bound to the row's node without a path, so it is set once per object
/// and keeps the old id after a rename, as upstream's did (the ui_check scripts rely on that).
/// </summary>
public sealed class TreeIdConverter : IValueConverter
{
	/// <inheritdoc />
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => "Tree:" + value switch
	{
		INamed named => named.Name,
		NamedCollection folder => folder.Key,
		null => "",
		_ => value.GetType().Name,
	};

	/// <inheritdoc />
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
