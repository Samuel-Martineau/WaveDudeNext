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
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Dialogs;

/// <summary>
/// The string table as the dialogs and the help windows read it in code: a key's text from the application's merged
/// resources (the <c>Strings/Strings.XX.axaml</c> dictionary the language switch swaps), the key itself when no table
/// has it, as upstream's <c>TryFindResource</c> fallback did. Also the dialogs' access letters: upstream's dialogs let
/// the underlined letter of a button work without Alt (the XAML comments of MessageWindow and MemoryWarningWindow),
/// which Avalonia's access keys do not, so <see cref="TryPressAccessLetter"/> adds it. UI thread.
/// </summary>
internal static class DialogStrings
{
	/// <summary>The text of a string table key, or the key when no table has it.</summary>
	public static string Lookup(string key)
	{
		Application? application = Application.Current;
		if (application != null && application.TryGetResource(key, application.ActualThemeVariant, out object? value) && value is string text)
		{
			return text;
		}
		return key;
	}

	/// <summary>A key's text with the access key underscore removed, for an automation name or a log line.</summary>
	public static string Plain(string key) => StripAccessKey(Lookup(key));

	/// <summary>A caption without its access key underscore ("_Don't save" to "Don't save"); a doubled underscore stays one.</summary>
	public static string StripAccessKey(string caption)
	{
		int i = caption.IndexOf('_');
		return i < 0 ? caption : caption.Remove(i, 1);
	}

	/// <summary>
	/// A letter pressed without modifiers presses the button of <paramref name="root"/> whose caption underlines it, as
	/// upstream's dialogs did without Alt (the underlined letters show while Alt is held, as everywhere else); true when a
	/// button took it. A caption is a string with the underscore before the letter.
	/// </summary>
	public static bool TryPressAccessLetter(Visual root, KeyEventArgs e)
	{
		if (e.KeyModifiers != KeyModifiers.None || e.KeySymbol is not { Length: 1 } symbol || !char.IsLetter(symbol[0]))
		{
			return false;
		}
		foreach (Button button in root.GetVisualDescendants().OfType<Button>())
		{
			if (!button.IsEffectivelyEnabled || !button.IsEffectivelyVisible || button.Content is not string caption)
			{
				continue;
			}
			int underscore = caption.IndexOf('_');
			if (underscore >= 0 && underscore + 1 < caption.Length && char.ToUpperInvariant(caption[underscore + 1]) == char.ToUpperInvariant(symbol[0]))
			{
				button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
				return true;
			}
		}
		return false;
	}
}
