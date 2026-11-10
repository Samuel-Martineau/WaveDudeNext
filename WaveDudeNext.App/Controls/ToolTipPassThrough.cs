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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// Tooltips let the pointer through to what lies under them, as WPF's do (2026-11-10, the user's report on the File >
/// Examples entries: the tooltip "steals the hover under it"). Avalonia places a tooltip 20 px below the pointer, in a
/// popup window of its own that takes the mouse, and its tooltip service keeps a tooltip open while the pointer is over
/// that window; so moving down a menu parks the pointer on the tooltip of the entry just left, and the entry beneath
/// never gets the hover. Avalonia's <see cref="PopupRoot.IsHitTestVisible"/> forwards to the platform's popup
/// (<c>IPopupImpl.SetHitTestVisible</c>, documented to make the native window input-transparent); the tooltip never
/// sets it, so this does, on every tooltip's popup root as the tooltip is loaded into it (a new root per opening). The
/// pointer then reaches the window beneath, where the service sees the entry under it and moves the tooltip on, which
/// is what WPF's hit-test transparent tooltip windows give the WPF head. UI thread; call <see cref="Install"/> once.
/// </summary>
public static class ToolTipPassThrough
{
	private static bool _installed;

	/// <summary>Once per process: every tooltip's popup window is made transparent to the pointer when the tooltip is loaded into it.</summary>
	public static void Install()
	{
		if (_installed)
		{
			return;
		}
		_installed = true;
		Control.LoadedEvent.AddClassHandler<ToolTip>(OnLoaded);
	}

	private static void OnLoaded(ToolTip tip, RoutedEventArgs e)
	{
		if (TopLevel.GetTopLevel(tip) is PopupRoot root && root.IsHitTestVisible)
		{
			root.IsHitTestVisible = false;
		}
	}
}
