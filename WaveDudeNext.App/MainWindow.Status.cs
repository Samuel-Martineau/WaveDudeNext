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

using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using NLog;
using WaveDudeNext.App.Docking;
using WaveDudeNext.App.Logging;

namespace WaveDudeNext.App;

/// <summary>
/// The status bar's message: the last log line of Info and up, from the <see cref="StatusBarTarget"/>, in the colours of
/// its level; a double-click on the bar opens the Messages pane. The run's text and the export's progress are bound in
/// the markup.
/// </summary>
public partial class MainWindow
{
	/// <summary>Hears the status bar's log target from the window's first show to its closing.</summary>
	private void AttachStatusBar()
	{
		StatusBarTarget.Attach(ShowStatusLine);
		Closed += (_, _) => StatusBarTarget.Detach(ShowStatusLine);
		statusBar.DoubleTapped += (_, _) => ShowMessages();
	}

	private void ShowStatusLine(StatusLine line)
	{
		txtStatus.Text = line.Text;
		ToolTip.SetTip(txtStatus, line.Exception?.ToString());
		string background = line.Level >= LogLevel.Error ? "StatusBarErrorBackground" : line.Level == LogLevel.Warn ? "StatusBarWarnBackground" : "StatusBarBackground";
		string foreground = line.Level >= LogLevel.Error ? "StatusBarErrorForeground" : line.Level == LogLevel.Warn ? "StatusBarWarnForeground" : "StatusBarForeground";
		// bound, not looked up: a lookup without a theme variant skips the Dark/Light theme dictionaries these brushes
		// live in, and a binding also follows a theme switch
		statusBar[!Border.BackgroundProperty] = new DynamicResourceExtension(background);
		txtStatus[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(foreground);
	}

	/// <summary>Shows the Messages pane and focuses its list (the status bar's double-click).</summary>
	public void ShowMessages()
	{
		foreach (DockItem item in dockHost.Items)
		{
			if (item.Id == "messages")
			{
				dockHost.Show(item);
			}
		}
		messagesPane.FocusList();
	}
}
