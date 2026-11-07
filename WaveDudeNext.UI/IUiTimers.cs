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

namespace WaveDudeNext.UI;

/// <summary>
/// The UI thread's timers as the models of this library need them (2026-11-07; each application implements it, WPF
/// over its <c>DispatcherTimer</c> at Background priority): a timer calls back once on the UI thread when its interval
/// has passed since it was last started. The models use them to pace work, never to judge an edit: the scratch frame
/// after a burst of edits or scrubs is asked for when the burst has rested for a moment, and the automatic re-run
/// waits for a longer quiet (see <see cref="LivePreview"/>).
/// </summary>
public interface IUiTimers
{
	/// <summary>A stopped timer that calls <paramref name="tick"/> on the UI thread <paramref name="interval"/> after each <see cref="IUiTimer.Restart"/>.</summary>
	IUiTimer Create(TimeSpan interval, Action tick);
}

/// <summary>One timer of <see cref="IUiTimers"/>: restarted by every edit it paces, it fires once when left alone for its interval.</summary>
public interface IUiTimer
{
	/// <summary>Whether the timer is running towards its tick.</summary>
	bool IsRunning { get; }

	/// <summary>Starts the interval afresh; a tick already due is dropped.</summary>
	void Restart();

	/// <summary>Stops the timer without a tick.</summary>
	void Stop();
}
