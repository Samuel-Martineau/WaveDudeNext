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

using WaveDudeNext.Export;

namespace WaveDudeNext.UI;

/// <summary>What an export's Save dialog is for; the application's words and filters follow from it.</summary>
public enum ExportSubject
{
	/// <summary>The field's pictures of a run.</summary>
	Field,
	/// <summary>The intensity's pictures of a run.</summary>
	Intensity,
	/// <summary>Both layers' pictures, two outputs from one name.</summary>
	Both,
	/// <summary>One frame as a single image file.</summary>
	Still,
	/// <summary>A probe's recording as CSV.</summary>
	ProbeData,
}

/// <summary>The user's pick in an export's Save dialog.</summary>
/// <param name="Path">The file chosen, as typed; the extension may not be the format's yet.</param>
/// <param name="FormatIndex">The place of the filter chosen in the list of formats offered; 0 where one filter was offered.</param>
public sealed record SavePick(string Path, int FormatIndex);

/// <summary>
/// The dialogs of an export as the models of this library ask them (2026-11-07; each application implements it, WPF
/// with the common Save dialog and its message window): where a file goes, and whether a sequence folder with files
/// in it is replaced. Asked on the UI thread and answered before they return.
/// </summary>
public interface IExportDialogs
{
	/// <summary>The Save dialog for <paramref name="subject"/>, starting in <paramref name="folder"/> with <paramref name="name"/> proposed.</summary>
	/// <param name="subject">What is exported: the application's title and filters follow from it.</param>
	/// <param name="folder">The folder the dialog opens in; "" for the system's choice.</param>
	/// <param name="name">The file name proposed, without a folder.</param>
	/// <param name="formats">The formats offered, in the order of the application's filters; empty for a subject with one filter (the probe data).</param>
	/// <param name="formatIndex">The place in <paramref name="formats"/> of the one offered first.</param>
	/// <returns>The pick, or null for Cancel.</returns>
	SavePick? AskWhereToSave(ExportSubject subject, string folder, string name, IReadOnlyList<ExportFormat> formats, int formatIndex);

	/// <summary>Asks whether a sequence may go into <paramref name="folder"/>, which exists and holds files; true for yes.</summary>
	bool AskToReplaceFolder(string folder);
}
