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

using System.Windows.Threading;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// The UI thread's timers for the models of <c>WaveDudeNext.UI</c> (<see cref="IUiTimers"/>): each a
/// <see cref="DispatcherTimer"/> at Background priority on <paramref name="dispatcher"/>, so a tick is served when the
/// hand rests, as the models' pacing means it to (the scene composition's coalescing waits at that priority for the same
/// reason).
/// </summary>
/// <param name="dispatcher">The UI thread's dispatcher.</param>
public sealed class DispatcherTimers(Dispatcher dispatcher) : IUiTimers
{
	/// <inheritdoc/>
	public IUiTimer Create(TimeSpan interval, Action tick) => new Timer(dispatcher, interval, tick);

	private sealed class Timer : IUiTimer
	{
		private readonly DispatcherTimer _timer;

		public Timer(Dispatcher dispatcher, TimeSpan interval, Action tick)
		{
			_timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
			_timer.Tick += (_, _) => tick();
		}

		public bool IsRunning => _timer.IsEnabled;

		public void Restart()
		{
			_timer.Stop();
			_timer.Start();
		}

		public void Stop() => _timer.Stop();
	}
}
