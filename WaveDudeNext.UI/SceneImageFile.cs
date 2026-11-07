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
using WaveDudeNext.Model;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// The scene image and its file (a model of this library since 2026-11-06; in the main window before): embedding a
/// picked file, dropping the image, writing the embedded bytes out, and keeping the embedded image in step with the
/// file it came from (the user's rule, 2026-10-15). The file named by <see cref="Scene.ImagePath"/> is watched while the
/// project is open and re-embedded when it has changed on disk, and when a project is opened the file is compared with
/// the embedded bytes once and taken instead when it differs. Either way the change goes through the model, so
/// whoever watches the project sees a scene edit. The embedded bytes are the baseline, so "changed" is a byte
/// comparison; no hash. A file that is gone changes nothing but is reported (<see cref="IsFileMissing"/>, 2026-10-26,
/// Jeroen's report: a deleted file still showed its path, which is right, but nothing said so).
/// <para>
/// <b>The file is read when it has been written and closed, at once, and time has no say in it</b> (2026-11-06, the
/// user's rule; a quiet period of 400 ms after the last file event and a retry loop with pauses had stood here). It
/// is opened in a way that refuses to share it with a writer, which fails for as long as any program has it open for
/// writing; then nothing happens, and the writer's closing the file is the next event. Should an event go missing, the
/// application calls <see cref="CheckFile"/> when its window becomes active again, the moment the user comes back from
/// the image editor: that look acts only on a file that has changed since it was last read here, so an image whose
/// re-embedding the user has undone is not embedded again behind their back. Where the system has no such refusal
/// (Linux, macOS) a file caught half-written does not decode, is left alone, and its completion is the next event.
/// </para>
/// UI thread only; the watcher's events are posted to it.
/// </summary>
/// <param name="document">The open project, and the folder a relative image path is resolved against.</param>
/// <param name="decoder">The application's image decoder, which proves that a file is an image.</param>
/// <param name="uiThread">Posts to the UI thread; the file watcher's events arrive on a thread of its own.</param>
public sealed class SceneImageFile(ProjectDocument document, IImageDecoder decoder, SynchronizationContext uiThread) : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Folders beside the project file where a project without a path may find its image by name: the folder itself and the author's layout.</summary>
	private static readonly string[] Folders = ["", "scene_images"];

	private FileSystemWatcher? _watcher;
	/// <summary>The full path of the file <see cref="_watcher"/> watches.</summary>
	private string? _watchedPath;
	/// <summary>The image file as it was when last read here: its path, time of last writing and length; null before the first read.</summary>
	private (string Path, DateTime Written, long Length)? _seen;
	private bool _isFileMissing;

	private Project Project => document.Project;

	/// <summary>
	/// Whether the scene's image file (<see cref="Scene.ImagePath"/>) is named but not on disk: the scene form says so beside
	/// the path. The embedded image is what is simulated, so nothing else follows. An unsaved project with a relative path
	/// cannot tell and says no. Kept up to date by the watch.
	/// </summary>
	public bool IsFileMissing
	{
		get => _isFileMissing;
		private set => SetProperty(ref _isFileMissing, value);
	}

	/// <summary>
	/// The embedded image was replaced by its changed file, with no doing of the user's in this application: an edit
	/// complete in itself, which the application ends the undo step for.
	/// </summary>
	public event EventHandler? Reembedded;

	/// <summary>Whether there is an image to drop: embedded bytes, a name or a path.</summary>
	public bool CanRemove => Project.Scene is { } scene && (scene.ImageData != null || scene.Image.Length > 0 || scene.ImagePath.Length > 0);

	/// <summary>Whether there are embedded bytes to write out.</summary>
	public bool CanSave => Project.Scene is { ImageData: not null };

	/// <summary>The file extension of the embedded image for a save, with its dot: the image's own, else by the bytes' signature; "" when unknown.</summary>
	public string SaveExtension
	{
		get
		{
			Scene scene = Project.Scene;
			string extension = Path.GetExtension(scene.Image);
			return extension.Length > 0 || scene.ImageData is not { } bytes ? extension : ExtensionOf(bytes);
		}
	}

	/// <summary>The file name a save proposes: the image's name, else the project's with "_scene" and <see cref="SaveExtension"/>.</summary>
	public string SaveFileName => Project.Scene.Image.Length > 0 ? Project.Scene.Image : document.Name + "_scene" + SaveExtension;

	/// <summary>The folder a save starts in: the image file's when it exists, else the project's; "" for neither.</summary>
	public string SaveFolder
	{
		get
		{
			string resolved = Project.Scene.ResolveImagePath(document.Folder);
			string? folder = resolved.Length > 0 ? Path.GetDirectoryName(resolved) : null;
			return folder != null && Directory.Exists(folder) ? folder : document.Folder ?? "";
		}
	}

	/// <summary>
	/// Embeds the image file at <paramref name="fileName"/> in the project (2026-10-13; the file is decoded once to prove
	/// it is an image and to log its size). The scene switches to image mode and shows the file's name. A file that
	/// cannot be read as an image is logged as an error and changes nothing.
	/// </summary>
	/// <returns>Whether the image was embedded.</returns>
	public bool TryEmbed(string fileName)
	{
		try
		{
			byte[] bytes = File.ReadAllBytes(fileName);
			ScenePixels pixels = decoder.Decode(bytes);
			_seen = Stamp(Path.GetFullPath(fileName));
			Scene scene = Project.Scene;
			scene.ImageData = bytes;
			scene.Image = Path.GetFileName(fileName);
			scene.ImagePath = Scene.StoredImagePath(document.Folder, fileName);
			scene.Mode = SceneMode.Image;
			Logger.Info("Scene image '{Name}' embedded: {Width}×{Height}, {Size} KB", scene.Image, pixels.Width, pixels.Height, Math.Max(1, bytes.Length / 1024));
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
		{
			Logger.Error(ex, "The image could not be loaded: {Message}", ex.Message);
			return false;
		}
	}

	/// <summary>
	/// Drops the scene image (2026-10-26, Jeroen's report: there was no way to drop an image): the canvas takes the
	/// image's size, so nothing moves, then the scene drops the name, the path and the embedded bytes and is a blank canvas.
	/// </summary>
	public void Remove()
	{
		Scene scene = Project.Scene;
		if (scene.ImageData != null)
		{
			try
			{
				ScenePixels pixels = decoder.Decode(scene.ImageData);
				scene.Width = pixels.Width;
				scene.Height = pixels.Height;
			}
			catch (Exception ex) when (ex is NotSupportedException or FormatException or IOException)
			{
				Logger.Debug("The embedded image could not be decoded for its size; the canvas keeps {Width}×{Height}: {Message}", scene.Width, scene.Height, ex.Message);
			}
		}
		scene.RemoveImage();
		Logger.Info("Scene image removed: a blank canvas of {Width}×{Height}", scene.Width, scene.Height);
	}

	/// <summary>
	/// Writes the embedded bytes, the file as it was embedded, to <paramref name="fileName"/> (2026-10-29, the user's
	/// design). An export: the project, its path and the watch stay as they are.
	/// </summary>
	/// <param name="fileName">The file to write.</param>
	/// <param name="error">What went wrong, already logged; for the caller to show. Null also when there is nothing to write.</param>
	/// <returns>Whether the file was written.</returns>
	public bool TrySave(string fileName, out Exception? error)
	{
		error = null;
		if (Project.Scene.ImageData is not { } bytes)
		{
			return false;
		}
		try
		{
			File.WriteAllBytes(fileName, bytes);
			Logger.Info("Scene image saved: {Size} KB to {Path}", Math.Max(1, bytes.Length / 1024), fileName);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.Error(ex, "The scene image could not be saved: {Message}", ex.Message);
			error = ex;
			return false;
		}
	}

	/// <summary>
	/// A project was opened or imported: finds the image file of a project that knows it only by name, watches the
	/// file, and takes it at once when it differs from the embedded image (<paramref name="reason"/> for the log).
	/// </summary>
	public void Sync(string reason)
	{
		Locate();
		Watch();
		Reload(reason);
	}

	/// <summary>
	/// Watches the scene image's file as the project names it now (after a project, a path or a file name changed);
	/// nothing when the project has no embedded image or the path does not resolve. Says whether the file is missing.
	/// </summary>
	public void Watch()
	{
		Stop();
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(document.Folder);
		IsFileMissing = Missing();
		if (path.Length == 0 || scene.ImageData == null)
		{
			return;
		}
		string? folder = Path.GetDirectoryName(path);
		if (folder == null || !Directory.Exists(folder))
		{
			return;
		}
		try
		{
			_watchedPath = Path.GetFullPath(path);
			_watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
			};
			_watcher.Changed += OnFileEvent;
			_watcher.Created += OnFileEvent;
			_watcher.Renamed += OnFileEvent;
			_watcher.Deleted += OnFileEvent;
			_watcher.EnableRaisingEvents = true;
			Logger.Debug("Watching the scene image file '{Path}'", path);
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
		{
			Logger.Debug("The scene image file '{Path}' cannot be watched: {Message}", path, ex.Message);
			_watcher = null;
		}
	}

	/// <summary>Ends the watch.</summary>
	public void Stop()
	{
		if (_watcher != null)
		{
			_watcher.EnableRaisingEvents = false;
			_watcher.Dispose();
			_watcher = null;
		}
		_watchedPath = null;
	}

	/// <summary>
	/// Looks at the scene image's file without an event of it, as the application does when its window becomes active
	/// again: re-embeds the file when it has changed since it was last read here and no program has it open for writing,
	/// and says whether it is missing.
	/// </summary>
	public void CheckFile()
	{
		string path = Project.Scene.ResolveImagePath(document.Folder);
		if (path.Length > 0 && Stamp(Path.GetFullPath(path)) is { } stamp && stamp != _seen)
		{
			Reload("found changed on the window's return");
		}
		IsFileMissing = Missing();
	}

	/// <summary>
	/// A file event, on the watcher's thread: the file is looked at on the UI thread, unless no watch on that file is
	/// running by then (the watch stopped, or the project names another file). An event of the same file still counts
	/// after its watch was started anew, which every re-embedding does.
	/// </summary>
	private void OnFileEvent(object sender, FileSystemEventArgs e)
	{
		string? watched = _watchedPath;
		uiThread.Post(_ =>
		{
			if (_watcher != null && watched != null && string.Equals(watched, _watchedPath, StringComparison.OrdinalIgnoreCase))
			{
				Reload("changed on disk");
				IsFileMissing = Missing();
			}
		}, null);
	}

	/// <summary>
	/// A project saved before the path existed knows its image only by name: looks for that name beside the
	/// project file and in a <c>scene_images</c> folder next to it (the author's layout, where an imported
	/// project is usually saved) and adopts the first match as the path, so the file is watched and compared
	/// from now on. Nothing happens without an embedded image, a name, or a project folder.
	/// </summary>
	private void Locate()
	{
		Scene scene = Project.Scene;
		if (scene.ImagePath.Length > 0 || scene.Image.Length == 0 || scene.ImageData == null || document.Folder is not { } folder)
		{
			return;
		}
		foreach (string sub in Folders)
		{
			string candidate = Path.Combine(folder, sub, scene.Image);
			if (File.Exists(candidate))
			{
				scene.ImagePath = Scene.StoredImagePath(folder, candidate);
				Logger.Info("Scene image file found for '{Name}': '{Path}' (the project had no path; it is watched from now on)", scene.Image, scene.ImagePath);
				return;
			}
		}
	}

	/// <summary>Whether the scene names an image file that is not on disk (see <see cref="IsFileMissing"/>).</summary>
	private bool Missing()
	{
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(document.Folder);
		return path.Length > 0 && scene.ImageData != null && !File.Exists(path);
	}

	/// <summary>
	/// Reads the scene image's file, when no program has it open for writing, and embeds it when its bytes differ from
	/// the embedded ones; a file that is not an image is reported and left alone. True when the image was replaced.
	/// </summary>
	private bool Reload(string reason)
	{
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(document.Folder);
		if (path.Length == 0 || scene.ImageData == null || !File.Exists(path))
		{
			return false;
		}
		// the file's state as it is read: taken first, so a change during the read shows as one afterwards
		(string Path, DateTime Written, long Length)? stamp = Stamp(Path.GetFullPath(path));
		byte[]? bytes = TryReadClosed(path);
		if (bytes == null)
		{
			return false;
		}
		_seen = stamp;
		if (bytes.AsSpan().SequenceEqual(scene.ImageData))
		{
			return false;
		}
		try
		{
			ScenePixels pixels = decoder.Decode(bytes);
			scene.ImageData = bytes;
			scene.Image = Path.GetFileName(path);
			Logger.Info("Scene image '{Name}' re-embedded from '{Path}' ({Reason}): {Width}×{Height}, {Size} KB", scene.Image, path, reason, pixels.Width, pixels.Height, Math.Max(1, bytes.Length / 1024));
			Reembedded?.Invoke(this, EventArgs.Empty);
			return true;
		}
		catch (Exception ex) when (ex is NotSupportedException or FormatException or IOException)
		{
			Logger.Warn(ex, "The scene image file '{Path}' changed but cannot be read as an image: {Message}", path, ex.Message);
			return false;
		}
	}

	/// <summary>
	/// The file's bytes when no program has it open for writing; null while one has, and when the file cannot be read.
	/// Opened for reading with other readers let in and writers refused: for as long as a program holds the file open for
	/// writing the system refuses that, either because this open will not share with a writer or because the writer does
	/// not share with readers. Nothing waits and nothing is tried again: the writer's closing the file is the next event.
	/// </summary>
	private static byte[]? TryReadClosed(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			byte[] bytes = new byte[stream.Length];
			stream.ReadExactly(bytes);
			return bytes;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.Debug("The scene image file '{Path}' is not read now ({Message}): its next change, or the window's return, looks again", path, ex.Message);
			return null;
		}
	}

	/// <summary>A file's path, time of last writing and length; null when it is not there.</summary>
	private static (string Path, DateTime Written, long Length)? Stamp(string fullPath)
	{
		var info = new FileInfo(fullPath);
		return info.Exists ? (fullPath, info.LastWriteTimeUtc, info.Length) : null;
	}

	/// <summary>The file extension of an image's bytes by their signature: PNG, JPEG, BMP, GIF or TIFF; "" when unknown.</summary>
	private static string ExtensionOf(byte[] bytes) => bytes switch
	{
		[0x89, 0x50, 0x4E, 0x47, ..] => ".png",
		[0xFF, 0xD8, 0xFF, ..] => ".jpg",
		[0x42, 0x4D, ..] => ".bmp",
		[0x47, 0x49, 0x46, 0x38, ..] => ".gif",
		[0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..] => ".tif",
		_ => "",
	};
}
