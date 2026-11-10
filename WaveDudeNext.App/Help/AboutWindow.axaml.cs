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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using NLog;
using WaveDudeNext.App.Dialogs;
using WaveDudeNext.App.Theming;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Help;

/// <summary>
/// The About dialog (Help > About), as upstream's: product, version and copyright from <see cref="AboutInfo"/>, the
/// credits as in the source header with their links, the license sentence with the embedded LICENSE.md rendered on
/// request, and the system report with a Copy button. Modal, owned by the main window, closed by Esc or Enter; every
/// link and button is in the Tab cycle, the focus starts on Close. Shown with <c>ShowDialog(owner)</c>. UI thread only.
/// </summary>
public partial class AboutWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	/// <summary>The document box's height while the window is sized to its content (the XAML value).</summary>
	private const double LicenseBoxHeight = 260;
	/// <summary>The grid row of the license document and of the filler above the buttons (the XAML's RowDefinitions).</summary>
	private const int LicenseRow = 3, FillerRow = 5;
	private readonly string _report;
	private MarkdownDocument? _license;

	/// <summary>Builds the dialog and gathers the system report once.</summary>
	public AboutWindow()
	{
		InitializeComponent();
		productText.Text = AboutInfo.Product;
		versionText.Text = string.Format(DialogStrings.Lookup("AboutVersionFormat"), AboutInfo.Version);
		copyrightText.Text = AboutInfo.Copyright;
		creditVleggaar.Text = AboutInfo.CreditVleggaar;
		creditWaveSimulator2D.Text = AboutInfo.CreditWaveSimulator2D;
		creditPort.Text = AboutInfo.CreditPort;
		_report = AboutInfo.SystemReport(Settings.Default.UseGpu, DialogStrings.Lookup);
		systemText.Text = _report;
		Opened += (_, _) => closeButton.Focus();
		AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
		// the UI zoom on every window of the app, as upstream's Window style applied it
		WindowZoom.Attach(this);
	}

	/// <summary>Whether the license text is shown, for the tests.</summary>
	public bool IsLicenseShown => licenseBox.IsVisible;

	/// <summary>Esc closes even while the text box has the focus.</summary>
	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
		else if (DialogStrings.TryPressAccessLetter(this, e))
		{
			e.Handled = true;
		}
	}

	/// <summary>A credit or license link: the address for its automation id, opened in the browser.</summary>
	private void OnLink(object? sender, RoutedEventArgs e)
	{
		string? url = sender is Control control ? AutomationProperties.GetAutomationId(control) switch
		{
			"AboutLinkHuygensYouTube" => AboutInfo.HuygensYouTube,
			"AboutLinkHuygensSite" => AboutInfo.HuygensSite,
			"AboutLinkDiffractionYouTube" => AboutInfo.DiffractionYouTube,
			"AboutLinkWaveSimulator2D" => AboutInfo.WaveSimulator2D,
			"AboutLinkGpl" => AboutInfo.Gpl,
			_ => null,
		} : null;
		if (url != null)
		{
			OpenInBrowser(url);
		}
	}

	/// <summary>Opens an address in the user's browser through the desktop (xdg-open, ShellExecute); a failure is logged, never shown.</summary>
	internal static void OpenInBrowser(string url)
	{
		Logger.Debug("About: opening {Uri}", url);
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException)
		{
			Logger.Warn(ex, "About: could not open {Uri}", url);
		}
	}

	private void OnToggleLicense(object? sender, RoutedEventArgs e)
	{
		bool show = !licenseBox.IsVisible;
		if (show && _license == null)
		{
			_license = MarkdownDocument.Render(AboutInfo.LicenseMarkdown(), new MarkdownRenderOptions { TightLists = false, Name = "LICENSE.md" });
			_license.LinkActivated += (_, args) =>
			{
				if (!args.Target.StartsWith('#'))
				{
					OpenInBrowser(args.Target);
				}
			};
			licenseView.Content = _license.Root;
		}
		licenseBox.IsVisible = show;
		// the extra height of a resized window goes to the document while it is shown, else to the gap above the buttons
		layout.RowDefinitions[LicenseRow].Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
		layout.RowDefinitions[FillerRow].Height = show ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
		licenseToggle[!ContentProperty] = new DynamicResourceExtension(show ? "AboutHideLicense" : "AboutShowLicense");
		// the window follows its content while the document is hidden (it grows by the box's fixed height when the
		// document appears and shrinks when it goes) and is sized by hand while the document is shown, since a window
		// sized to content snaps back after a resize and a star row under content sizing would take the whole document;
		// the switch waits for the layout that lets the window grow, then frees the box to fill its row
		if (show)
		{
			Dispatcher.UIThread.Post(() =>
			{
				SizeToContent = SizeToContent.Manual;
				licenseBox.Height = double.NaN;
			}, DispatcherPriority.Loaded);
		}
		else
		{
			licenseBox.Height = LicenseBoxHeight;
			SizeToContent = SizeToContent.Height;
		}
	}

	private async void OnCopy(object? sender, RoutedEventArgs e)
	{
		try
		{
			Avalonia.Input.Platform.IClipboard? clipboard = Clipboard;
			if (clipboard == null)
			{
				Logger.Warn("About: no clipboard on this platform");
				return;
			}
			await clipboard.SetTextAsync(_report);
			Logger.Info(DialogStrings.Lookup("AboutCopied"));
		}
		catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.ExternalException)
		{
			Logger.Warn(ex, "About: the clipboard is held by another program");
		}
	}

	private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
