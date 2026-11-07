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
using WaveDudeNext.Engine;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.UI;

/// <summary>
/// The pictures' Auto brightness (2026-10-20, Jeroen's point 9; the user's choice of the variants: a one-shot fit to
/// the frame shown, the brightness then staying put, so a decaying wave still looks like one; a model of this library
/// since 2026-11-07, the main window's before). A frame of halves (a 16-bit run, or a scratch frame in 16-bit) is
/// fitted at once; palette indices are clipped at the brightness they were captured with, so for an 8-bit preview the
/// frame is computed once more in 16-bit, on an engine of its own, any run paused meanwhile (the user's frame comes
/// first, as for scratch frames), and fitted when it arrives (<see cref="Fitted"/>). The fit is an ordinary brightness
/// edit: in 8-bit it is a simulation edit as any other. UI thread only.
/// </summary>
public sealed class AutoBrightness : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private readonly ProjectDocument _document;
	private readonly RunHost _runs;
	private readonly PreviewState _preview;
	private readonly BusyState _busy;
	private readonly SceneComposer _scene;
	private readonly SynchronizationContext _uiThread;

	/// <summary>Computes the 16-bit frame of an 8-bit preview for a fit; null between fits (its engine is released).</summary>
	private ScratchRunner? _fitRunner;

	/// <summary>The layer and frame the pending fit is for.</summary>
	private (ProbeLayer Layer, int Frame) _fitRequest;

	/// <summary>The run paused while the fit's frame computes.</summary>
	private SimulationRun? _pausedForFit;

	/// <param name="document">The open project: its display settings take the fit.</param>
	/// <param name="runs">The run paused while a frame is computed for the fit.</param>
	/// <param name="preview">The frame shown, fitted when it is 16-bit; the position the frame is computed for otherwise.</param>
	/// <param name="busy">Asked whether an export is in progress, which rules a fit out.</param>
	/// <param name="scene">The scene pixels the computed frame takes.</param>
	/// <param name="uiThread">The UI thread for the computed frame's arrival.</param>
	public AutoBrightness(ProjectDocument document, RunHost runs, PreviewState preview, BusyState busy, SceneComposer scene, SynchronizationContext uiThread)
	{
		_document = document;
		_runs = runs;
		_preview = preview;
		_busy = busy;
		_scene = scene;
		_uiThread = uiThread;
	}

	/// <summary>
	/// A fit that had to compute its frame arrived and was applied, after the command that asked for it had finished: an
	/// edit complete in itself, whose undo step the application ends here. A fit of the frame shown is applied inside
	/// the command and raises nothing.
	/// </summary>
	public event EventHandler? Fitted;

	/// <summary>Whether a frame is being computed for a fit; a second fit waits for it.</summary>
	public bool IsFitting => _fitRunner != null;

	/// <summary>Whether a fit can begin: none is computing, no export is in progress, and the project has steps to run.</summary>
	public bool CanFit => !IsFitting && !_busy.IsExporting && _document.Project.Simulation.Steps > 0;

	/// <summary>Fits <paramref name="layer"/>'s brightness to the frame shown, at once from its 16-bit samples, else to the same frame computed in 16-bit.</summary>
	public void Fit(ProbeLayer layer)
	{
		FrameLayer plane = PlaneOf(layer);
		if (_preview.ShownFrame is { Source: { Format: FrameFormat.Half } source, Frame: int shown } && source.Has(plane) && source.Count > 0)
		{
			var halves = new Half[source.Width * source.Height];
			if (source.CopyLayer(Math.Clamp(shown, 0, source.Count - 1), plane, halves))
			{
				Apply(layer, BrightnessFit.For(halves, source.KindOf(plane)), _preview.Position);
				return;
			}
		}
		RunSpec spec;
		try
		{
			spec = RunSpec.Create(_document.Project, _scene.Load(), Settings.Default.UseGpu, trackIntensity: plane == FrameLayer.Intensity,
				quality: RunHost.PreviewQualitySetting with { Format = FrameFormat.Half });
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Warn("No brightness fit: {Message}", ex.Message);
			return;
		}
		int frame = Math.Clamp(_preview.Position, 0, Math.Max(0, spec.FrameCount - 1));
		Logger.Info("Fitting the {Layer} brightness: frame {Frame} is computed in 16-bit", layer == ProbeLayer.Field ? "field" : "intensity", frame);
		if (_runs.Run is { IsActive: true } run && !run.IsPauseRequested)
		{
			run.Pause();
			_pausedForFit = run;
		}
		_fitRequest = (layer, frame);
		_fitRunner = new ScratchRunner(_uiThread);
		_fitRunner.Completed += OnFitFrameCompleted;
		_fitRunner.Request(spec, frame);
		OnPropertyChanged(nameof(IsFitting));
		OnPropertyChanged(nameof(CanFit));
	}

	private void OnFitFrameCompleted(object? sender, ScratchResult result)
	{
		ScratchRunner? runner = _fitRunner;
		_fitRunner = null;
		if (runner != null)
		{
			runner.Completed -= OnFitFrameCompleted;
		}
		if (_pausedForFit is { } paused)
		{
			_pausedForFit = null;
			if (ReferenceEquals(paused, _runs.Run) && paused.IsActive)
			{
				paused.Resume();
			}
		}
		FrameLayer plane = PlaneOf(_fitRequest.Layer);
		IFrameSource source = new SnapshotFrameSource(result.Snapshot);
		var halves = new Half[source.Width * source.Height];
		double? fit = source.Has(plane) && source.CopyLayer(0, plane, halves) ? BrightnessFit.For(halves, source.KindOf(plane)) : null;
		// the runner's engine is released once its result is read (the snapshot belongs to the runner)
		runner?.Dispose();
		Apply(_fitRequest.Layer, fit, result.Frame);
		Fitted?.Invoke(this, EventArgs.Empty);
		OnPropertyChanged(nameof(IsFitting));
		OnPropertyChanged(nameof(CanFit));
	}

	/// <summary>Sets the fitted brightness on the layer, or says there was nothing to fit.</summary>
	private void Apply(ProbeLayer layer, double? fit, int frame)
	{
		string name = layer == ProbeLayer.Field ? "field" : "intensity";
		Display display = _document.Project.Display;
		DisplayLayer settings = layer == ProbeLayer.Field ? display.Field : display.Intensity;
		if (fit is not { } brightness)
		{
			Logger.Info("No {Layer} brightness fitted: frame {Frame} is empty", name, frame);
			return;
		}
		Logger.Info("The {Layer} brightness is fitted to frame {Frame}: {Old:G4} becomes {New:G4}", name, frame, settings.Brightness, brightness);
		settings.Brightness = brightness;
	}

	private static FrameLayer PlaneOf(ProbeLayer layer) => layer == ProbeLayer.Intensity ? FrameLayer.Intensity : FrameLayer.Field;
}
