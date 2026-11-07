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
using System.Collections.ObjectModel;
using WaveDudeNext.Model;

namespace WaveDudeNext.UI;

/// <summary>
/// A request to bring something to the user (<see cref="Selection.Reveal"/>, <see cref="Selection.RevealFolder"/>): the
/// tree shows its row selected and gives it the keyboard focus, and with <see cref="Edit"/> the properties pane puts
/// the caret into its first editor. Every request is an object of its own and equal to no other, so a view bound to
/// <see cref="Selection.RevealRequest"/> sees a change even when the same item is asked for twice.
/// </summary>
/// <param name="target">The item, or with <paramref name="isFolder"/> the list whose folder is meant.</param>
/// <param name="isFolder">Whether <paramref name="target"/> is one of the project's lists, standing for its folder in the tree.</param>
/// <param name="edit">Whether the item is to be edited at once.</param>
public sealed class RevealRequest(object target, bool isFolder, bool edit)
{
	/// <summary>The item to show, which is the current item already; or, with <see cref="IsFolder"/>, the list of the project whose folder in the tree is meant.</summary>
	public object Target { get; } = target;

	/// <summary>Whether <see cref="Target"/> is one of the project's lists, standing for its folder in the tree.</summary>
	public bool IsFolder { get; } = isFolder;

	/// <summary>Whether the item is to be edited at once: a newly added one, whose name the user types next.</summary>
	public bool Edit { get; } = edit;
}

/// <summary>
/// What is selected in the open project (2026-11-06, the user's design: the application's state lives in models the
/// views bind, not in the window): the <see cref="Current"/> item, which the properties pane shows and the tree marks
/// as its own selected row, and the <see cref="Items"/> selected with it, one shared list the tree, the preview's
/// overlays and the commands all edit and watch (2026-10-19, the user's request). The views bind both, the current item
/// two-way, so a selection made in one shows in the others without anyone passing it on. A command that wants more than
/// the selection, the item's row in front of the user with the keyboard focus on it, says so with <see cref="Reveal"/>,
/// which the views bind as <see cref="RevealRequest"/>: nobody reaches into a view. UI thread only; a change of
/// <see cref="Current"/> or <see cref="RevealRequest"/> raises <see cref="ObservableObject.PropertyChanged"/>, the list
/// its own collection changes.
/// </summary>
public sealed class Selection : ObservableObject
{
	private object? _current;
	private RevealRequest? _revealRequest;

	/// <summary>
	/// The current item, or null: any node of the tree (the project, a settings object, a folder, a source, part, probe
	/// or colour scheme), always one of <see cref="Items"/>. Setting an item that is not in the list is a plain selection,
	/// which makes the list that item alone (the tree and the overlays put an item into the list first when they mean to
	/// extend the selection); null empties the list. The list is settled before the change is announced.
	/// </summary>
	public object? Current
	{
		get => _current;
		set
		{
			if (Equals(_current, value))
			{
				return;
			}
			_current = value;
			if (value == null)
			{
				Items.Clear();
			}
			else if (!Items.Contains(value))
			{
				Items.Clear();
				Items.Add(value);
			}
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// The selected items, the current one among them: several only of sources, parts and probes
	/// (<see cref="IsGroupable"/>), which the tree and the overlays add and remove (Ctrl and Shift, the marquee).
	/// </summary>
	public ObservableCollection<object> Items { get; } = [];

	/// <summary>
	/// The newest request to bring something to the user, a fresh object per request (<see cref="Reveal"/>,
	/// <see cref="RevealFolder"/>); null until the first. The tree and the properties pane bind it and act on each change.
	/// </summary>
	public RevealRequest? RevealRequest
	{
		get => _revealRequest;
		private set => SetProperty(ref _revealRequest, value);
	}

	/// <summary>
	/// Makes <paramref name="item"/> the current item (a plain selection unless it is in the list) and brings it to the
	/// user: the tree gives its row the keyboard focus, so the arrows and Del go on from there; with
	/// <paramref name="edit"/> the properties pane then puts the caret into the item's first editor, its name.
	/// </summary>
	public void Reveal(object item, bool edit = false)
	{
		Current = item;
		RevealRequest = new RevealRequest(item, isFolder: false, edit);
	}

	/// <summary>
	/// Brings the folder that shows <paramref name="list"/>, one of the project's lists, to the user. The folder's node is
	/// the tree's own, so the current item follows from the tree's selection.
	/// </summary>
	public void RevealFolder(IEnumerable list) => RevealRequest = new RevealRequest(list, isFolder: true, edit: false);

	/// <summary>Whether an item can be selected together with others: a source, an optical part or a probe.</summary>
	public static bool IsGroupable(object? item) => item is Source or OpticalPart or Probe;

	/// <summary>Every source, part and probe of <paramref name="project"/> in tree order; the range selections and Select all draw on it.</summary>
	public static IEnumerable<object> Groupable(Project project) => project.Sources.Cast<object>().Concat(project.Parts).Concat(project.Probes);

	/// <summary>What a command is about: its parameter, which a row's own menu or button passes, or without one the current item.</summary>
	public object? Target(object? parameter) => parameter ?? _current;

	/// <summary>
	/// What a command acts on: the whole selection when its target (<see cref="Target"/>) is one of several selected
	/// items (Explorer's rule, 2026-10-19), else the target alone. A snapshot, since acting changes the selection.
	/// </summary>
	public object[] Group(object? parameter)
	{
		object? target = Target(parameter);
		if (target == null)
		{
			return [];
		}
		return Items.Count > 1 && Items.Contains(target) ? [.. Items] : [target];
	}

	/// <summary>Makes the selection exactly <paramref name="items"/> and <paramref name="current"/> the current item (a current item outside them is a plain selection, as ever).</summary>
	public void Set(IEnumerable<object> items, object? current)
	{
		object[] snapshot = [.. items];
		Items.Clear();
		foreach (object item in snapshot)
		{
			Items.Add(item);
		}
		Current = current;
	}

	/// <summary>Select all: every source, part and probe of <paramref name="project"/>; the last one becomes the current item.</summary>
	public void SelectAll(Project project)
	{
		object[] all = [.. Groupable(project)];
		Set(all, all.Length > 0 ? all[^1] : null);
	}

	/// <summary>Takes <paramref name="item"/> into the selection and makes it current, or out of it, the current item then moving to the last one left.</summary>
	public void Toggle(object item)
	{
		if (Items.Contains(item))
		{
			Items.Remove(item);
			if (ReferenceEquals(_current, item))
			{
				Current = Items.LastOrDefault();
			}
		}
		else
		{
			Items.Add(item);
			Current = item;
		}
	}
}
