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

using System.Windows.Input;
using WaveDudeNext.Model;
using WaveDudeNext.UI;
using WaveDudeNext.WPF.Dialogs;

namespace WaveDudeNext.WPF;

/// <summary>
/// Handlers of <see cref="Commands.ExportCommands"/> (2026-10-14), one line each into <see cref="Exporter"/>, which
/// carries the exports out; the window shows what fails (<see cref="Exporter.Failed"/>) and keeps where exports go
/// (<see cref="ExportPaths"/>) for it.
/// </summary>
public partial class MainWindow
{
	/// <summary>Where exports go: what the export dialogs start with and what Export last repeats; created in the constructor.</summary>
	private readonly ExportPaths _exportPaths;

	private void CanExport(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Exporter.CanExport;

	private void OnExportField(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportAsync(field: true, intensity: false);

	private void OnExportIntensity(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportAsync(field: false, intensity: true);

	private void OnExportBoth(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportAsync(field: true, intensity: true);

	private void OnExportFieldFrame(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportAsync(field: true, intensity: false, still: true);

	private void OnExportIntensityFrame(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportAsync(field: false, intensity: true, still: true);

	private void CanExportProbeCsv(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Exporter.CanExportProbe(e.Parameter);

	private void OnExportProbeCsv(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is Probe probe)
		{
			_ = Exporter.ExportProbeAsync(probe);
		}
	}

	private void CanExportLast(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Exporter.CanExportLast;

	private void OnExportLast(object sender, ExecutedRoutedEventArgs e) => _ = Exporter.ExportLastAsync();

	/// <summary>An export failed: the text of the key, with the error's words under it when there is one.</summary>
	private void OnExportFailed(object? sender, ExportFailure failure)
	{
		string message = (string)FindResource(failure.Key);
		if (failure.Error is { } error)
		{
			ShowError(message, error);
		}
		else
		{
			MessageWindow.Show(this, message, MessageKind.Error, MessageButtons.Ok);
		}
	}
}
