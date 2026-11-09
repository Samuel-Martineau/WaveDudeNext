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

using WaveDudeNext.Engine;
using WaveDudeNext.Model;

namespace WaveDudeNext.Probes;

/// <summary>
/// Accumulates probe samples and line profiles from the engine into <see cref="ProbeTrace"/>s and hands
/// incremental batches to the UI. Lives entirely on the run's worker: <see cref="Collect"/> is called after
/// every recorded frame and about every 50 ms, <see cref="CollectProfiles"/> once per recorded frame (the
/// profiles are kept per frame); <see cref="TakeForUi"/> then packages what arrived since the previous batch
/// into a <see cref="ProbeUpdate"/>, which the run raises as <c>ProbesUpdated</c> and the UI applies to its own
/// traces. The UI thread never touches this object itself.
/// </summary>
public sealed class ProbeCollector
{
	/// <summary>Creates one empty <see cref="ProbeTrace"/> and one pending sample list per probe of <paramref name="sampler"/>, and a counting detector's Poisson process.</summary>
	public ProbeCollector(ProbeSampler sampler)
	{
		_pending = [];
		_sampler = sampler;
		Traces = new ProbeTrace[sampler.Count];
		_totals = new double[sampler.Count];
		_pendingProfiles = [];
		_counters = new PoissonProcess?[sampler.Count];
		_countedExpectation = new double[sampler.Count];
		_countedSteps = new int[sampler.Count];
		_imagers = new PoissonProcess[]?[sampler.Count];
		for (int i = 0; i < sampler.Count; i++)
		{
			Traces[i] = new ProbeTrace();
			_pending.Add(new List<float>());
			_pendingProfiles.Add(new List<float[]>());
			if (sampler.Detectors[i] == ProbeDetector.Counting)
			{
				_counters[i] = new PoissonProcess((ulong)(uint)sampler.Seed, Stream(sampler.Streams[i], 0));
			}
		}
	}

	/// <summary>The random stream of a detector's process: the probe's name hash, with the pixel's index along the line for an imaging detector's processes.</summary>
	private static ulong Stream(uint nameHash, int pixel) => ((ulong)nameHash << 24) ^ (ulong)(uint)pixel;

	/// <summary>
	/// Drains the engine's buffered point-probe samples (<see cref="IWaveEngine.TakeProbeSamples"/>, one value
	/// per step since the previous call) into the series traces and the pending UI batch. For cumulative
	/// probes (point + IntegratedValue) the per-step intensities are replaced in place by their running
	/// sum, which stays at 0 for steps before the probe's start step. Does nothing when there are no point
	/// probes.
	/// </summary>
	/// <param name="engine">Engine that buffered the samples since the previous call.</param>
	/// <param name="stepsDone">Steps completed so far; recorded as <see cref="ProbeTrace.StepsSampled"/>.</param>
	public void Collect(IWaveEngine engine, int stepsDone)
	{
		if (!_sampler.HasSeries)
		{
			return;
		}
		float[][] samples = engine.TakeProbeSamples();
		// the number of steps already processed before this batch, so the absolute step of values[j] can be worked
		// out below (one value per step, in order) - needed to purge a cumulative total from before "start after step"
		int batchStartStep = _lastStepsDone;
		for (int i = 0; i < _sampler.Count; i++)
		{
			if (_sampler.IsProfile[i] || i >= samples.Length)
			{
				continue;
			}
			float[] values = samples[i];
			if (values.Length == 0)
			{
				continue;
			}
			if (_sampler.Cumulative[i])
			{
				bool useStartAfterStep = _sampler.UseStartAfterStep[i];
				int startAfterStep = _sampler.StartAfterStep[i];
				for (int j = 0; j < values.Length; j++)
				{
					// sample j belongs to step batchStartStep + j + 1 (one sample per step)
					if (useStartAfterStep && batchStartStep + j + 1 < startAfterStep)
					{
						// before recording starts: keep the running total at zero, purging any intensity that
						// arrived earlier, so the total does not jump the moment recording starts
						_totals[i] = 0.0;
						values[j] = 0f;
					}
					else
					{
						_totals[i] += values[j];
						values[j] = (float)_totals[i];
					}
				}
			}
			Traces[i].Series.Add(values);
			Traces[i].StepsSampled = stepsDone;
			_pending[i].AddRange(values);
		}
		_lastStepsDone = stepsDone;
	}

	/// <summary>
	/// Reads the current line-probe profiles from the engine (<see cref="IWaveEngine.ReadProbeProfiles"/>),
	/// appends them to the traces as the profiles of the next frame and queues them for the next
	/// <see cref="TakeForUi"/>. Does nothing when there are no line probes. The run calls this exactly once
	/// per recorded frame, in frame order, so a trace's profile list is indexed by frame.
	/// </summary>
	/// <param name="engine">Engine holding the current profiles.</param>
	/// <param name="stepsDone">Steps completed so far; recorded as <see cref="ProbeTrace.StepsSampled"/>.</param>
	public void CollectProfiles(IWaveEngine engine, int stepsDone)
	{
		if (!_sampler.HasProfiles)
		{
			return;
		}
		float[]?[] profiles = engine.ReadProbeProfiles();
		for (int i = 0; i < _sampler.Count; i++)
		{
			if (!_sampler.IsProfile[i] || i >= profiles.Length || profiles[i] is not { } profile)
			{
				continue;
			}
			switch (_sampler.Detectors[i])
			{
				case ProbeDetector.Counting:
					// a series, not a profile: the detections of the line's total, step by step
					CountDetections(i, profile, stepsDone);
					continue;
				case ProbeDetector.Imaging:
					profile = ImageDetections(i, profile);
					break;
			}
			Traces[i].AddProfile(profile, _sampler.Lengths[i]);
			Traces[i].StepsSampled = stepsDone;
			_pendingProfiles[i].Add(profile);
		}
		_lastStepsDone = stepsDone;
	}

