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
/// Undo and redo (2026-10-22, the user's request): the Edit menu's two commands, handed to the <see cref="UndoHistory"/>
/// model, which keeps the steps, carries the selection over and tells the document whether it is dirty. The window
/// tells it what happens to the project: a replaced one (<c>OnProjectReplaced</c>), an edit (<c>OnProjectChanged</c>),
/// a save (the document's <see cref="ProjectDocument.Saved"/>) and, through the <see cref="Platform.EditBoundaries"/>
/// watching its input, the moment an edit has finished and becomes a step. UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>The undo history of the open project; created in the constructor.</summary>
	private readonly UndoHistory _history;

	private void CanUndo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _history.CanUndo;

	private void CanRedo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _history.CanRedo;

	private void OnUndo(object sender, ExecutedRoutedEventArgs e) => _history.Undo();

	private void OnRedo(object sender, ExecutedRoutedEventArgs e) => _history.Redo();
}
