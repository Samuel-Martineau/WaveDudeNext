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

using System.Diagnostics.CodeAnalysis;
using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Parts;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.UI;

/// <summary>What <see cref="ProjectDocument.Opened"/> reports: where the thing now open came from.</summary>
public enum OpenedKind
{
	/// <summary>A new, empty project (File > New, File > Close).</summary>
	NewProject,
	/// <summary>A project file: the document has its file name.</summary>
	ProjectFile,
	/// <summary>A project of Wave Dude 2D, imported: unsaved, a file name suggested for saving it.</summary>
	ImportedProject,
	/// <summary>A shipped example: unsaved, its own file name suggested.</summary>
	Example,
	/// <summary>A colour scheme file, added to the project that is open, which stays.</summary>
	ColorScheme,
}

/// <summary>The arguments of <see cref="ProjectDocument.Opened"/>.</summary>
/// <param name="kind">Where it came from.</param>
/// <param name="item">What to show as selected: the project, or the colour scheme added to it.</param>
public sealed class OpenedEventArgs(OpenedKind kind, object item) : EventArgs
{
	/// <summary>Where it came from.</summary>
	public OpenedKind Kind { get; } = kind;

	/// <summary>What to show as selected: the project, or the colour scheme added to it.</summary>
	public object Item { get; } = item;
}

/// <summary>
/// The open project as a document (2026-11-06, the user's design: the application's state and file handling live in a
/// model the window binds, not in the window): the <see cref="Project"/>, the file it was loaded from or saved to, whether
/// it was edited since, and the name an unsaved one came with; and what File > New, Open, Save and the examples do with
/// them, the recent files list included. It watches the open project (<see cref="ProjectWatcher"/>): every edit anywhere
/// in it makes the document dirty and is reported as <see cref="Changed"/>, the one event the rest of the application
/// hangs its rules on; and it keeps the sources on a logic gate's inputs there when the gate is edited
/// (<see cref="GatePortFollower"/>, a rule of editing the project, 2026-11-07, the window's before). It talks to nobody:
/// an operation that can fail returns the error for the caller to show, and what the rest of the application has to do
/// at a certain moment is an event (<see cref="ProjectReplacing"/>, <see cref="ProjectReplaced"/>, <see cref="Opened"/>,
/// <see cref="Saved"/>). Every failure is logged here, an unreadable file with its text
/// (<see cref="UnreadableFileException"/>). UI thread only; a change raises <see cref="ObservableObject.PropertyChanged"/>,
/// which the views bind.
/// </summary>
public sealed class ProjectDocument : ObservableObject
{
	/// <summary>Extension of project files written by <see cref="TrySave"/>.</summary>
	public const string Extension = ".wdproj";

	/// <summary>Files the recent list keeps.</summary>
	private const int RecentFileCount = 10;

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private Project _project = new();
	private ProjectWatcher _watcher;
	private GatePortFollower _follower;
	private string? _filename;
	private string? _suggestedFileName;
	private bool _isDirty;

	/// <summary>An empty project, as the application starts with.</summary>
	public ProjectDocument()
	{
		_watcher = Watch(_project);
		_follower = new GatePortFollower(_project);
	}

	/// <summary>The open project. Never null: an empty project while nothing is loaded.</summary>
	public Project Project => _project;

	/// <summary>Full path the project was loaded from or saved to; null for an unsaved project.</summary>
	public string? Filename
	{
		get => _filename;
		private set
		{
			if (SetProperty(ref _filename, value))
			{
				OnPropertyChanged(nameof(Folder));
			}
		}
	}

	/// <summary>The folder of the project file, or null for an unsaved project.</summary>
	public string? Folder => _filename != null ? Path.GetDirectoryName(_filename) : null;

	/// <summary>The project's name for a file made from it (an export, the scene image written out): the project file's name without its extension, else the suggested one's, else "project".</summary>
	public string Name => Path.GetFileNameWithoutExtension(_filename ?? _suggestedFileName ?? "project");

	/// <summary>
	/// The file name of an unsaved project that came from somewhere, an import or a shipped example: the title shows it
	/// (2026-10-29, the user's request: an opened example read as nothing, then "Untitled*") and Save As proposes it; null
	/// for a new project.
	/// </summary>
	public string? SuggestedFileName
	{
		get => _suggestedFileName;
		private set => SetProperty(ref _suggestedFileName, value);
	}

