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

using System.ComponentModel;
using NLog;
using WaveDudeNext.Engine;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// The current run and its frame store (a model of this library since 2026-11-07; the main window's own before). A run
/// snapshots the project when it starts (<see cref="StartAsync"/>: the scene composed, the RAM budget, the memory warning
/// when the frames do not fit, the store, the worker); editing the project meanwhile is allowed and does not affect
/// it, but marks it stale (<see cref="IsRunStale"/>, <see cref="IsProbesStale"/>, set by the live-editing rules of
/// <see cref="LivePreview"/>). The store lives until the run is replaced, the project is replaced or the application
/// closes (<see cref="Discard"/>, <see cref="Shutdown"/>). The preview and the status bar bind <see cref="Run"/> and the
/// flags. UI thread only.
/// </summary>
public sealed class RunHost : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private readonly ProjectDocument _document;
	private readonly SceneComposer _scene;
	private readonly IMemoryWarning _warning;
	private readonly SynchronizationContext _progress;
	private SimulationRun? _run;
	private bool _isRunStale;
	private bool _isProbesStale;
	private bool _isStarting;

	/// <param name="document">The open project, snapshotted by every run.</param>
	/// <param name="scene">The scene pixels a run takes (<see cref="SceneComposer.Load"/>).</param>
	/// <param name="warning">Asks the user before a preview run whose frames do not fit in the RAM cache.</param>
	/// <param name="progress">
	/// The UI thread for a run's progress and final state, at the priority the application wants them (WPF: Loaded,
	/// 2026-11-04, the lowest the dispatcher serves ahead of queued input, so a mouse reporting a thousand times a
	/// second does not starve the heartbeat; see the application's notes).
	/// </param>
	public RunHost(ProjectDocument document, SceneComposer scene, IMemoryWarning warning, SynchronizationContext progress)
	{
		_document = document;
		_scene = scene;
		_warning = warning;
		_progress = progress;
	}

	/// <summary>The current run's state changed (started, paused, resumed, stopped, completed, failed); the run itself announces the details.</summary>
	public event EventHandler? RunStateChanged;

	/// <summary>The current run ended, however: completed, stopped or failed. Edits made meanwhile are looked at again then (<see cref="LivePreview"/>).</summary>
	public event EventHandler? RunFinished;

	/// <summary>The current run (active or finished, its frames in the preview), or null.</summary>
	public SimulationRun? Run
	{
		get => _run;
		private set => SetProperty(ref _run, value);
	}

	/// <summary>Whether a run is in progress.</summary>
	public bool IsActive => _run?.IsActive == true;

	/// <summary>
	/// Whether a start waits for the user's answer to the memory warning, from the question to the run's start or the
	/// refusal (2026-11-09). The application is live meanwhile, under a blocking modal window's message loop as much
	/// as under a window that answers later, and no other start is taken (<see cref="StartAsync"/> answers false): the
	/// automatic re-run that a scratch frame's arrival would begin does not ask a second time over the first.
	/// </summary>
	public bool IsStarting
	{
		get => _isStarting;
		private set => SetProperty(ref _isStarting, value);
	}

	/// <summary>Whether the project was edited in a way that changes the simulation since the current run started.</summary>
	public bool IsRunStale
	{
		get => _isRunStale;
		set => SetProperty(ref _isRunStale, value);
	}

	/// <summary>
	/// Whether a probe was edited (measurement, position, list) since the current run started: its frames are
	/// still right, its recordings are not. The graphs then draw from the scratch frame's recording, and Play
	/// or F5 starts a run that refreshes them; no run is started by the edit itself (the user's rule,
	/// 2026-10-14: a probe never changes the picture, and a drag must not launch a simulation).
	/// </summary>
	public bool IsProbesStale
	{
		get => _isProbesStale;
		set => SetProperty(ref _isProbesStale, value);
	}

	/// <summary>
	/// The preview quality chosen in the View menu (persisted user settings, 2026-10-15): how much of a run is
	/// captured and kept, for weaker machines. Applied to the preview's runs and scratch frames; an export's run
	/// is always <see cref="PreviewQuality.Full"/>.
	/// </summary>
	public static PreviewQuality PreviewQualitySetting => new(
		Math.Max(1, Settings.Default.PreviewResolution),
		Math.Max(1, Settings.Default.PreviewFrames),
		Settings.Default.PreviewSampleBytes == 1 ? FrameFormat.Index : FrameFormat.Half);

	/// <summary>
	/// Composes the scene, snapshots the project, asks when the frames do not fit in the RAM cache, checks the disk,
	/// creates the store and starts the worker. The task is finished when the call returns unless the memory warning was
	/// asked and the application's window answers later (<see cref="IMemoryWarning"/>); it then completes on the UI
	/// thread, <see cref="IsStarting"/> being true until it does.
	/// </summary>
	/// <param name="automatic">Started after an edit rather than by the user: the preview keeps its position.</param>
	/// <param name="trackIntensity">Store the intensity plane whatever the project needs (an export of the intensity asks for it).</param>
	/// <param name="fullQuality">Capture every cell of every frame as a half whatever the View menu says (an export needs that).</param>
	/// <param name="intensityRoot">With <paramref name="fullQuality"/>: store the intensity as its square root, the preview's, or as I (an OpenEXR export of I).</param>
	/// <returns>Whether a run was started: false when the memory warning was cancelled, or when another start still waits for its answer.</returns>
	/// <exception cref="RunSpecException">The project cannot run as it is (no scene image, for one); carried by the task, as the others are.</exception>
	/// <exception cref="IOException">The cache drive has no room for the frames that do not fit in memory, or its folder cannot be made.</exception>
	public async Task<bool> StartAsync(bool automatic = false, bool trackIntensity = false, bool fullQuality = false, bool intensityRoot = true)
	{
		if (_isStarting)
		{
			Logger.Debug("{Kind} not started: another start waits for the memory warning's answer", automatic ? "Re-run" : "Run");
			return false;
		}
		if (!Settings.Default.UseGpu)
		{
			Logger.Info("Running on the CPU: 'Use the GPU' is off in the Simulation menu");
		}
		PreviewQuality quality = fullQuality ? PreviewQuality.Full with { IntensityRoot = intensityRoot } : PreviewQualitySetting;
		RunSpec spec = RunSpec.Create(_document.Project, _scene.Load(), Settings.Default.UseGpu, trackIntensity, quality);
		// the frames stay in memory up to the budget (the user's share of the RAM, capped by what is free now); only
		// the rest needs room on the cache drive (2026-10-14). The outgoing run's frames count as free: Discard
		// below gives them back, and the new store allocates its memory tier only once they are gone (2026-10-16)
		long budget = FrameMemory.Budget(Settings.Default.FrameMemoryPercent, _run?.Store.RamBytes ?? 0);
		int ramFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, budget));
		// a preview run that does not fit asks first (the memory warning, 2026-11-03, the user's design: F5, Play and the
		// automatic re-run alike, so no spill is silent; an export's run must be full quality and never asks). The choice
		// lands in the View menu settings, so the scratch frames match the run and later runs fit without asking; the
		// settings' change marks the old run stale and schedules a scratch frame and a re-run, as the View menu's does, and
		// the run started below makes that re-run find nothing stale
		if (ramFrames < spec.StoredFrameCount && !fullQuality && Settings.Default.WarnWhenRamInsufficient)
		{
			// the budget with the RAM cache size at Auto, the warning's fourth lever: the budget itself when it is Auto already
			long autoBudget = Settings.Default.FrameMemoryPercent > FrameMemory.Automatic ? FrameMemory.Budget(FrameMemory.Automatic, _run?.Store.RamBytes ?? 0) : budget;
			IsStarting = true;
			try
			{
				MemoryWarningChoice? choice = await _warning.AskAsync(spec, budget, autoBudget, quality);
				if (choice == null)
				{
					Logger.Info("{Kind} not started: the memory warning was cancelled", automatic ? "Re-run" : "Run");
					return false;
				}
				if (choice.Mute)
				{
					Settings.Default.WarnWhenRamInsufficient = false;
				}
				if (choice.CacheAuto)
				{
					Settings.Default.FrameMemoryPercent = FrameMemory.Automatic;
				}
				if (choice.Quality != quality)
				{
					Settings.Default.SetPreviewQuality(choice.Quality);
					quality = PreviewQualitySetting;
				}
				// snapshotted again, whatever the choice (2026-11-09; only for another quality before): the application was
				// live while the question was open, so an edit that came without input (the scene image re-embedded from
				// its file) is in the run, which is marked fresh below
				spec = RunSpec.Create(_document.Project, _scene.Load(), Settings.Default.UseGpu, trackIntensity, quality);
				budget = FrameMemory.Budget(Settings.Default.FrameMemoryPercent, _run?.Store.RamBytes ?? 0);
				ramFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, budget));
				Settings.Default.TrySave();
				Begin(spec, budget, ramFrames, automatic);
				return true;
			}
			finally
			{
				IsStarting = false;
			}
		}
		Begin(spec, budget, ramFrames, automatic);
		return true;
	}

	/// <summary>The disk check, the store and the worker for <paramref name="spec"/>; the outgoing run goes first.</summary>
	/// <param name="spec">The run to make.</param>
	/// <param name="budget">The RAM cache's budget in bytes.</param>
	/// <param name="ramFrames">How many of the run's frames fit in <paramref name="budget"/>.</param>
	/// <param name="automatic">Started after an edit rather than by the user.</param>
	private void Begin(RunSpec spec, long budget, int ramFrames, bool automatic)
	{
		if (ramFrames < spec.StoredFrameCount)
		{
			FrameCache.CheckSpace(FrameStore.SizeOf(spec.Shape, spec.Layers, spec.StoredFrameCount - ramFrames));
		}
		Task memoryReleased = Discard();
		FrameStore store = FrameStore.Create(FrameCache.NewPath(), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, budget, memoryReleased);
		var run = new SimulationRun(spec, store, _progress, automatic);
		run.PropertyChanged += OnRunPropertyChanged;
		run.Finished += OnRunFinished;
		Logger.Info("{Kind} started: {Steps} steps, {Stored} of {Frames} frames stored as {Width}×{Height} ({Quality}), {RamFrames} in memory ({RamBytes:0.0} GB) and {DiskFrames} on disk; edits made from now on do not affect it",
			automatic ? "Re-run" : "Run", spec.Steps, spec.StoredFrameCount, spec.FrameCount, spec.Shape.Width, spec.Shape.Height, spec.Quality, store.RamFrames, FrameMemory.ToGigabytes(store.RamBytes), spec.StoredFrameCount - store.RamFrames);
		IsRunStale = false;
		IsProbesStale = false;
		Run = run;
		run.Start();
	}

	/// <summary>Stops the run in progress, if any; its frames stay.</summary>
	public void Stop() => _run?.Stop();

	/// <summary>Pauses the run in progress, or resumes a paused one.</summary>
	public void TogglePause() => _run?.TogglePause();

	/// <summary>
	/// Drops the current run: stops it and disposes its store (deleting the cache file) once the worker has let go of
	/// it. Called before a new run, when the project is replaced and on closing. An export reading the store is the
	/// application's to end first.
	/// </summary>
	/// <returns>Completes when the old store is disposed and its memory given back (at once when there was no run); the next run's store waits for it before allocating its memory tier.</returns>
	public Task Discard()
	{
		SimulationRun? old = _run;
		if (old != null)
		{
			Logger.Debug("Run discarded: {State}, {Frames} frames, cache {Path}", old.State, old.FrameCount, old.Store.Path);
		}
		// the run goes first: with it gone the preview shows the scratch frame; clearing the stale flag before
		// would make it pick the old store for a moment (and wait on its file being deleted)
		Run = null;
		IsRunStale = false;
		IsProbesStale = false;
		if (old == null)
		{
			return Task.CompletedTask;
		}
		old.Stop();
		// on the thread pool: unmapping a run's cache file is quick, but never the UI thread's wait
		return old.Completion.ContinueWith(_ =>
		{
			old.Store.Dispose();
			old.Dispose();
		}, TaskScheduler.Default);
	}

	/// <summary>Synchronous variant of <see cref="Discard"/> for closing: waits (briefly) for the worker so the file is closed before the process ends.</summary>
	public void Shutdown()
	{
		SimulationRun? old = _run;
		if (old == null)
		{
			return;
		}
		Run = null;
		old.Stop();
		if (!old.Completion.Wait(TimeSpan.FromSeconds(5)))
		{
			Logger.Warn("The run did not stop in time; its cache file is deleted by the system when the process ends");
		}
		// not awaited: the process exit closes the handles and deletes the file in any case
		Task.Run(() =>
		{
			old.Store.Dispose();
			old.Dispose();
		});
	}

	private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SimulationRun.State) && ReferenceEquals(sender, _run))
		{
			OnPropertyChanged(nameof(IsActive));
			RunStateChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void OnRunFinished(object? sender, EventArgs e)
	{
		if (ReferenceEquals(sender, _run))
		{
			RunFinished?.Invoke(this, EventArgs.Empty);
		}
	}
}
