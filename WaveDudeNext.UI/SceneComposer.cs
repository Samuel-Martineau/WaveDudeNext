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
using WaveDudeNext.Parts;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// The scene as pixels (a model of this library since 2026-11-06; in the main window before): the decoded scene image
/// with the enabled optical parts painted in, or the parts on a blank canvas. <see cref="Pixels"/> is what the preview
/// shows under its pictures, kept current by <see cref="Update"/>; <see cref="Load"/> is what a run, a scratch frame and
/// an export are made from. Both draw on the same caches, so an unchanged scene is the same reference, which
/// <see cref="RunSpec.SameSceneAs"/> compares and the scratch runner keeps its engine by. The application tells it what
/// happens to the project (<see cref="Reset"/>, <see cref="PartsChanged"/>, <see cref="Update"/>). UI thread only; the
/// painting runs on a worker.
/// </summary>
/// <param name="document">The open project, and the folder a relative image path is resolved against.</param>
/// <param name="decoder">The application's image decoder.</param>
/// <param name="idle">
/// Posts to the UI thread for when it has nothing more urgent to do (WPF: the dispatcher at background priority): an
/// update waits there for the hand to rest, so the many changes of one drag step make one composition.
/// </param>
public sealed class SceneComposer(ProjectDocument document, IImageDecoder decoder, SynchronizationContext idle) : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private (string Path, DateTime Written, ScenePixels Pixels)? _imageCache;
	private (byte[] Data, ScenePixels Pixels)? _embeddedCache;
	private (ScenePixels? Source, int PartsVersion, int Width, int Height, double Background, ScenePixels? Composed)? _composedCache;
	/// <summary>Bumped on every optical part change, so the scene is painted again only then.</summary>
	private int _partsVersion;
	private bool _updatePending;
	/// <summary>The composition on a worker with the key its result is cached under, or null when none is in flight.</summary>
	private (SceneKey Key, Task<ScenePixels?> Task)? _composition;
	/// <summary>Set by a request that arrives while a composition is in flight: another one follows it.</summary>
	private bool _composeAgain;
	private ScenePixels? _pixels;

	private Project Project => document.Project;

	/// <summary>
	/// The scene pixels for the preview: null for a canvas without parts, and when the image cannot be read (that is
	/// logged once a run reports it). Follows <see cref="Update"/>, a moment later.
	/// </summary>
	public ScenePixels? Pixels
	{
		get => _pixels;
		private set => SetProperty(ref _pixels, value);
	}

	/// <summary>A composition asked for by <see cref="Update"/> finished, <see cref="Pixels"/> set, changed or not (also when the image could not be read); what waits for the scene goes on.</summary>
	public event EventHandler? Composed;

	/// <summary>Whether a composition is pending or in flight (<see cref="Update"/> was called and <see cref="Composed"/> has not followed yet): <see cref="Load"/> would wait for it on the UI thread, so what can wait waits for the event.</summary>
	public bool IsComposing => _updatePending || _composition != null || _composeAgain;

	/// <summary>Another project is open: nothing decoded or composed for the old one applies.</summary>
	public void Reset()
	{
		_imageCache = null;
		_embeddedCache = null;
		_composedCache = null;
		// a new set of parts: a composition of the old project still on its worker must not land under the new one's key
		_partsVersion++;
	}

	/// <summary>An optical part, one of its points or the parts list changed: the parts are painted into the scene pixels, so they are painted again.</summary>
	public void PartsChanged() => _partsVersion++;

	/// <summary>
	/// Brings <see cref="Pixels"/> up to date. Coalesced and composed on a worker (2026-11-03, the user's choice): a drag
	/// of a part fires a change per mouse move, and composing the scene took 115–270 ms of the UI thread for Example 16's
	/// four rectangles, so the handle jumped; now one composition is in flight at a time, the newest request follows it,
	/// and the UI thread only takes the finished pixels (20–30 ms for the pictures). The overlay draws the selected part's
	/// outline itself, so what the user drags stays live meanwhile.
	/// </summary>
	public void Update()
	{
		if (_updatePending)
		{
			return;
		}
		_updatePending = true;
		idle.Post(_ =>
		{
			_updatePending = false;
			Compose();
		}, null);
	}

	/// <summary>
	/// The scene as the engines, the outline and the export see it: the decoded image (decoded once per file version)
	/// with the enabled optical parts painted in, or the parts on a blank canvas; null for a canvas without parts.
	/// Composed once per (image, parts version, canvas size, background index): an unchanged scene returns the same
	/// reference. A worker painting these very inputs (<see cref="Update"/>) is waited for rather than painting them again.
	/// </summary>
	/// <exception cref="RunSpecException">An image scene names no image, or a relative one in a project that has no file yet.</exception>
	/// <exception cref="IOException">The image file cannot be read.</exception>
	/// <exception cref="NotSupportedException">The image is none the decoder reads.</exception>
	public ScenePixels? Load()
	{
		ScenePixels? source = Decode();
		Scene scene = Project.Scene;
		var key = new SceneKey(source, _partsVersion, scene.Width, scene.Height, scene.RefractiveIndex);
		if (TryCached(key, out ScenePixels? composed))
		{
			return composed;
		}
		composed = _composition is { } composing && composing.Key == key
			? composing.Task.GetAwaiter().GetResult()
			: PartPainter.Paint(source, scene.Width, scene.Height, scene.RefractiveIndex, Project.Parts);
		_composedCache = (source, _partsVersion, scene.Width, scene.Height, scene.RefractiveIndex, composed);
		return composed;
	}

	/// <summary>
	/// Composes the scene on a worker from a snapshot of its inputs (the parts cloned: <see cref="OpticalPart.Clone"/>
	/// copies the point and node lists too, so the worker shares nothing with the model) and takes the result on the UI
	/// thread, again while requests arrived meanwhile. A failure of the painter surfaces here like any other.
	/// </summary>
	private async void Compose()
	{
		if (_composition != null)
		{
			_composeAgain = true;
			return;
		}
		do
		{
			_composeAgain = false;
			ScenePixels? source;
			try
			{
				source = Decode();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or RunSpecException)
			{
				Logger.Debug("Scene image not shown: {Message}", ex.Message);
				Pixels = null;
				Composed?.Invoke(this, EventArgs.Empty);
				return;
			}
			Scene scene = Project.Scene;
			var key = new SceneKey(source, _partsVersion, scene.Width, scene.Height, scene.RefractiveIndex);
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			if (!TryCached(key, out ScenePixels? composed))
			{
				List<OpticalPart> parts = Project.Parts.Select(part => part.Clone()).ToList();
				// the pool's state, for a report whose compositions take longer than the painter alone (2026-11-03)
				Logger.Debug("Scene composition starts: thread pool {Threads} threads, {Pending} items pending", ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
				Task<ScenePixels?> task = Task.Run(() => PartPainter.Paint(key.Source, key.Width, key.Height, key.Background, parts));
				_composition = (key, task);
				try
				{
					composed = await task;
				}
				finally
				{
					_composition = null;
				}
				_composedCache = (key.Source, key.PartsVersion, key.Width, key.Height, key.Background, composed);
			}
			long painted = System.Diagnostics.Stopwatch.GetTimestamp();
			Pixels = composed;
			Logger.Debug("Scene pixels updated: composed on a worker in {Paint:0.0} ms, pictures in {Pictures:0.0} ms", System.Diagnostics.Stopwatch.GetElapsedTime(started, painted).TotalMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(painted).TotalMilliseconds);
			Composed?.Invoke(this, EventArgs.Empty);
		}
		while (_composeAgain);
	}

	/// <summary>What a composed scene depends on: the decoded image (by reference), the parts' version, the canvas and the background index.</summary>
	private readonly record struct SceneKey(ScenePixels? Source, int PartsVersion, int Width, int Height, double Background);

	/// <summary>The composed scene cached for these inputs, when it is the one.</summary>
	private bool TryCached(SceneKey key, out ScenePixels? composed)
	{
		if (_composedCache is { } cached && ReferenceEquals(cached.Source, key.Source) && cached.PartsVersion == key.PartsVersion
			&& cached.Width == key.Width && cached.Height == key.Height && cached.Background == key.Background)
		{
			composed = cached.Composed;
			return true;
		}
		composed = null;
		return false;
	}

	/// <summary>The decoded scene image for an image scene, decoded once per file version; null for a blank canvas.</summary>
	private ScenePixels? Decode()
	{
		if (Project.Scene.Mode != SceneMode.Image)
		{
			return null;
		}
		if (Project.Scene.ImageData is { } data)
		{
			// embedded: decoded once per byte array (a new pick is a new array)
			if (_embeddedCache is { } embedded && ReferenceEquals(embedded.Data, data))
			{
				return embedded.Pixels;
			}
			ScenePixels decoded = decoder.Decode(data);
			_embeddedCache = (data, decoded);
			return decoded;
		}
		string path = ResolvePath();
		DateTime written = File.GetLastWriteTimeUtc(path);
		if (_imageCache is { } cached && cached.Path == path && cached.Written == written)
		{
			return cached.Pixels;
		}
		ScenePixels pixels = decoder.Decode(File.ReadAllBytes(path));
		_imageCache = (path, written, pixels);
		return pixels;
	}

	/// <summary>The scene image's full path: as given when rooted, otherwise relative to the project file.</summary>
	/// <exception cref="RunSpecException">No image is named, or the path is relative and the project has not been saved.</exception>
	private string ResolvePath()
	{
		string image = Project.Scene.Image;
		if (image.Length == 0)
		{
			throw new RunSpecException("The scene uses an image, but no image file is set.", "RunErrorNoImage");
		}
		if (Path.IsPathRooted(image))
		{
			return image;
		}
		if (document.Folder is not { } folder)
		{
			throw new RunSpecException("Save the project first, so the scene image can be found relative to it.", "RunErrorUnsavedImagePath");
		}
		return Path.GetFullPath(image, folder);
	}
}
