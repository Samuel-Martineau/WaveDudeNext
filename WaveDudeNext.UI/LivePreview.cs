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

using System.Collections.ObjectModel;
using System.ComponentModel;
using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Parts;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// Keeps the preview current after edits (a model of this library since 2026-11-07; the main window's own before). A
/// simulation-affecting edit marks the run stale (<see cref="RunHost.IsRunStale"/>), stops playback and any run in
/// progress (its snapshot is obsolete), asks the <see cref="ScratchRunner"/> for the frame the preview shows, and, when
/// <see cref="Settings.AutoRerun"/> is on, redoes the stored run once the edit has finished. A probe edit never starts
/// a run: only the recordings are stale, the scratch frame refreshes the graph; except that an active probe the run
/// does not record is a simulation edit (2026-10-18). Scrubbing asks for scratch frames whenever the store cannot show
/// the position: no run, a stale run, or a run that has not got there yet. The scratch runner has its own engine, and
/// any run in progress is paused while a scratch frame computes, so the frame the user looks at always comes first (the
/// user's rule, 2026-10-14).
/// <para>
/// Nothing here keeps time (2026-11-07, the user's choice, replacing a 120 ms debounce of the requests and a quiet period
/// of 1.5 s before the re-run): every change asks for its frame at once, and the runner, which finishes the frame in
/// flight and then takes the newest request (2026-11-09), does the coalescing, so a drag shows frames at the rate the
/// engine delivers them; a part or scene edit asks when the scene is composed (<see cref="SceneComposer.Composed"/>), never
/// waiting for the worker on the UI thread. The re-run comes when the edit has finished, the application's own notion
/// of it (<see cref="EditFinished"/>, the one the undo history takes: a mouse button released, the focus leaving, Enter,
/// a command), held back by a frame still computing, a run still active or an export in progress until each of them is
/// over, which each announces. The application's edit hub hands every change to <see cref="Edited"/>; the preview binds
/// <see cref="ScratchFrame"/>. UI thread only.
/// </para>
/// </summary>
public sealed class LivePreview : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private readonly ProjectDocument _document;
	private readonly RunHost _runs;
	private readonly PreviewState _preview;
	private readonly BusyState _busy;
	private readonly SceneComposer _scene;
	private readonly SynchronizationContext _uiThread;
	private ScratchRunner? _scratch;
	private SimulationRun? _pausedForScratch;
	private ScratchResult? _scratchFrame;

	/// <summary>The newest request made and what it is for: the frame's reason and the edit that led to it, logged when its result shows; null while none is outstanding.</summary>
	private (RunSpec Spec, int Frame, string Why)? _scratchRequested;

	/// <summary>A frame was asked for while the scene was being composed: it is asked for when the composition is done.</summary>
	private bool _requestWhenComposed;

	/// <summary>An edit began (a change arrived) and has not finished yet: no re-run meanwhile.</summary>
	private bool _editInProgress;

	/// <summary>The newest simulation or probe edit, named with its value on the next frame's log line (2026-10-27); null once named.</summary>
	private ProjectChange? _lastChange;

	/// <param name="document">The open project.</param>
	/// <param name="runs">The current run, its flags, and the automatic re-runs started here.</param>
	/// <param name="preview">The position the scratch frames are for; playback, stopped by a simulation edit.</param>
	/// <param name="busy">Told the frame being computed; asked whether an export is in progress, which holds the re-run back.</param>
	/// <param name="scene">The scene pixels a scratch frame takes, repainted after a part edit; a frame waits for its composition.</param>
	/// <param name="uiThread">The UI thread for the scratch runner's results, at normal priority.</param>
	public LivePreview(ProjectDocument document, RunHost runs, PreviewState preview, BusyState busy, SceneComposer scene, SynchronizationContext uiThread)
	{
		_document = document;
		_runs = runs;
		_preview = preview;
		_busy = busy;
		_scene = scene;
		_uiThread = uiThread;
		runs.RunFinished += (_, _) => RunFinished();
		preview.PropertyChanged += OnPreviewChanged;
		scene.Composed += (_, _) => OnSceneComposed();
	}

	/// <summary>The newest scratch frame (or skip notice) for the preview's position; null when none applies.</summary>
	public ScratchResult? ScratchFrame
	{
		get => _scratchFrame;
		private set => SetProperty(ref _scratchFrame, value);
	}

	private Project Project => _document.Project;

	/// <summary>Whether the store cannot show the preview's position, or its recordings are stale: no run, a stale one, stale probes, or a run that has not reached it.</summary>
	private bool NeedsScratch => _runs.Run == null || _runs.IsRunStale || _runs.IsProbesStale || _runs.Run.FrameCount <= _preview.Position;

	/// <summary>Whether a scratch frame is on its way: asked for, waiting for the scene, or computing.</summary>
	private bool ScratchPending => _requestWhenComposed || _scratchRequested != null || _scratch?.IsBusy == true;

	/// <summary>
	/// A project edit, after the application has told the document, the undo history and the scene image about it:
	/// what it means for the run, the preview and the scene. A colour scheme edit means nothing here; a probe edit
	/// stales the recordings and asks for the frame; a simulation edit (or an active probe the run does not record,
	/// or a brightness edit the 8-bit samples bake in) stales the run, stops playback and any run in progress,
	/// repaints the scene after a part edit, and asks for the frame; the re-run follows <see cref="EditFinished"/>.
	/// </summary>
	public void Edited(ProjectChange change)
	{
		if (change.Item is ColorScheme or ObservableCollection<ColorScheme>)
		{
			return;
		}
		bool unrecorded = change.AffectsProbes && HasUnrecordedProbe();
		if (change.AffectsProbes && !unrecorded)
		{
			// the frames stay, the recordings are stale: the scratch frame brings the new recording for the position
			if (_runs.Run != null && !_runs.IsProbesStale)
			{
				Logger.Debug("Probes edited ({Item}.{Property}): the run's recordings are stale", change.Item, change.Property);
				_runs.IsProbesStale = true;
			}
			_lastChange = change;
			_editInProgress = true;
			RequestScratch();
			return;
		}
		if (!change.AffectsSimulation && !unrecorded && !IsBrightnessBaked(change))
		{
			return;
		}
		_lastChange = change;
		_editInProgress = true;
		if (_runs.Run != null && !_runs.IsRunStale)
		{
			if (unrecorded)
			{
				Logger.Debug("Probes edited ({Item}.{Property}): an active probe has no recording in the run, so the run is stale", change.Item, change.Property ?? "list");
			}
			else
			{
				Logger.Debug("Edited ({Item}.{Property}): the run is stale", change.Item, change.Property ?? "list");
			}
		}
		bool sceneReplaced = change.Item is Project && change.Property == nameof(Project.Scene);
		if (IsPartChange(change))
		{
			// the parts are painted into the scene pixels: paint again, so the outline follows the edit at once
			_scene.PartsChanged();
		}
		if (change.Item is Scene || sceneReplaced || IsPartChange(change))
		{
			_scene.Update();
		}
		if (_runs.Run != null)
		{
			_runs.IsRunStale = true;
		}
		if (_preview.IsPlaying)
		{
			// the whole run is stale: playing its frames on, or computing more of them, serves nothing (the user's
			// rule, 2026-10-16); stopped first, so the scratch frame below is for the position playback halted at
			Logger.Debug("Playback stopped: the run is stale");
			_preview.IsPlaying = false;
		}
		if (_runs.Run is { IsActive: true } run)
		{
			// its snapshot is already obsolete: stop it now; the re-run follows the edit's end. Any run, since
			// 2026-10-16 (the user's rule: an edit makes the whole run moot, whoever started it); before, a run the
			// user had started went on and was only marked stale
			Logger.Debug("The {Kind} is stopped: its snapshot is obsolete", run.IsAutomatic ? "automatic run" : "run");
			run.Stop();
		}
		RequestScratch();
	}

	/// <summary>
	/// The edit has finished (the application's word, from its input: a mouse button released or pressed, the focus
	/// leaving, Enter in a text box, a command, a drop, an edit that arrived without input): a stale run is redone now,
	/// or as soon as the frame still computing, the run still active or the export in progress is over.
	/// </summary>
	public void EditFinished()
	{
		_editInProgress = false;
		if (Settings.Default.AutoRerun && _runs.IsRunStale && !TryRerun())
		{
			Logger.Debug("Automatic re-run waits: {Reason}", _busy.IsExporting ? "an export is in progress" : _runs.IsActive ? "a run is still active"
				: _runs.IsStarting ? "a run's start waits for the memory warning's answer" : "a preview frame is being computed");
		}
	}

	/// <summary>
	/// View > Preview resolution / frames / samples changed: the run and the scratch frame on screen were made
	/// with the old shape, so both are redone as after an edit (the run marked stale, an automatic one stopped,
	/// the position's frame requested, the re-run following the command's end).
	/// </summary>
	public void PreviewQualityChanged()
	{
		if (_runs.Run is { } run)
		{
			_runs.IsRunStale = true;
			if (run is { IsAutomatic: true, IsActive: true })
			{
				Logger.Debug("The automatic run is stopped: the preview quality changed");
				run.Stop();
			}
		}
		// the setting is a command's doing (the View menu, the memory warning inside a run's start): its end brings the re-run
		_editInProgress = true;
		RequestScratch();
	}

	/// <summary>
	/// Play means "show me the simulation": a missing or stale run is started like an automatic one (position kept,
	/// not followed); a failure goes to the log and status bar. Not while an export reads the store. A run refused
	/// in the memory warning turns playback off again, before the pane's clock starts (2026-11-03): there is no
	/// simulation to show then. Playback is held while the start is decided (<see cref="PreviewState.IsPlaybackHeld"/>,
	/// 2026-11-09) and released after the refusal has turned it off, so the clock never starts for a refused run,
	/// whether the answer came within the call (a blocking window) or later.
	/// </summary>
	public async Task EnsureRunAsync()
	{
		if (_busy.IsExporting || _runs.IsActive || (_runs.Run != null && !_runs.IsRunStale && !_runs.IsProbesStale))
		{
			return;
		}
		_preview.IsPlaybackHeld = true;
		try
		{
			if (!await _runs.StartAsync(automatic: true))
			{
				_preview.IsPlaying = false;
			}
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Error(ex, "The run could not start: {Message}", ex.Message);
		}
		finally
		{
			_preview.IsPlaybackHeld = false;
		}
	}

	/// <summary>A run ended, or an export that held the re-run back: edits made meanwhile are still pending, so look again.</summary>
	public void RunFinished()
	{
		if (_runs.IsRunStale)
		{
			RequestScratch();
			TryRerun();
		}
		else if (_runs.IsProbesStale)
		{
			RequestScratch();
		}
	}

	/// <summary>
	/// A new project is in place and its scene composition under way: no scratch frame applies, and the first one warms
	/// the engine up (an OpenCL compile), better now than on the first click. Called by the application after it has
	/// told the scene composer, so the frame follows the new scene.
	/// </summary>
	public void ProjectReplaced()
	{
		ScratchFrame = null;
		_scratchRequested = null;
		_editInProgress = false;
		RequestScratch();
	}

	/// <summary>Ends the scratch engine; for closing.</summary>
	public void Shutdown()
	{
		_scratchRequested = null;
		_requestWhenComposed = false;
		_scratch?.Dispose();
		_scratch = null;
	}

	/// <summary>A scrub to a position the store cannot show asks for its frame; so does playback stopping at one.</summary>
	private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PreviewState.Position) && NeedsScratch)
		{
			RequestScratch();
		}
		else if (e.PropertyName == nameof(PreviewState.IsPlaying) && !_preview.IsPlaying && NeedsScratch)
		{
			RequestScratch();
		}
	}

	/// <summary>The scene is composed: a frame that waited for it is asked for now, unless another composition is already under way.</summary>
	private void OnSceneComposed()
	{
		if (_requestWhenComposed && !_scene.IsComposing)
		{
			_requestWhenComposed = false;
			RequestScratch();
		}
	}

	/// <summary>Whether the change is to an optical part, one of its points or fibre nodes, a point or node list, or the parts list.</summary>
	private static bool IsPartChange(ProjectChange change) =>
		change.Item is OpticalPart or PartPoint or FiberNode or ObservableCollection<OpticalPart> or ObservableCollection<PartPoint> or ObservableCollection<FiberNode>
			|| change.Item is Project && change.Property == nameof(Project.Parts);

	/// <summary>
	/// Whether an active probe has no recording in the current run: one just switched on, or one just added. A
	/// scratch frame fills its graph for one position only, and a scrub or playback showed it empty until a run
	/// recorded it, so such a probe edit counts as a simulation edit (the user's choice, 2026-10-18). A move or a
	/// measurement change of a recorded probe only stales the recordings, as before.
	/// </summary>
	private bool HasUnrecordedProbe() =>
		_runs.Run is { } run && Project.Probes.Any(probe => probe.Active && !run.Spec.Probes.Any(spec => ReferenceEquals(spec.Origin, probe)));

	/// <summary>
	/// Whether a display edit changes the stored frames after all: a layer's brightness while the preview's
	/// samples are 8-bit palette indices (View > Preview samples), which bake the brightness in at capture time.
	/// Such an edit then counts as a simulation edit: stale run, scratch frame, automatic re-run.
	/// </summary>
	private static bool IsBrightnessBaked(ProjectChange change) =>
		change.Item is DisplayLayer && change.Property == nameof(DisplayLayer.Brightness) && Settings.Default.PreviewSampleBytes == 1;

	/// <summary>
	/// Asks for the frame at the preview's position, now. Not while the preview plays (2026-10-16, after an edit during
	/// playback kept playing the old frames until playback ended): a scratch frame could never serve then, the position
	/// has moved on before it arrives. A simulation edit stops playback at once (<see cref="Edited"/>); what plays on is
	/// a run that has not reached the position, and the frame for the final position is asked for when playback stops.
	/// While the scene is being composed the request waits for the composition (<see cref="OnSceneComposed"/>): the
	/// pixels would otherwise be waited for on the UI thread.
	/// </summary>
	private void RequestScratch()
	{
		if (_preview.IsPlaying || !NeedsScratch || Project.Simulation.Steps < 1)
		{
			return;
		}
		if (_scene.IsComposing)
		{
			_requestWhenComposed = true;
			return;
		}
		RunSpec? spec = TrySnapshotProject();
		if (spec == null)
		{
			return;
		}
		if (_scratch == null)
		{
			_scratch = new ScratchRunner(_uiThread);
			_scratch.Completed += OnScratchCompleted;
		}
		// the user's frame comes first: a run in progress, automatic or started by the user, would otherwise take
		// every core (CPU) or the card's time away from it; it continues once the frame is there (any run since
		// 2026-10-14; a run the user paused stays paused)
		if (_runs.Run is { IsActive: true } run && !run.IsPauseRequested)
		{
			run.Pause();
			_pausedForScratch = run;
		}
		int frame = Math.Clamp(_preview.Position, 0, Math.Max(0, spec.FrameCount - 1));
		// the newest edit with its value (2026-10-27), named on the frame's line when its result shows; a request a
		// newer one supersedes before the runner takes it is never computed and leaves the Trace line alone
		string edit = _lastChange == null ? "" : ": " + _lastChange.Item + "." + (_lastChange.Property ?? "list") + (_lastChange.Values == null ? "" : " = " + _lastChange.NewValueText);
		_lastChange = null;
		string why = (_runs.Run == null ? "no run" : _runs.IsRunStale ? "the run is stale" : _runs.IsProbesStale ? "the probes changed" : "the run has not got there yet") + edit;
		Logger.Trace("Preview frame {Frame} requested from scratch ({Why})", frame, why);
		_scratchRequested = (spec, frame, why);
		_scratch.Request(spec, frame);
		_busy.ComputingFrame = frame;
	}

	private void OnScratchCompleted(object? sender, ScratchResult result)
	{
		// the request this result answers is the newest one: the indicator goes off and the paused run goes on (a newer
		// request keeps both as they are, since its frame comes first)
		if (_scratchRequested is { } requested && ReferenceEquals(requested.Spec, result.Spec) && requested.Frame == result.Frame)
		{
			Logger.Debug("Preview frame {Frame} from scratch ({Why})", result.Frame, requested.Why);
			_scratchRequested = null;
			_busy.ComputingFrame = -1;
			if (_pausedForScratch is { } paused)
			{
				_pausedForScratch = null;
				if (ReferenceEquals(paused, _runs.Run) && paused.IsActive)
				{
					paused.Resume();
				}
			}
		}
		ScratchFrame = result;
		TryRerun();
	}

	/// <summary>
	/// Redoes the stored run when the setting is on, the run is stale and nothing holds it back: an edit still in
	/// progress, a frame still computing, a run still active, a start still waiting for the memory warning's answer or
	/// an export in progress. True when the re-run was begun: started, or its memory warning asked (2026-11-09; a
	/// warning cancelled within the call had counted as held back before, and was logged as waiting for a frame).
	/// </summary>
	private bool TryRerun()
	{
		if (!Settings.Default.AutoRerun || !_runs.IsRunStale || _editInProgress || _runs.IsActive || _runs.IsStarting || ScratchPending || _busy.IsExporting)
		{
			return false;
		}
		Rerun();
		return true;
	}

	/// <summary>
	/// Starts the automatic re-run; the answer to its memory warning may be a while. Not awaited by anyone: an error
	/// other than the expected ones reaches the UI thread's handler, as it did when the start was a plain call.
	/// </summary>
	private async void Rerun()
	{
		try
		{
			await _runs.StartAsync(automatic: true);
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			// no message box for something the user did not ask for; the status bar shows the error
			Logger.Error(ex, "The automatic re-run could not start: {Message}", ex.Message);
		}
	}

	/// <summary>The project as a run spec with the scene composed, or null when it cannot run (logged at Debug).</summary>
	private RunSpec? TrySnapshotProject()
	{
		try
		{
			return RunSpec.Create(Project, _scene.Load(), Settings.Default.UseGpu, quality: RunHost.PreviewQualitySetting);
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Debug("No preview: {Message}", ex.Message);
			return null;
		}
	}
}