	/// <summary>
	/// A counting detector's detections since its previous collection (2026-11-07, the user's choice of resolution): the
	/// line's integrated intensity, summed over its pixels and times the sensitivity, is the cumulative expectation; the
	/// frame's increase is shared evenly over the steps since the previous collection, and each step's detections are
	/// the events of the probe's Poisson process in its share (a sum of Poisson counts is Poisson of the sum, so the
	/// frame's total is exact and the steps within it draw from the frame's mean rate). One value a step into the series.
	/// </summary>
	private void CountDetections(int i, float[] profile, int stepsDone)
	{
		PoissonProcess counter = _counters[i]!;
		int steps = stepsDone - _countedSteps[i];
		if (steps <= 0)
		{
			return;
		}
		double total = 0.0;
		foreach (float value in profile)
		{
			total += value;
		}
		double from = _countedExpectation[i];
		double expectation = Math.Max(from, total * _sampler.Sensitivities[i]);
		var values = new float[steps];
		int before = counter.Count;
		for (int s = 0; s < steps; s++)
		{
			int upTo = counter.CountUpTo(from + (expectation - from) * (s + 1) / steps);
			values[s] = upTo - before;
			before = upTo;
		}
		_countedExpectation[i] = expectation;
		_countedSteps[i] = stepsDone;
		Traces[i].Series.Add(values);
		Traces[i].StepsSampled = stepsDone;
		_pending[i].AddRange(values);
	}

	/// <summary>
	/// An imaging detector's image at this frame: at every pixel of the line the events of that pixel's Poisson process
	/// up to its integrated intensity times the sensitivity, which only grows, so the image builds up over the run and a
	/// frame asked for once (a scratch frame) shows what the run shows there. With a bin of more than a pixel
	/// (2026-11-08, the user's request) the detections of a bin's pixels are added up and every pixel of the bin shows the
	/// total, a histogram of flat bars over the line; the last bin may be shorter.
	/// </summary>
	private float[] ImageDetections(int i, float[] profile)
	{
		PoissonProcess[] imagers = _imagers[i] ??= MakeImagers(i, profile.Length);
		double sensitivity = _sampler.Sensitivities[i];
		int bin = _sampler.BinSizes[i];
		var counts = new float[profile.Length];
		int pixels = Math.Min(profile.Length, imagers.Length);
		for (int start = 0; start < pixels; start += bin)
		{
			int end = Math.Min(pixels, start + bin);
			float total = 0f;
			for (int k = start; k < end; k++)
			{
				total += imagers[k].CountUpTo(profile[k] * sensitivity);
			}
			for (int k = start; k < end; k++)
			{
				counts[k] = total;
			}
		}
		return counts;
	}

	private PoissonProcess[] MakeImagers(int i, int pixels)
	{
		var imagers = new PoissonProcess[pixels];
		for (int k = 0; k < pixels; k++)
		{
			imagers[k] = new PoissonProcess((ulong)(uint)_sampler.Seed, Stream(_sampler.Streams[i], k));
		}
		return imagers;
	}

	/// <summary>
	/// Drains the pending series samples and the profiles recorded since the previous call into a
	/// <see cref="ProbeUpdate"/> for the UI thread. Sample arrays are copied; profile arrays are handed over by
	/// reference, which is safe because the engine allocates a new array on every read and nobody writes to it
	/// afterwards.
	/// </summary>
	/// <returns>A batch covering everything since the previous call; never null.</returns>
	public ProbeUpdate TakeForUi()
	{
		int count = _sampler.Count;
		float[][] samples = new float[count][];
		float[][][] profiles = new float[count][][];
		for (int i = 0; i < count; i++)
		{
			samples[i] = _pending[i].ToArray();
			_pending[i].Clear();
			profiles[i] = _pendingProfiles[i].ToArray();
			_pendingProfiles[i].Clear();
		}
		return new ProbeUpdate(samples, profiles, _sampler.Lengths, _lastStepsDone);
	}

	/// <summary>Worker-side trace per probe (index parallel to the sampler).</summary>
	public ProbeTrace[] Traces { get; }

	/// <summary>Per-probe pixel indices, flags and line lengths; immutable.</summary>
	private readonly ProbeSampler _sampler;

	/// <summary>Per probe: running sum for cumulative point probes, kept in double to limit drift; unused entries stay 0.</summary>
	private readonly double[] _totals;

	/// <summary>Per probe: series values collected since the last <see cref="TakeForUi"/>.</summary>
	private readonly List<List<float>> _pending;

	/// <summary>Per probe: the profiles recorded since the last <see cref="TakeForUi"/>, in frame order.</summary>
	private readonly List<List<float[]>> _pendingProfiles;

	/// <summary>Per probe: a counting detector's Poisson process; null for the others.</summary>
	private readonly PoissonProcess?[] _counters;

	/// <summary>Per probe: the cumulative expectation a counting detector was last counted up to.</summary>
	private readonly double[] _countedExpectation;

	/// <summary>Per probe: the steps a counting detector's series covers so far.</summary>
	private readonly int[] _countedSteps;

	/// <summary>Per probe: an imaging detector's Poisson process per pixel of its line, made at its first profile; null for the others.</summary>
	private readonly PoissonProcess[]?[] _imagers;

	/// <summary>Steps completed at the most recent collect call; numbers the samples of the next batch and is reported as <see cref="ProbeUpdate.StepsDone"/>.</summary>
	private int _lastStepsDone;
}
