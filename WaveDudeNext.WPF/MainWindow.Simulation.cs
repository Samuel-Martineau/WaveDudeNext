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

using System.IO;
using System.Windows.Input;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF;

/// <summary>
/// Handlers of <see cref="Commands.SimulationCommands"/>, one line each into <see cref="RunHost"/> (the current run and
/// its store) and <see cref="LivePreview"/> (what Play does); the window shows the error of a run the user asked for.
/// </summary>
public partial class MainWindow
{
	/// <summary>
	/// The choices of Simulation > RAM cache size: <see cref="FrameMemory.Automatic"/> (the application
	/// decides from what is free) and 10 % to 100 % of the RAM as an upper bound (2026-10-15, the user's design).
	/// </summary>
	public static int[] FrameMemoryOptions { get; } = [FrameMemory.Automatic, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

	private void CanRun(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !RunHost.IsActive && !Busy.IsExporting;

	private void OnRun(object sender, ExecutedRoutedEventArgs e)
	{
		try
		{
			RunHost.Start();
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Error(ex, "The run could not start: {Message}", ex.Message);
			ShowError((string)FindResource("RunError"), ex);
		}
	}

	private void CanControlRun(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = RunHost.IsActive;

	/// <summary>Stop applies to a run in progress, or to an export in progress (Esc cancels it).</summary>
	private void CanStop(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = RunHost.IsActive || Exporter.IsWriting;

	/// <summary>Play pressed: a missing or stale run is started (<see cref="LivePreview.EnsureRun"/>).</summary>
	private void OnEnsureRun(object sender, ExecutedRoutedEventArgs e) => Live.EnsureRun();

	private void OnTogglePause(object sender, ExecutedRoutedEventArgs e) => RunHost.TogglePause();

	/// <summary>The timeline's Play and Pause as one command of the window, F7 (2026-11-04): handed to the preview's own binding.</summary>
	private void CanPlayPause(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = MediaCommands.TogglePlayPause.CanExecute(null, previewPane);

	private void OnPlayPause(object sender, ExecutedRoutedEventArgs e) => MediaCommands.TogglePlayPause.Execute(null, previewPane);

	/// <summary>Cancels the export when one is running (it has the user's attention then), else stops the run.</summary>
	private void OnStop(object sender, ExecutedRoutedEventArgs e)
	{
		if (Exporter.IsWriting)
		{
			Exporter.Cancel();
		}
		else
		{
			RunHost.Stop();
		}
	}

	/// <summary>The project is being replaced: an export reading the run's store ends with what it has, then the run goes (<see cref="RunHost.Discard"/>).</summary>
	private void DiscardRun()
	{
		if (Exporter.IsWriting)
		{
			Logger.Warn("Export cancelled: its run is being discarded");
			Exporter.Cancel();
		}
		_ = RunHost.Discard();
	}
}
