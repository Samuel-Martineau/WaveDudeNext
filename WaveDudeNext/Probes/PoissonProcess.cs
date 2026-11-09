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

namespace WaveDudeNext.Probes;

/// <summary>
/// A unit-rate Poisson process on the half-line, walked from 0 (a probabilistic detector's detections, 2026-11-07): the
/// number of its events up to a position is Poisson-distributed with that position as its mean, and the counts at two
/// positions differ by the events between them. So one process, asked for the count at the detector's cumulative
/// expectation (intensity integrated over the steps, times the sensitivity), gives the same detections whether it is
/// asked once at the end (a scratch frame) or frame by frame (a run), and the counts never fall. Seeded from the
/// project's seed and the probe's name, so a project gives the same detections every time. The gaps between events
/// are exponential; a span of more than <see cref="WalkLimit"/> expected events in one call is jumped with a normal
/// approximation of its count (a sensitivity typed far too high would otherwise walk for ever), so beyond it the two
/// ways of asking agree in distribution only. One thread at a time.
/// </summary>
public sealed class PoissonProcess
{
	/// <summary>The longest span, in expected events, walked event by event in one call.</summary>
	public const double WalkLimit = 100_000.0;

	private readonly Pcg32 _random;
	private double _next;
	private int _count;

	/// <summary>A process whose events are drawn from <paramref name="seed"/> on stream <paramref name="stream"/>.</summary>
	public PoissonProcess(ulong seed, ulong stream)
	{
		_random = new Pcg32(seed, stream);
		_next = Exponential();
	}

	/// <summary>The events counted so far: the count at the largest position asked for.</summary>
	public int Count => _count;

	/// <summary>The number of events up to <paramref name="position"/> (the cumulative expectation); never less than a count given before.</summary>
	public int CountUpTo(double position)
	{
		if (!double.IsFinite(position))
		{
			return _count;
		}
		if (position - _next > WalkLimit)
		{
			// too far to walk: the count of the span up to a margin before the position is normal about its length, the rest walked
			double span = position - _next - WalkLimit / 2.0;
			double jumped = Math.Round(span + Math.Sqrt(span) * Gaussian());
			_count += (int)Math.Clamp(jumped, 0.0, int.MaxValue - (double)_count);
			_next += span;
		}
		while (_next <= position)
		{
			_count++;
			_next += Exponential();
		}
		return _count;
	}

	/// <summary>A gap between events: exponential with mean 1.</summary>
	private double Exponential() => -Math.Log(1.0 - _random.NextDouble());

	/// <summary>A standard normal deviate (Box–Muller).</summary>
	private double Gaussian()
	{
		double u = 1.0 - _random.NextDouble();
		double v = _random.NextDouble();
		return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
	}
}