	/// <summary>
	/// Whether the project was edited since it was opened, created or last saved (2026-10-14): the title carries an
	/// asterisk and New, Open, Close and Exit ask before discarding. Set by every change of the project, and by Undo and
	/// Redo when they reach or leave the saved step; cleared here when the project is replaced or saved.
	/// </summary>
	public bool IsDirty
	{
		get => _isDirty;
		set => SetProperty(ref _isDirty, value);
	}

	/// <summary>
	/// Something in the open project changed (a property of any object in it, or a list), the document dirty already.
	/// Raised for a step Undo or Redo write back as for an edit by hand, since the project is the same object throughout.
	/// </summary>
	public event EventHandler<ProjectChange>? Changed;

	/// <summary>The open project is about to be replaced, and is still <see cref="Project"/>: what depends on it (its run) goes first.</summary>
	public event EventHandler? ProjectReplacing;

	/// <summary>
	/// Another project is <see cref="Project"/> now, clean; its file name follows. Raised before the property's own change
	/// notification, so whoever keeps state per project has reset it when the views bound to the project take the new one.
	/// </summary>
	public event EventHandler? ProjectReplaced;

	/// <summary>Something was opened and is in place, the file name and the suggested name with it: a project of any origin, or a colour scheme added to the open project.</summary>
	public event EventHandler<OpenedEventArgs>? Opened;

	/// <summary>
	/// The project was written to <see cref="Filename"/>. Raised before <see cref="IsDirty"/> is cleared, so the undo
	/// history, which marks its saved step now, has ended a pending step first.
	/// </summary>
	public event EventHandler? Saved;

	/// <summary>A new, empty project without a file replaces the open one (File > New and File > Close).</summary>
	public void New()
	{
		Replace(new Project(), null, null);
		Opened?.Invoke(this, new OpenedEventArgs(OpenedKind.NewProject, _project));
	}

	/// <summary>
	/// Opens the file at <paramref name="fileName"/> by its kind: a project file becomes the open project, a project of
	/// Wave Dude 2D (its <c>configuration.json</c>) is imported as a new, unsaved one, and a colour scheme file is added to
	/// the open project's own schemes. On a failure the open project stays as it is.
	/// </summary>
	/// <param name="fileName">The file; a project file's name is made a full path.</param>
	/// <param name="error">What went wrong, already logged; for the caller to show.</param>
	/// <returns>Whether the file was opened.</returns>
	public bool TryOpen(string fileName, [NotNullWhen(false)] out Exception? error)
	{
		if (string.Equals(Path.GetExtension(fileName), ColorSchemeStore.XmlExtension, StringComparison.OrdinalIgnoreCase))
		{
			return TryAddColorScheme(fileName, out error);
		}
		if (LegacyProject.IsLegacyFile(fileName))
		{
			return TryImport(fileName, out error);
		}
		try
		{
			fileName = Path.GetFullPath(fileName);
			Project loaded = Project.Load(fileName);
			Replace(loaded, fileName, null);
			Logger.Info("Opened '{Path}'", fileName);
			RememberRecent(fileName);
			Opened?.Invoke(this, new OpenedEventArgs(OpenedKind.ProjectFile, loaded));
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			// the file's text goes into the log under the error (2026-11-03, the user's rule); the caller shows the error alone
			Logger.Error(new UnreadableFileException(fileName, ex), "Error while opening '{Path}'", fileName);
			error = ex;
			return false;
		}
	}

	/// <summary>
	/// One of the shipped examples (2026-10-29): the embedded project becomes the open project, unsaved and without a file,
	/// its own file name suggested when it is saved; on a failure the open project stays as it is.
	/// </summary>
	/// <param name="example">The example.</param>
	/// <param name="error">What went wrong, already logged; for the caller to show.</param>
	/// <returns>Whether the example was opened.</returns>
	public bool TryOpenExample(ExampleEntry example, [NotNullWhen(false)] out Exception? error)
	{
		try
		{
			Project loaded;
			using (Stream stream = Examples.Open(example))
			{
				loaded = Project.Load(stream);
			}
			Replace(loaded, null, example.FileName);
			Logger.Info("Opened the example '{Title}' ({Resource})", example.Title, example.Resource);
			Opened?.Invoke(this, new OpenedEventArgs(OpenedKind.Example, loaded));
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while opening the example '{Resource}'", example.Resource);
			error = ex;
			return false;
		}
	}

