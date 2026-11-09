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

using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;
using WaveDudeNext.WPF.Commands;
using WaveDudeNext.WPF.Converters;
using WaveDudeNext.WPF.Rendering;

namespace WaveDudeNext.WPF;

/// <summary>
/// Handlers of <see cref="Commands.ProjectCommands"/>: structural edits of the open project, carried out by the
/// <see cref="ProjectItems"/> model. A command parameter names the node to act on; without one the selection is used.
/// What stays here is what needs the window: where on the shown canvas a new item lands, the menus of part and probe
/// kinds, a text box's own Delete, and the dialogs of the scene image's commands, which the <see cref="SceneImageFile"/> model
/// carries out.
/// </summary>
public partial class MainWindow
{
	/// <summary>The item commands of the open project; created in the constructor.</summary>
	private readonly ProjectItems _items;

	private void CanSelectAll(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Selection.Groupable(Project).Any();

	/// <summary>Ctrl+A: every source, part and probe; the last one becomes the current item.</summary>
	private void OnSelectAll(object sender, ExecutedRoutedEventArgs e) => Selection.SelectAll(Project);

	private void CanToggleSelected(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Selection.IsGroupable(Selection.Target(e.Parameter));

	/// <summary>Insert or Ctrl+Space on a tree row: into the selection, or out of it (the current item then moves to the last one left).</summary>
	private void OnToggleSelected(object sender, ExecutedRoutedEventArgs e)
	{
		if (Selection.Target(e.Parameter) is { } item && Selection.IsGroupable(item))
		{
			Selection.Toggle(item);
		}
	}

	/// <summary>The centre of the canvas the preview shows (an image scene's own size), where a new source, probe or part lands (2026-10-26, Jeroen's report: one at the corner went unnoticed).</summary>
	private (int X, int Y) CanvasCentre => (Preview.CanvasWidth / 2, Preview.CanvasHeight / 2);

	private void OnAddSource(object sender, ExecutedRoutedEventArgs e) => _items.AddSource(CanvasCentre.X, CanvasCentre.Y);

	/// <summary>
	/// Add part: with an <see cref="OpticalPart"/> subclass as parameter (the menu entries) the part is appended; without one (the
	/// "+ Part" button, the Parts folder's plus) the invoking element's context menu, the menu of kinds, opens under it,
	/// so the kind is chosen there (keyboard: Space opens it, the arrows walk it, Enter picks).
	/// </summary>
	private void OnAddPart(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is Type kind)
		{
			_items.AddPart(kind, CanvasCentre.X, CanvasCentre.Y);
		}
		else
		{
			OpenKindMenu(e.OriginalSource);
		}
	}

	/// <summary>Opens the menu of kinds attached to <paramref name="element"/> (its context menu: the part kinds, or the probe kinds), placed under it.</summary>
	private static void OpenKindMenu(object? element)
	{
		if (element is FrameworkElement { ContextMenu: { } menu } anchor)
		{
			menu.PlacementTarget = anchor;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		}
	}

	/// <summary>
	/// Add probe: with a <see cref="ProbeDetector"/> as parameter (the menu entries) the probe is appended, a standard one
	/// or a probabilistic detector (2026-11-07, Jeroen's addition); without one (the "+ Probe" button, the Probes
	/// folder's plus) the invoking element's context menu, the menu of kinds, opens under it as the parts' does.
	/// </summary>
	private void OnAddProbe(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is ProbeDetector detector)
		{
			_items.AddProbe(CanvasCentre.X, CanvasCentre.Y, detector);
		}
		else
		{
			OpenKindMenu(e.OriginalSource);
		}
	}

