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

using NLog;
using WaveDudeNext.Export;

namespace WaveDudeNext.UI;

/// <summary>
/// Where exports go (a model of this library since 2026-11-06; in the main window before): what an export dialog
/// starts with, and what Export last repeats. The first export after a project is opened starts in the project's
/// folder under the project's name; every one after it where the session's last export of its kind went (2026-10-28,
/// Jeroen's report: every dialog started in the project's folder, so exports elsewhere meant navigating there each
/// time). There are three kinds, a run's pictures, a still and a probe's data, each with its own place; they are kept
/// per project file in the user settings (<see cref="ExportHint"/>, the user's choice: a path belongs to the machine,
/// not to a project file that may be shared, and an export must not dirty the project) and come back when the project
/// is opened. The application shows the dialogs and tells this model what was chosen. UI thread only.
/// </summary>
/// <param name="document">The open project: its file is the key of the kept paths, its folder where a first export starts.</param>
public sealed class ExportPaths(ProjectDocument document)
{
	/// <summary>The layer suffixes of an export's file name, which an export of both layers appends itself.</summary>
	public static readonly IReadOnlyList<string> LayerSuffixes = ["_field", "_intensity"];

	/// <summary>Projects whose export paths the settings keep (<see cref="Settings.ExportHints"/>), the oldest dropped beyond it.</summary>
	private const int HintCount = 20;

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Where the last export of a run's pictures went; null until there is one, or a kept one was restored.</summary>
	private string? _run;
	/// <summary>Where the last still went.</summary>
	private string? _still;
	/// <summary>Where the last probe data went.</summary>
	private string? _probe;

	/// <summary>A run's export as it was made: the layers, the format and the path, its extension the format's.</summary>
	/// <param name="Field">The field was exported.</param>
	/// <param name="Intensity">The intensity was exported.</param>
	/// <param name="Format">The format.</param>
	/// <param name="Path">The file, or for both layers the name their two files are made from.</param>
	public sealed record RunExport(bool Field, bool Intensity, ExportFormat Format, string Path);

	/// <summary>
	/// This session's last export of a run's pictures, which Export last makes again as it was; null before the first
	/// since a project was opened, when the command is disabled (2026-10-28, the user's request; a still is no "last
	/// export", his choice). Never restored from the settings: a repeat as the first keystroke of a session would
	/// replace a file without a question.
	/// </summary>
	public RunExport? LastRun { get; private set; }

	/// <summary>
	/// The format chosen last for a run's pictures, which the next dialog offers first; kept in the user settings as
	/// its place in <see cref="ExportFormats.All"/>.
	/// </summary>
	public ExportFormat Format
	{
		get => ExportFormats.All[Math.Clamp(Settings.Default.ExportFilterIndex, 0, ExportFormats.All.Length - 1)];
		set => Settings.Default.ExportFilterIndex = Math.Max(0, Array.IndexOf(ExportFormats.All, value));
	}

	/// <summary>Another project is open (opened, imported or new): nothing to repeat, and the dialogs start in the project's folder until <see cref="Restore"/> finds what the settings keep for it.</summary>
	public void Forget()
	{
		LastRun = null;
		_run = null;
		_still = null;
		_probe = null;
	}

	/// <summary>A project was opened from its file: the paths the settings keep for it, if any, so its dialogs start where its last exports went.</summary>
	public void Restore()
	{
		if (document.Filename is not { } file || Settings.Default.ExportHints.Find(h => string.Equals(h.Project, file, StringComparison.OrdinalIgnoreCase)) is not { } hint)
		{
			return;
		}
		_run = hint.Run.Length > 0 ? hint.Run : null;
		_still = hint.Still.Length > 0 ? hint.Still : null;
		_probe = hint.Probe.Length > 0 ? hint.Probe : null;
		Logger.Debug("Export paths restored from the settings: run {Run}, still {Still}, probe {Probe}", hint.Run, hint.Still, hint.Probe);
	}

