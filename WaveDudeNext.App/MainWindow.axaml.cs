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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NLog;
using WaveDudeNext.App.Docking;
using WaveDudeNext.App.Theming;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The main window of the Avalonia head (2026-11-09): the menu bar, the dock host with the three panes around the preview,
/// the status bar. As this step leaves it, the window is the frame alone: the dock layout and the placement are kept in
/// the user settings as the WPF window keeps them, and the UI zoom applies; the models of <c>WaveDudeNext.UI</c>, the
/// panes' bindings and the commands come with the next steps, each as the WPF window has it.
/// </summary>
public partial class MainWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Creates the window: the dock layout and the placement from the settings, the UI zoom.</summary>
	public MainWindow()
	{
		InitializeComponent();
		RestoreLayout();
		RestorePlacement();
		Closing += (_, _) =>
		{
			Settings.Default.Layout = dockHost.SaveLayout();
			SavePlacement();
			Settings.Default.TrySave();
		};
		WindowZoom.Attach(this);
		KeyDown += OnWindowKeyDown;
	}

	/// <summary>The menu bar is out of the Tab cycle: F10 is the way in (the WPF head's rule). Menu.Open is Avalonia's answer to Alt and F10: the first header takes the keyboard focus, Down opens it, Left and Right move between headers.</summary>
	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (!e.Handled && e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None)
		{
			menu.Open();
			e.Handled = true;
		}
	}

	/// <summary>The saved dock layout, else the default arrangement the markup declares; failing both, the two panes at their sides.</summary>
	private void RestoreLayout()
	{
		bool restored = Settings.Default.Layout is { } saved && dockHost.LoadLayout(saved);
		if (!restored && dockHost.DefaultLayout != null)
		{
			restored = dockHost.LoadLayout(dockHost.DefaultLayout);
		}
		if (!restored)
		{
			Logger.Warn("No dock layout could be loaded: the Project and Properties panes take their default sides");
			foreach (DockItem item in dockHost.Items)
			{
				if (item.Id is "project" or "properties")
				{
					dockHost.Show(item);
				}
			}
		}
	}

	/// <summary>The window's size, position and state as the last session left them (the size at zoom 1, which the zoom multiplies).</summary>
	private void RestorePlacement()
	{
		Settings settings = Settings.Default;
		Width = settings.WindowWidth;
		Height = settings.WindowHeight;
		if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
		{
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = new PixelPoint((int)Math.Round(settings.WindowLeft), (int)Math.Round(settings.WindowTop));
		}
		if (settings.WindowState == WindowMode.Maximized)
		{
			WindowState = WindowState.Maximized;
		}
	}

	/// <summary>Never saved as minimised; a maximised window keeps its normal size for the next start.</summary>
	private void SavePlacement()
	{
		Settings settings = Settings.Default;
		settings.WindowState = WindowState == WindowState.Maximized ? WindowMode.Maximized : WindowMode.Normal;
		if (WindowState == WindowState.Normal)
		{
			settings.WindowWidth = Width / WindowZoom.Scale;
			settings.WindowHeight = Height / WindowZoom.Scale;
			settings.WindowLeft = Position.X;
			settings.WindowTop = Position.Y;
		}
	}
}