	/// <summary>
	/// Writes the open project to <paramref name="fileName"/>, which becomes its file; an image file inside that folder is
	/// stored relative to it, so the project moves with its image.
	/// </summary>
	/// <param name="fileName">The file; made a full path.</param>
	/// <param name="error">What went wrong, already logged; for the caller to show.</param>
	/// <returns>Whether the project was saved.</returns>
	public bool TrySave(string fileName, [NotNullWhen(false)] out Exception? error)
	{
		try
		{
			fileName = Path.GetFullPath(fileName);
			_project.Scene.RelativizeImagePath(Path.GetDirectoryName(fileName));
			_project.Save(fileName);
			Filename = fileName;
			Saved?.Invoke(this, EventArgs.Empty);
			IsDirty = false;
			Logger.Info("Saved '{Path}'", fileName);
			RememberRecent(fileName);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while saving '{Path}'", fileName);
			error = ex;
			return false;
		}
	}

	/// <summary>
	/// A project of the author's program (its <c>configuration.json</c>) becomes a new, unsaved project (2026-10-14): the
	/// scene image is embedded, what could not be carried over exactly is logged as a warning, and a <c>.wdproj</c> named
	/// after the project beside the original is suggested for saving it.
	/// </summary>
	private bool TryImport(string fileName, [NotNullWhen(false)] out Exception? error)
	{
		try
		{
			var notes = new List<string>();
			Project imported = LegacyProject.Import(fileName, notes, out string projectName);
			Replace(imported, null, LegacyProject.SuggestedFileName(fileName, projectName));
			foreach (string note in notes)
			{
				Logger.Warn(note);
			}
			Logger.Info("Imported the legacy project '{Name}'; save it as a WaveDude Next project (File > Save As)", projectName.Length > 0 ? projectName : Path.GetFileName(fileName));
			Opened?.Invoke(this, new OpenedEventArgs(OpenedKind.ImportedProject, imported));
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while importing '{Path}'", fileName);
			error = ex;
			return false;
		}
	}

	/// <summary>A scheme file (double-clicked, dropped, or given on the command line) is added to the open project's own schemes.</summary>
	private bool TryAddColorScheme(string fileName, [NotNullWhen(false)] out Exception? error)
	{
		try
		{
			ColorScheme scheme = ColorSchemeStore.LoadFile(fileName);
			scheme.FilePath = "";
			_project.AddColorScheme(scheme);
			Logger.Info("Colour scheme '{Name}' loaded from '{Path}' into the project", scheme.Name, fileName);
			Opened?.Invoke(this, new OpenedEventArgs(OpenedKind.ColorScheme, scheme));
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			// the file's text goes into the log under the error (2026-11-03, the user's rule); the caller shows the error alone
			Logger.Error(new UnreadableFileException(fileName, ex), "Error while opening '{Path}'", fileName);
			error = ex;
			return false;
		}
	}

	/// <summary>Makes <paramref name="project"/> the open one, clean, under these names, raising the events in the order they document.</summary>
	private void Replace(Project project, string? filename, string? suggestedFileName)
	{
		ProjectReplacing?.Invoke(this, EventArgs.Empty);
		_watcher.Dispose();
		_follower.Dispose();
		_project = project;
		_watcher = Watch(project);
		_follower = new GatePortFollower(project);
		IsDirty = false;
		ProjectReplaced?.Invoke(this, EventArgs.Empty);
		OnPropertyChanged(nameof(Project));
		Filename = filename;
		SuggestedFileName = suggestedFileName;
	}

	/// <summary>Watches <paramref name="project"/>: every change makes the document dirty and is raised as <see cref="Changed"/>.</summary>
	private ProjectWatcher Watch(Project project)
	{
		var watcher = new ProjectWatcher(project);
		watcher.Changed += (_, change) =>
		{
			if (Logger.IsTraceEnabled)
			{
				// every change with its value before and after (2026-10-27); Trace, so a Release build pays nothing for it
				Logger.Trace("Changed ({Item}.{Property}){Values}", change.Item, change.Property ?? "list", change.Values == null ? "" : ": " + change.OldValueText + " → " + change.NewValueText);
			}
			IsDirty = true;
			Changed?.Invoke(this, change);
		};
		return watcher;
	}

	/// <summary>Puts the file at the top of the recent list (<see cref="Settings.RecentFiles"/>) and saves the settings.</summary>
	private static void RememberRecent(string fileName)
	{
		// a fresh list so the bound menu sees the change
		var recent = new List<string> { fileName };
		foreach (string entry in Settings.Default.RecentFiles)
		{
			if (!string.Equals(entry, fileName, StringComparison.OrdinalIgnoreCase) && recent.Count < RecentFileCount)
			{
				recent.Add(entry);
			}
		}
		Settings.Default.RecentFiles = recent;
		Settings.Default.TrySave();
	}
}