	/// <summary>
	/// Where the dialog for a run's pictures starts and the name it proposes: the last such export's folder and its
	/// name, a layer suffix at its end swapped for <paramref name="suffix"/> (the intensity exported after the field is
	/// not proposed the field's file); without one, the project's folder and <paramref name="suggested"/>.
	/// </summary>
	/// <param name="suggested">The name of a first export: the project's name with the layer's suffix.</param>
	/// <param name="suffix">This export's layer suffix; "" for both layers.</param>
	public (string Folder, string Name) StartOfRun(string suggested, string suffix) => Start(_run, takeName: true, suggested, suffix);

	/// <summary>Where a still's dialog starts: the last still's folder, else the project's; the name is always <paramref name="suggested"/>, the frame's own.</summary>
	public (string Folder, string Name) StartOfStill(string suggested) => Start(_still, takeName: false, suggested, "");

	/// <summary>Where the dialog for a probe's data starts: the last such export's folder, else the project's; the name is always <paramref name="suggested"/>, the probe's own.</summary>
	public (string Folder, string Name) StartOfProbe(string suggested) => Start(_probe, takeName: false, suggested, "");

	/// <summary>A file was chosen in an export dialog: its folder is where a first export starts when the project has no folder of its own. Not for a path a script handed in.</summary>
	public void ChosenInDialog(string path) => Settings.Default.LastExportFolder = Path.GetDirectoryName(path) ?? "";

	/// <summary>A run's pictures go to <paramref name="path"/>: the next such dialog starts there, Export last repeats this, and the settings keep the path for the project.</summary>
	public void RunExported(bool field, bool intensity, ExportFormat format, string path)
	{
		_run = path;
		LastRun = new RunExport(field, intensity, format, path);
		Remember();
	}

	/// <summary>A still goes to <paramref name="path"/>: the next still's dialog starts in its folder, and the settings keep the path for the project.</summary>
	public void StillExported(string path)
	{
		_still = path;
		Remember();
	}

	/// <summary>A probe's data goes to <paramref name="path"/>: the next such dialog starts in its folder, and the settings keep the path for the project.</summary>
	public void ProbeExported(string path)
	{
		_probe = path;
		Remember();
	}

	/// <summary>
	/// Where an export dialog starts and the name it proposes: the folder of <paramref name="remembered"/>, this session's
	/// last export of the kind, while it exists, else the project's folder, else the settings' last export folder; the name
	/// <paramref name="suggested"/>, or with <paramref name="takeName"/> the remembered file's stem, a layer suffix at its
	/// end swapped for <paramref name="suffix"/>.
	/// </summary>
	private (string Folder, string Name) Start(string? remembered, bool takeName, string suggested, string suffix)
	{
		string? folder = Path.GetDirectoryName(remembered);
		if (folder == null || !Directory.Exists(folder))
		{
			return (document.Folder ?? (Directory.Exists(Settings.Default.LastExportFolder) ? Settings.Default.LastExportFolder : ""), suggested);
		}
		string name = suggested;
		if (takeName)
		{
			name = Path.GetFileNameWithoutExtension(remembered!);
			foreach (string known in LayerSuffixes)
			{
				if (name.EndsWith(known, StringComparison.OrdinalIgnoreCase))
				{
					name = name[..^known.Length] + suffix;
					break;
				}
			}
		}
		Logger.Debug("Export dialog starts where the session's last export went: {Folder}, as {Name}", folder, name);
		return (folder, name);
	}

	/// <summary>
	/// Keeps the export paths for the project file in the user settings, this project first, the others after it up to
	/// <see cref="HintCount"/> and only while their project files exist; a project without a file keeps nothing.
	/// </summary>
	private void Remember()
	{
		if (document.Filename is not { } file)
		{
			return;
		}
		// a fresh list, as the recent files are kept
		var hints = new List<ExportHint> { new() { Project = file, Run = _run ?? "", Still = _still ?? "", Probe = _probe ?? "" } };
		foreach (ExportHint entry in Settings.Default.ExportHints)
		{
			if (!string.Equals(entry.Project, file, StringComparison.OrdinalIgnoreCase) && hints.Count < HintCount && File.Exists(entry.Project))
			{
				hints.Add(entry);
			}
		}
		Settings.Default.ExportHints = hints;
		Settings.Default.TrySave();
	}
}
