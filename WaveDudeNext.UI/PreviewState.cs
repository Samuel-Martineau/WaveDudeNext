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

using WaveDudeNext.Model;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>The frames the preview's pictures show and the index of the one shown: the scratch frame, index 0, or the run's frame at the position.</summary>
/// <param name="Source">The frames.</param>
/// <param name="Frame">The index of the frame shown in <paramref name="Source"/>.</param>
public sealed record ShownFrame(IFrameSource Source, int Frame);

/// <summary>
/// The preview as the application sees it (a model of this library since 2026-11-07; the main window had reached into
/// the preview pane for each of these before): the position on the timeline and whether it plays, which the pane and
/// the application both move; what the pictures show and the canvas they show, which the pane tells and the
/// application reads (the brightness fit, where a new item lands); and a count of the colour changes the pane follows.
/// The pane binds everything here in the XAML. The pane's playback clock is its own: the application only turns it
/// off. UI thread only.
/// </summary>
public sealed class PreviewState : ObservableObject
{
	private int _position;
	private bool _isPlaying;
	private bool _isPlaybackHeld;
	private ShownFrame? _shownFrame;
	private int _canvasWidth = 1;
	private int _canvasHeight = 1;
	private int _colorsVersion;

	/// <summary>The frame at the preview's position on the timeline, 0 up; moved by the scrubber, the keys and playback, and by the application for a new project.</summary>
	public int Position
	{
		get => _position;
		set => SetProperty(ref _position, value);
	}

	/// <summary>Whether the preview plays: the pane's clock moves <see cref="Position"/>. The user starts and stops it; an edit or a run's start stops it (the application's rules).</summary>
	public bool IsPlaying
	{
		get => _isPlaying;
		set => SetProperty(ref _isPlaying, value);
	}

	/// <summary>
	/// Whether playback, though on, is held (2026-11-09): the run Play asked for is still being decided, the memory
	/// warning open (<see cref="LivePreview.EnsureRunAsync"/>). The pane's clock runs while <see cref="IsPlaying"/> is
	/// true and this is false, so it does not tick through the stale frames under the question (2026-11-03); the
	/// application releases the hold after it has turned playback off for a refused run, never before.
	/// </summary>
	public bool IsPlaybackHeld
	{
		get => _isPlaybackHeld;
		set => SetProperty(ref _isPlaybackHeld, value);
	}

	/// <summary>The frames the pictures show and the one shown, told by the pane; null while nothing is shown. The brightness fit reads it.</summary>
	public ShownFrame? ShownFrame
	{
		get => _shownFrame;
		set => SetProperty(ref _shownFrame, value);
	}

	/// <summary>Width in pixels of the canvas the pictures show, told by the pane: the frames' while there are any, else the project's; 1 before the pane said.</summary>
	public int CanvasWidth
	{
		get => _canvasWidth;
		set => SetProperty(ref _canvasWidth, value);
	}

	/// <summary>Height in pixels of the canvas the pictures show (see <see cref="CanvasWidth"/>).</summary>
	public int CanvasHeight
	{
		get => _canvasHeight;
		set => SetProperty(ref _canvasHeight, value);
	}

	/// <summary>
	/// Counts the calls of <see cref="RefreshColors"/>: the pane rebuilds its palettes from the project's colour schemes
	/// and redraws when it changes. A number rather than an event, so the pane binds it like the rest.
	/// </summary>
	public int ColorsVersion
	{
		get => _colorsVersion;
		private set => SetProperty(ref _colorsVersion, value);
	}

	/// <summary>A project colour scheme was edited, added or removed: the pictures are recoloured from the schemes as they are now.</summary>
	public void RefreshColors() => ColorsVersion++;
}
