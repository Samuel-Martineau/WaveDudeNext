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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using NLog;
using WaveDudeNext.App.Commands;
using WaveDudeNext.UI;
using WaveDudeNext.UI.Theming;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// The colour theme (View > Theme): System, Light or Dark. Upstream swapped the merged colour table (Dark.xaml or
/// Light.xaml) among the application resources and told the DWM about the title bar; here the two tables are the
/// application's theme dictionaries (App.axaml), so the switch is <see cref="Application.RequestedThemeVariant"/>:
/// Light and Dark name their table, System is <see cref="ThemeVariant.Default"/>, which follows the desktop's
/// preference through Avalonia's platform settings. Every style reads its brushes through DynamicResource, so a switch
/// restyles the whole application live; the window chrome is Avalonia's own and follows the variant by itself. The
/// choice is written to <see cref="Settings.Theme"/>; nothing is saved here, the window saves the settings when it
/// closes. Bind menus to <see cref="Options"/>, <see cref="Current"/> and <see cref="SelectCommand"/>. UI thread only.
/// </summary>
public sealed class ThemeController : INotifyPropertyChanged
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private AppTheme _current = AppTheme.Dark;

	private ThemeController()
	{
		SelectCommand = new RelayCommand<AppTheme>(theme => Current = theme);
	}

	/// <summary>The single theme state of the application.</summary>
	public static ThemeController Instance { get; } = new();

	/// <summary>All selectable themes, in menu order: System, Light, Dark.</summary>
	public IReadOnlyList<AppTheme> Options { get; } = Enum.GetValues<AppTheme>();

	/// <summary>Sets <see cref="Current"/> to its parameter, an <see cref="AppTheme"/>.</summary>
	public ICommand SelectCommand { get; }

	/// <summary>The theme the user chose. Setting it restyles the application at once and writes the setting.</summary>
	public AppTheme Current
	{
		get => _current;
		set
		{
			if (_current == value)
			{
				return;
			}
			_current = value;
			Settings.Default.Theme = value;
			Logger.Info("Theme changed to {Theme}", value);
			ApplyVariant();
			OnPropertyChanged();
		}
	}

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Takes the theme from the settings and applies it; called once at start-up, before the main window shows.</summary>
	public void Apply()
	{
		_current = Settings.Default.Theme;
		Logger.Debug("Theme: {Theme}", _current);
		ApplyVariant();
		OnPropertyChanged(nameof(Current));
	}

	/// <summary>The theme variant an <see cref="AppTheme"/> stands for.</summary>
	public static ThemeVariant VariantOf(AppTheme theme) => theme switch
	{
		AppTheme.Light => ThemeVariant.Light,
		AppTheme.Dark => ThemeVariant.Dark,
		_ => ThemeVariant.Default,
	};

	private void ApplyVariant()
	{
		// no application while the settings are read outside Avalonia (the CLI, a unit test): nothing to restyle
		if (Application.Current is { } app)
		{
			app.RequestedThemeVariant = VariantOf(_current);
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
