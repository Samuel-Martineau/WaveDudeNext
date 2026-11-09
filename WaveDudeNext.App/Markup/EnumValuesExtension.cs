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

namespace WaveDudeNext.App.Markup;

/// <summary>
/// All values of an enum, for an <c>ItemsSource</c>. The type is written with its XAML prefix, for example
/// <c>{meta:EnumValues model:SceneMode}</c> (upstream's <c>EnumValuesExtension</c>).
/// </summary>
public sealed class EnumValuesExtension
{
	/// <summary>Creates the extension for one enum.</summary>
	/// <param name="type">The enum type with its XAML prefix.</param>
	public EnumValuesExtension(string type)
	{
		Type = type;
	}

	/// <summary>The enum type with its XAML prefix.</summary>
	public string Type { get; set; }

	/// <summary>The enum's values in declaration order.</summary>
	public Array ProvideValue(IServiceProvider serviceProvider)
	{
		Type type = ModelMemberExtension.ResolveType(serviceProvider, Type);
		if (!type.IsEnum)
		{
			throw new ArgumentException($"'{Type}' is not an enum.");
		}
		return Enum.GetValues(type);
	}
}
