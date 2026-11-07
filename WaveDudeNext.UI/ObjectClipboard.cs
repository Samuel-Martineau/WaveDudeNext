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

using NLog;
using WaveDudeNext.Model;

namespace WaveDudeNext.UI;

/// <summary>
/// Cut, Copy and Paste of project objects (2026-10-23, the user's choices; a model of this library since 2026-11-06,
/// the main window's command handlers before). The objects go to the system's clipboard through
/// <see cref="ProjectClipboard"/>, as its format and as the same XML in plain text, which Paste takes back too, from a
/// text editor for instance. A command names what it acts on by its parameter, which a row's own menu passes, or acts
/// on the selection. Paste puts the objects behind the current item and selects them; a settings object pastes onto
/// its own row or the project root. Cut is Copy and Remove, for list items. Each is complete in itself, one undo step.
/// What a focused text box does with the same keys is its own and the application's affair. UI thread only.
/// </summary>
/// <param name="document">The open project.</param>
/// <param name="selection">What the commands act on without a parameter, and what Paste leaves selected.</param>
/// <param name="items">Removes what Cut has copied.</param>
/// <param name="clipboard">The system's clipboard.</param>
public sealed class ObjectClipboard(ProjectDocument document, Selection selection, ProjectItems items, IClipboard clipboard)
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>
	/// The clipboard's objects as last parsed and the clipboard's change count then, so the menus' frequent "can paste"
	/// questions read the clipboard once per change.
	/// </summary>
	private (uint ChangeCount, ProjectClipboard.Clip? Clip)? _cache;

	private Project Project => document.Project;

	/// <summary>Whether Copy has something to take: a copyable object as the command's target, or among the selection it belongs to.</summary>
	public bool CanCopy(object? parameter) => CopyGroup(parameter).Length > 0;

	/// <summary>Whether Cut has something to take: a list item (a source, part, probe or colour scheme).</summary>
	public bool CanCut(object? parameter) => CutGroup(parameter).Length > 0;

	/// <summary>Whether the clipboard holds project objects that can go where the command points.</summary>
	public bool CanPaste(object? parameter) => Clip() is { } clip && clip.CanPaste(Project, selection.Target(parameter));

	/// <summary>Puts the command's objects on the clipboard.</summary>
	public void Copy(object? parameter) => CopyToClipboard(CopyGroup(parameter));

	/// <summary>Puts the command's list items on the clipboard and removes them; nothing is removed when the clipboard refused.</summary>
	public void Cut(object? parameter)
	{
		if (CopyToClipboard(CutGroup(parameter)))
		{
			// the same group: Remove takes the row given, or the selection it belongs to, as Cut did
			items.Remove(parameter);
		}
	}

	/// <summary>Puts the clipboard's objects into the project where the command points and selects them, the last one current.</summary>
	public void Paste(object? parameter)
	{
		// taken, not kept: a paste makes the clip's objects the project's, so the next paste parses the text afresh
		ProjectClipboard.Clip? clip = Clip();
		_cache = null;
		if (clip == null)
		{
			return;
		}
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(Project, clip, selection.Target(parameter));
		if (pasted.Count == 0)
		{
			return;
		}
		Logger.Debug("Pasted {Count} object(s): {Names}", pasted.Count, string.Join(", ", pasted.Select(NameOrKind)));
		selection.Set(pasted.Where(item => item is INamed), pasted[^1]);
	}

	/// <summary>The copyable objects the command acts on: the row given, or the selection it belongs to.</summary>
	private object[] CopyGroup(object? parameter) => [.. selection.Group(parameter).Where(item => ProjectClipboard.CanCopy(Project, item))];

	/// <summary>The list items among them (sources, parts, probes, schemes): what Cut takes away.</summary>
	private object[] CutGroup(object? parameter) => [.. selection.Group(parameter).Where(item => item is INamed)];

	/// <summary>Puts <paramref name="objects"/> on the clipboard in the application's format and as text; false when there are none or the clipboard refused.</summary>
	private bool CopyToClipboard(object[] objects)
	{
		if (objects.Length == 0 || !clipboard.TrySetText(ProjectClipboard.Format, ProjectClipboard.Write(Project, objects)))
		{
			return false;
		}
		Logger.Debug("Copied {Count} object(s): {Names}", objects.Length, string.Join(", ", objects.Select(NameOrKind)));
		return true;
	}

	/// <summary>
	/// The objects on the clipboard, read and parsed once per change of the clipboard; null without any. The
	/// application's own format first, else plain text that starts with an element (a text editor's copy keeps text only).
	/// </summary>
	private ProjectClipboard.Clip? Clip()
	{
		uint count = clipboard.ChangeCount;
		if (count != 0 && _cache is { } cache && cache.ChangeCount == count)
		{
			return cache.Clip;
		}
		if (!clipboard.TryGetText(ProjectClipboard.Format, out string? text))
		{
			// another program holds the clipboard open: nothing is kept, the next question asks again
			return null;
		}
		ProjectClipboard.Clip? clip = text != null && text.AsSpan().TrimStart().StartsWith("<") ? ProjectClipboard.Read(text) : null;
		_cache = (count, clip);
		return clip;
	}

	private static string NameOrKind(object item) => (item as INamed)?.Name ?? item.GetType().Name;
}
