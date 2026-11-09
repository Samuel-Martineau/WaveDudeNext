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

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Views;

/// <summary>
/// Editor for a <see cref="InterferenceFilterPart"/>: the plate's material (its coating on the back alone) and size, and
/// the layer list, the outermost layer first (2026-11-07, the user's request): a row per layer with its thickness and
/// index, buttons to move it up or down and to remove it (in the Tab cycle, the keyboard's way), a grip to drag it to
/// another place (the mouse's way: the row moves as the pointer passes over the others), and below an add button and a
/// button that removes every layer.
/// The data context is the part. UI thread.
/// </summary>
public partial class InterferenceFilterPartView : UserControl
{
	/// <summary>The drag format of a layer row: the layer itself, in this process only; another target does not take it.</summary>
	private const string LayerFormat = "WaveDudeNext.FilterLayer";

	/// <summary>The layer whose grip was pressed, and where, until the pointer moves far enough to start a drag.</summary>
	private FilterLayer? _pressed;
	private Point _pressedAt;

	public InterferenceFilterPartView()
	{
		InitializeComponent();
	}

	/// <summary>The edited list: the filter's layers, or null for another data context.</summary>
	private ObservableCollection<FilterLayer>? Layers => (DataContext as InterferenceFilterPart)?.Layers;

	private void CanAddLayer(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Layers != null;

	/// <summary>Appends a copy of the last layer (a default layer to an empty list), so a stack of alternating layers grows by copying and changing.</summary>
	private void OnAddLayer(object sender, ExecutedRoutedEventArgs e) =>
		Layers?.Add(Layers.LastOrDefault()?.Clone() ?? new FilterLayer());

	private void CanEditLayer(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is FilterLayer layer && Layers?.Contains(layer) == true;

	private void OnRemoveLayer(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is FilterLayer layer)
		{
			Layers?.Remove(layer);
		}
	}

	private void CanRemoveAllLayers(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Layers?.Count > 0;

	/// <summary>Empties the list in one step, so a stack of another design starts from nothing; Undo brings the layers back.</summary>
	private void OnRemoveAllLayers(object sender, ExecutedRoutedEventArgs e) => Layers?.Clear();

	private void CanMoveLayerUp(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is FilterLayer layer && Layers?.IndexOf(layer) > 0;

	private void CanMoveLayerDown(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is FilterLayer layer && Layers is { } layers && layers.IndexOf(layer) is int i && i >= 0 && i < layers.Count - 1;

	private void OnMoveLayerUp(object sender, ExecutedRoutedEventArgs e) => MoveBy(e.Parameter as FilterLayer, -1, "Up");

	private void OnMoveLayerDown(object sender, ExecutedRoutedEventArgs e) => MoveBy(e.Parameter as FilterLayer, 1, "Down");

	/// <summary>
	/// Moves <paramref name="layer"/> <paramref name="delta"/> places and gives the keyboard focus back to the same button
	/// on its row, which the list rebuilt in its new place: a press of the button again moves it on. At either end of the
	/// list, where that button is disabled, the focus goes to the other one.
	/// </summary>
	private void MoveBy(FilterLayer? layer, int delta, string button)
	{
		if (layer == null || Layers is not { } layers)
		{
			return;
		}
		int from = layers.IndexOf(layer);
		int to = from + delta;
		if (from < 0 || to < 0 || to >= layers.Count)
		{
			return;
		}
		layers.Move(from, to);
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
		{
			if (list.ItemContainerGenerator.ContainerFromItem(layer) is DependencyObject row)
			{
				Button? target = Descendants<Button>(row).FirstOrDefault(b => Equals(b.Tag, button) && b.IsEnabled)
					?? Descendants<Button>(row).FirstOrDefault(b => b.Tag is string && b.IsEnabled);
				target?.Focus();
			}
		});
	}

	/// <summary>Every descendant of <paramref name="root"/> of type <typeparamref name="T"/>, depth first.</summary>
	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
			{
				yield return match;
			}
			foreach (T deeper in Descendants<T>(child))
			{
				yield return deeper;
			}
		}
	}

	private void OnGripDown(object sender, MouseButtonEventArgs e)
	{
		_pressed = (sender as FrameworkElement)?.DataContext as FilterLayer;
		_pressedAt = e.GetPosition(this);
	}

	private void OnGripUp(object sender, MouseButtonEventArgs e) => _pressed = null;

	/// <summary>Starts the drag of the pressed row once the pointer has moved the system's drag distance.</summary>
	private void OnGripMove(object sender, MouseEventArgs e)
	{
		if (_pressed is not { } layer || e.LeftButton != MouseButtonState.Pressed)
		{
			_pressed = null;
			return;
		}
		Vector moved = e.GetPosition(this) - _pressedAt;
		if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		_pressed = null;
		DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(LayerFormat, layer), DragDropEffects.Move);
	}

	/// <summary>A dragged layer over another row takes that row's place at once, so the list shows where it will land; anything else dragged here is left to the window (a file it opens).</summary>
	private void OnLayerDragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(LayerFormat) is not FilterLayer dragged || Layers is not { } layers)
		{
			return;
		}
		e.Effects = DragDropEffects.Move;
		e.Handled = true;
		if ((e.OriginalSource as FrameworkElement)?.DataContext is FilterLayer over && !ReferenceEquals(over, dragged))
		{
			int from = layers.IndexOf(dragged);
			int to = layers.IndexOf(over);
			if (from >= 0 && to >= 0)
			{
				layers.Move(from, to);
			}
		}
	}

	/// <summary>The row is already in its place (<see cref="OnLayerDragOver"/>); the drop only ends the drag.</summary>
	private void OnLayerDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(LayerFormat))
		{
			e.Effects = DragDropEffects.Move;
			e.Handled = true;
		}
	}
}
