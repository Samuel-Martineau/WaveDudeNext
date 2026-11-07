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

using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The taskbar button's progress state from what the application is busy with (<see cref="BusyKind"/>; the Windows 7
/// taskbar progress, which WPF exposes as <see cref="TaskbarItemInfo"/>; the user's rule, 2026-10-14): an export
/// writing frames fills the button as the frames go (<see cref="TaskbarItemProgressState.Normal"/>, the value bound
/// beside it), an export waiting for its run or a frame being computed shows the marquee, an idle application nothing.
/// </summary>
public sealed class TaskbarProgressConverter : IValueConverter
{
	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
	{
		BusyKind.Exporting => TaskbarItemProgressState.Normal,
		BusyKind.WaitingForRun or BusyKind.ComputingFrame => TaskbarItemProgressState.Indeterminate,
		_ => TaskbarItemProgressState.None,
	};

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
