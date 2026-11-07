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

using System.Windows;
using Microsoft.Win32;
using WaveDudeNext.Export;
using WaveDudeNext.UI;
using WaveDudeNext.WPF.Dialogs;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// The export dialogs for the models of <c>WaveDudeNext.UI</c> (<see cref="IExportDialogs"/>): the common Save dialog
/// with the title and the filters of the string table, its filter order being <see cref="ExportFormats.All"/>'s,
/// <see cref="ExportFormats.Stills"/>' for a still and one CSV filter for a probe's data; and the application's message
/// window for the question about a sequence folder. Both modal over <paramref name="owner"/>.
/// </summary>
/// <param name="owner">The window the dialogs belong to, whose resources hold the strings.</param>
public sealed class WpfExportDialogs(Window owner) : IExportDialogs
{
	/// <inheritdoc/>
	public SavePick? AskWhereToSave(ExportSubject subject, string folder, string name, IReadOnlyList<ExportFormat> formats, int formatIndex)
	{
		var dialog = new SaveFileDialog
		{
			Title = Text(subject switch
			{
				ExportSubject.Field => "ExportDialogTitleField",
				ExportSubject.Intensity => "ExportDialogTitleIntensity",
				ExportSubject.Both => "ExportDialogTitleBoth",
				ExportSubject.Still => "ExportDialogTitleFrame",
				_ => "ExportDialogTitleProbeCsv",
			}),
			Filter = Text(subject switch
			{
				ExportSubject.Still => "ExportDialogFilterStill",
				ExportSubject.ProbeData => "ExportDialogFilterCsv",
				_ => "ExportDialogFilter",
			}),
			FilterIndex = formatIndex + 1,
			AddExtension = true,
			DefaultExt = formats.Count > 0 ? ExportFormats.Extension(formats[Math.Clamp(formatIndex, 0, formats.Count - 1)]) : ProbeCsv.Extension,
			FileName = name,
			InitialDirectory = folder,
		};
		return dialog.ShowDialog(owner) == true ? new SavePick(dialog.FileName, Math.Max(0, dialog.FilterIndex - 1)) : null;
	}

	/// <inheritdoc/>
	public bool AskToReplaceFolder(string folder) =>
		MessageWindow.Show(owner, string.Format(Text("ExportFolderExistsFormat"), folder), MessageKind.Question, MessageButtons.YesNo) == MessageAnswer.Yes;

	private string Text(string key) => (string)owner.FindResource(key);
}