	/// <summary>A folder's plus: the command's parameter is the folder's node, whose list takes the new item.</summary>
	private void CanAdd(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Selection.Target(e.Parameter) is NamedCollection folder && _items.IsList(folder.Items);

	private void OnAdd(object sender, ExecutedRoutedEventArgs e)
	{
		if (Selection.Target(e.Parameter) is not NamedCollection folder)
		{
			return;
		}
		if (_items.HasKinds(folder.Items))
		{
			// a part's or a probe's kind is chosen first: the menu of kinds under the button
			OpenKindMenu(e.OriginalSource);
		}
		else
		{
			// at the centre of the shown canvas, as the toolbar's buttons put it (the folder's plus had taken the scene's
			// own size until 2026-11-06, which an image scene does not have)
			_items.AddTo(folder.Items, CanvasCentre.X, CanvasCentre.Y);
		}
	}

	private void CanEditItem(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _items.CanEdit(e.Parameter);

	private void CanToggleEnabled(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _items.CanToggleEnabled(e.Parameter);

	/// <summary>The tree's check boxes by keyboard: Space on a selected source, part or probe flips it (the author's point 11, 2026-10-13); each of a selected group flips on its own.</summary>
	private void OnToggleEnabled(object sender, ExecutedRoutedEventArgs e) => _items.ToggleEnabled(e.Parameter);

	private void OnDuplicate(object sender, ExecutedRoutedEventArgs e) => _items.Duplicate(e.Parameter);

	/// <summary>Edit > Delete: in a text box (the menu's command starts from the element that had the focus) its selected text; else what Remove takes.</summary>
	private void CanDelete(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.OriginalSource is System.Windows.Controls.TextBox box
			? box is { IsReadOnly: false, SelectionLength: > 0 }
			: _items.CanEdit(null);

	private void OnDelete(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.OriginalSource is System.Windows.Controls.TextBox box)
		{
			box.SelectedText = "";
		}
		else
		{
			ProjectCommands.Remove.Execute(null, this);
		}
	}

	private void OnRemove(object sender, ExecutedRoutedEventArgs e) => _items.Remove(e.Parameter);

	private void CanMoveUp(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _items.CanMove(e.Parameter, -1);

	private void CanMoveDown(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _items.CanMove(e.Parameter, +1);

	private void OnMoveUp(object sender, ExecutedRoutedEventArgs e) => _items.Move(e.Parameter, -1);

	private void OnMoveDown(object sender, ExecutedRoutedEventArgs e) => _items.Move(e.Parameter, +1);

	/// <summary>
	/// Scene > Browse: picks an image file, which the scene image model embeds in the project (2026-10-13). A file
	/// that cannot be read as an image is logged there as an error, which the status bar shows.
	/// </summary>
	private void OnBrowseSceneImage(object sender, ExecutedRoutedEventArgs e)
	{
		var dialog = new OpenFileDialog
		{
			Filter = (string)FindResource("SceneImageFilter"),
			Title = (string)FindResource("TipBrowseSceneImage"),
		};
		if (dialog.ShowDialog(this) == true)
		{
			SceneImage.TryEmbed(dialog.FileName);
		}
	}

	private void CanRemoveSceneImage(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = SceneImage.CanRemove;

	/// <summary>Remove beside Browse (2026-10-26, Jeroen's report: there was no way to drop an image): a blank canvas of the image's size remains, one undo step.</summary>
	private void OnRemoveSceneImage(object sender, ExecutedRoutedEventArgs e) => SceneImage.Remove();

	private void CanSaveSceneImage(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = SceneImage.CanSave;

	/// <summary>
	/// The save button between the picker and the X (2026-10-29, the user's design): writes the embedded bytes, the file as
	/// it was embedded, to a file of the user's choice, proposed under the image's name in its folder when that exists, else
	/// the project's. An export: the project, its path and the watch stay as they are.
	/// </summary>
	private void OnSaveSceneImage(object sender, ExecutedRoutedEventArgs e)
	{
		if (!SceneImage.CanSave)
		{
			return;
		}
		string extension = SceneImage.SaveExtension;
		var dialog = new SaveFileDialog
		{
			Title = (string)FindResource("TipSaveSceneImage"),
			// the embedded file's own type; an unknown one gets the picker's list
			Filter = extension.Length > 0 ? string.Format((string)FindResource("SceneImageSaveFilterFormat"), extension) : (string)FindResource("SceneImageFilter"),
			AddExtension = extension.Length > 0,
			DefaultExt = extension,
			FileName = SceneImage.SaveFileName,
			InitialDirectory = SceneImage.SaveFolder,
		};
		if (dialog.ShowDialog(this) != true)
		{
			Logger.Debug("Scene image save cancelled in the Save dialog");
			return;
		}
		if (!SceneImage.TrySave(dialog.FileName, out Exception? error) && error != null)
		{
			ShowError(string.Format((string)FindResource("SceneImageSaveError"), dialog.FileName), error);
		}
	}
}
