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
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.UI;

/// <summary>
/// The items a project keeps in lists (sources, optical parts, probes, colour schemes) and what the commands do with
/// them (a model of this library since 2026-11-06; the main window's command handlers before): add, duplicate, remove,
/// move, enable. A command names what it acts on by its parameter, which a row's own menu or button passes, or acts
/// on the selection (<see cref="Selection.Target"/>, and <see cref="Selection.Group"/> where several selected items are
/// taken together); afterwards the item the user goes on with is brought to them (<see cref="Selection.Reveal"/>): a
/// new one with the caret in its name, the one that took a removed one's place, the one that was moved. Each operation
/// is complete in itself, one undo step, which the application's own notice of a finished command ends. UI thread only.
/// </summary>
/// <param name="document">The open project.</param>
/// <param name="selection">What the commands act on without a parameter, and where they leave the user.</param>
public sealed class ProjectItems(ProjectDocument document, Selection selection)
{
	private Project Project => document.Project;

	/// <summary>
	/// The list of <paramref name="project"/> that holds items of <paramref name="item"/>'s kind: its sources, parts,
	/// probes or colour schemes; null for anything else (a settings object, a folder, null). The item need not be in it.
	/// </summary>
	public static IList? ListOf(Project project, object? item) => item switch
	{
		Source => project.Sources,
		OpticalPart => project.Parts,
		Probe => project.Probes,
		ColorScheme => project.ColorSchemes,
		_ => null,
	};

	/// <summary>Whether <paramref name="list"/> is one of the open project's four lists of items, which a folder of the tree shows.</summary>
	public bool IsList(object? list) =>
		ReferenceEquals(list, Project.Sources) || ReferenceEquals(list, Project.Parts) || ReferenceEquals(list, Project.Probes) || ReferenceEquals(list, Project.ColorSchemes);

	/// <summary>Appends a new source at (<paramref name="x"/>, <paramref name="y"/>) in canvas pixels, selected, the caret in its name.</summary>
	public Source AddSource(int x, int y) => Added(Project.AddSource(x, y));

	/// <summary>Appends a new optical part of <paramref name="kind"/>, an <see cref="OpticalPart"/> subclass, at (<paramref name="x"/>, <paramref name="y"/>) in canvas pixels, selected, the caret in its name.</summary>
	public OpticalPart AddPart(Type kind, int x, int y) => Added(Project.AddPart(kind, x, y));

	/// <summary>Appends a new probe at (<paramref name="x"/>, <paramref name="y"/>) in canvas pixels, selected, the caret in its name.</summary>
	public Probe AddProbe(int x, int y) => Added(Project.AddProbe(x, y));

	/// <summary>Appends a new colour scheme of the project's own, selected, the caret in its name.</summary>
	public ColorScheme AddColorScheme() => Added(Project.AddColorScheme());

	/// <summary>
	/// Appends a new item to <paramref name="list"/>, one of the project's lists (a folder's plus): a source or a probe
	/// at (<paramref name="x"/>, <paramref name="y"/>) in canvas pixels, or a colour scheme. Null for the parts, whose
	/// kind must be chosen first (<see cref="AddPart"/>), and for anything that is no list of the project.
	/// </summary>
	public object? AddTo(object? list, int x, int y) =>
		ReferenceEquals(list, Project.Sources) ? AddSource(x, y)
		: ReferenceEquals(list, Project.Probes) ? AddProbe(x, y)
		: ReferenceEquals(list, Project.ColorSchemes) ? AddColorScheme()
		: null;

	/// <summary>Whether the command's target is an item of a list: what Duplicate and Remove take.</summary>
	public bool CanEdit(object? parameter) => selection.Target(parameter) is INamed;

	/// <summary>Whether the command's target can be switched on and off: a source, a part or a probe.</summary>
	public bool CanToggleEnabled(object? parameter) => Selection.IsGroupable(selection.Target(parameter));

	/// <summary>Flips a source's or part's Enabled or a probe's Active (the author's point 11, 2026-10-13); each of a selected group flips on its own.</summary>
	public void ToggleEnabled(object? parameter)
	{
		foreach (object item in selection.Group(parameter))
		{
			switch (item)
			{
				case Source source:
					source.Enabled = !source.Enabled;
					break;
				case OpticalPart part:
					part.Enabled = !part.Enabled;
					break;
				case Probe probe:
					probe.Active = !probe.Active;
					break;
			}
		}
	}

	/// <summary>A copy after each original; the copies become the selection, the last one the current item with the caret in its name.</summary>
	public void Duplicate(object? parameter)
	{
		var copies = new List<object>();
		foreach (object item in selection.Group(parameter))
		{
			object? copy = item switch
			{
				Source source => Project.Duplicate(source),
				OpticalPart part => Project.Duplicate(part),
				Probe probe => Project.Duplicate(probe),
				ColorScheme scheme => Project.Duplicate(scheme),
				_ => null,
			};
			if (copy != null)
			{
				copies.Add(copy);
			}
		}
		if (copies.Count == 0)
		{
			return;
		}
		selection.Items.Clear();
		foreach (object copy in copies)
		{
			selection.Items.Add(copy);
		}
		selection.Reveal(copies[^1], edit: true);
	}

	/// <summary>Removes the target, or the whole selection it belongs to; the user is kept in place at the slot of the last one removed, or at the folder of a list left empty.</summary>
	public void Remove(object? parameter)
	{
		object[] group = selection.Group(parameter);
		if (group.Length == 0 || ListOf(Project, group[^1]) is not { } list)
		{
			return;
		}
		int index = list.IndexOf(group[^1]);
		foreach (object item in group)
		{
			selection.Items.Remove(item);
			switch (item)
			{
				case Source source:
					Project.Remove(source);
					break;
				case OpticalPart part:
					Project.Remove(part);
					break;
				case Probe probe:
					Project.Remove(probe);
					break;
				case ColorScheme scheme:
					Project.Remove(scheme);
					break;
			}
		}
		// keep the user in place: the item that took the slot, else the folder
		if (list.Count > 0)
		{
			selection.Reveal(list[Math.Clamp(index, 0, list.Count - 1)]!);
		}
		else
		{
			selection.RevealFolder(list);
		}
	}

	/// <summary>Whether the command's target can change places with its neighbour <paramref name="delta"/> rows on (-1 the one above, +1 the one below).</summary>
	public bool CanMove(object? parameter, int delta)
	{
		object? item = selection.Target(parameter);
		if (item is not INamed || ListOf(Project, item) is not { } list)
		{
			return false;
		}
		int index = list.IndexOf(item);
		return index >= 0 && index + delta >= 0 && index + delta < list.Count;
	}

	/// <summary>Moves the command's target <paramref name="delta"/> rows on within its list (-1 up, +1 down) and keeps the user on it.</summary>
	public void Move(object? parameter, int delta)
	{
		object? item = selection.Target(parameter);
		if (ListOf(Project, item) is not { } list)
		{
			return;
		}
		int index = list.IndexOf(item) + delta;
		bool moved = item switch
		{
			Source source => Project.Move(source, index),
			OpticalPart part => Project.Move(part, index),
			Probe probe => Project.Move(probe, index),
			ColorScheme scheme => Project.Move(scheme, index),
			_ => false,
		};
		if (moved)
		{
			selection.Reveal(item!);
		}
	}

	/// <summary>Selects a newly added item, brings its row to the user and puts the caret into its first editor (the name).</summary>
	private T Added<T>(T item) where T : notnull
	{
		selection.Reveal(item, edit: true);
		return item;
	}
}
