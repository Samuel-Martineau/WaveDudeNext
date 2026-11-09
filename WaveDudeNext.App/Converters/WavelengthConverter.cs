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
using Avalonia;
using Avalonia.Data.Converters;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The source's estimated wavelength as text: values are the source level, the simulation's wave speed and source
/// frequency scale, and whether the 4th-order Laplacian is on (<see cref="Simulation.Wavelength"/>). "≈ 16,0 px" in
/// the current culture; with the 9-point stencil "(nominal)" is appended, because that stencil's speed varies with
/// the frequency; "–" when the inputs do not give a wavelength. A fifth input that changes with the language only
/// serves to re-read the word on a switch.
/// </summary>
public sealed class WavelengthConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Count < 4 || values[0] is not double level || values[1] is not double waveSpeed
			|| values[2] is not double scale || values[3] is not bool fourthOrder)
		{
			return "–";
		}
		double wavelength = Simulation.Wavelength(level, waveSpeed, scale);
		if (double.IsNaN(wavelength) || double.IsInfinity(wavelength))
		{
			return "–";
		}
		string text = string.Format(culture, "≈ {0:0.0} px", wavelength);
		if (fourthOrder)
		{
			return text;
		}
		// the 9-point stencil disperses: the word from the string table
		string nominal = Application.Current?.TryGetResource("WavelengthNominal", null, out object? word) == true && word is string resource ? resource : "(nominal)";
		return text + " " + nominal;
	}
}
