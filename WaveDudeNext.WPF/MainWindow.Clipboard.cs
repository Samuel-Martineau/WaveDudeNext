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

using System.Windows.Input;
using WaveDudeNext.UI;

namespace WaveDudeNext.WPF;

/// <summary>
/// Cut, Copy and Paste of project objects (2026-10-23, the user's choices): WPF's commands bound on the window, so a
/// focused text box handles them for its text first and everywhere else they act on the selection (the tree, the handles
/// on the pictures, the Edit menu, a tree row's menu), carried out by the <see cref="ObjectClipboard"/> model on the
/// Windows clipboard (<see cref="Platform.WpfClipboard"/>). UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>Cut, copy and paste of the open project's objects; created in the constructor.</summary>
	private readonly ObjectClipboard _clipboard;

	private void CanCopyObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _clipboard.CanCopy(e.Parameter);

	private void CanCutObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _clipboard.CanCut(e.Parameter);

	private void CanPasteObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _clipboard.CanPaste(e.Parameter);

	private void OnCopyObjects(object sender, ExecutedRoutedEventArgs e) => _clipboard.Copy(e.Parameter);

	private void OnCutObjects(object sender, ExecutedRoutedEventArgs e) => _clipboard.Cut(e.Parameter);

	private void OnPasteObjects(object sender, ExecutedRoutedEventArgs e) => _clipboard.Paste(e.Parameter);
}
