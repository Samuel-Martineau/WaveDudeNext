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

using System.Collections;
using System.ComponentModel;
using Avalonia;
using Avalonia.Utilities;
using WaveDudeNext.App.Localization;

namespace WaveDudeNext.App;

/// <summary>
/// A folder row of the project tree: one of the project's lists (Sources, Parts, Probes, ColorSchemes) under the
/// string key of its heading (<c>TreeSources</c>, <c>TreeParts</c>, <c>TreeProbes</c>, <c>TreeColorSchemes</c>).
/// Upstream's NamedCollectionConverter made these; here the tree makes them, and the Properties pane's
/// CollectionView shows one as a heading with its count. The <see cref="Label"/> is looked up at call time and
/// announced again when the language changes, so a bound heading follows a language switch (upstream's
/// NamedCollection listened to LanguageController the same way, through a weak subscription, so a folder of a
/// replaced project is not kept alive by the controller).
/// </summary>
public sealed class NamedCollection : INotifyPropertyChanged, IWeakEventSubscriber<PropertyChangedEventArgs>
{
	/// <summary>A folder over <paramref name="items"/> headed by the string <paramref name="key"/>.</summary>
	/// <param name="key">The string table key of the heading.</param>
	/// <param name="items">The project list (an ObservableCollection of Source, OpticalPart, Probe or ColorScheme).</param>
	/// <param name="itemType">The element type of the list.</param>
	public NamedCollection(string key, IList items, Type itemType)
	{
		Key = key;
		Items = items;
		ItemType = itemType;
		WeakEvents.ThreadSafePropertyChanged.Subscribe(LanguageController.Instance, this);
	}

	/// <inheritdoc />
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>The language changed: the heading is read again by whoever shows it.</summary>
	void IWeakEventSubscriber<PropertyChangedEventArgs>.OnEvent(object? sender, WeakEvent ev, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(LanguageController.Current))
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
		}
	}

	/// <summary>The string table key of the heading, also the automation id suffix (<c>Tree:TreeSources</c>).</summary>
	public string Key { get; }

	/// <summary>The project's list.</summary>
	public IList Items { get; }

	/// <summary>The element type of the list: Source, OpticalPart, Probe or ColorScheme.</summary>
	public Type ItemType { get; }

	/// <summary>The heading in the current language, or the key when the table lacks it.</summary>
	public string Label =>
		Application.Current?.TryGetResource(Key, null, out object? text) == true && text is string label ? label : Key;

	/// <inheritdoc />
	public override string ToString() => Label;
}
