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

using System.Runtime.InteropServices;
using System.Windows;
using NLog;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// The Windows clipboard for the models of <c>WaveDudeNext.UI</c> (<see cref="IClipboard"/>): WPF's
/// <see cref="Clipboard"/>, and Windows' own count of the clipboard's changes. Another program may hold the clipboard
/// open at any moment; then nothing is copied (logged as a warning) or nothing read. UI thread.
/// </summary>
public sealed class WpfClipboard : IClipboard
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <inheritdoc/>
	public uint ChangeCount => GetClipboardSequenceNumber();

	/// <inheritdoc/>
	public bool TrySetText(string format, string text)
	{
		var data = new DataObject();
		data.SetData(format, text);
		data.SetText(text);
		try
		{
			Clipboard.SetDataObject(data, copy: true);
			return true;
		}
		catch (ExternalException ex)
		{
			// another program holds the clipboard open
			Logger.Warn(ex, "The clipboard is busy: nothing was copied");
			return false;
		}
	}

	/// <inheritdoc/>
	public bool TryGetText(string format, out string? text)
	{
		text = null;
		try
		{
			if (Clipboard.GetDataObject() is { } data)
			{
				text = data.GetDataPresent(format) ? data.GetData(format) as string
					: data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string
					: null;
			}
			return true;
		}
		catch (ExternalException)
		{
			// another program holds the clipboard open; the next question tries again
			return false;
		}
		catch (InvalidOperationException)
		{
			// what the clipboard holds cannot be had as text after all: as good as none
			text = null;
			return true;
		}
	}

	/// <summary>Windows' count of the clipboard's changes; 0 when it cannot be told.</summary>
	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();
}
