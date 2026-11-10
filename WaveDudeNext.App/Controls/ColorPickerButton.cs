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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// A colour swatch that opens a <see cref="ColorPicker"/> in a popup below it, upstream's <c>ColorPickerButton</c>:
/// the stop rows of the colour scheme editor use it next to the hex box. A toggle button whose checked state is the
/// popup; Space, Enter or Down open it, the focus goes to the picker's square, the colour follows every move at once
/// (the gradient and the preview recolour live, as the hex box does), Enter keeps and closes, Escape puts the colour
/// back as it was and closes, and a click outside closes (the popup's light dismiss consumes that click, so it cannot
/// reopen the popup from the swatch). The control theme in Themes/Editors.axaml names PART_Popup and PART_Picker;
/// the picker's colour is bound to <see cref="Color"/> from here. UI thread only.
/// </summary>
public class ColorPickerButton : ToggleButton
{
	private const string PopupPart = "PART_Popup";
	private const string PickerPart = "PART_Picker";

	/// <summary>Identifies the <see cref="Color"/> property.</summary>
	public static readonly StyledProperty<Rgb> ColorProperty =
		AvaloniaProperty.Register<ColorPickerButton, Rgb>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

	private Popup? _popup;
	private ColorPicker? _picker;
	private Rgb _original;

	/// <summary>The colour shown and edited; bind it two-way to the model's colour.</summary>
	public Rgb Color
	{
		get => GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	/// <inheritdoc />
	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);
		if (_popup != null)
		{
			_popup.Opened -= OnOpened;
			_popup.Closed -= OnClosed;
			_popup.Child?.RemoveHandler(KeyDownEvent, OnPopupKeyDown);
		}
		_popup = e.NameScope.Find<Popup>(PopupPart);
		_picker = e.NameScope.Find<ColorPicker>(PickerPart);
		if (_popup != null)
		{
			_popup.PlacementTarget = this;
			_popup.Placement = PlacementMode.Bottom;
			_popup.IsLightDismissEnabled = true;
			_popup.Opened += OnOpened;
			_popup.Closed += OnClosed;
			// keys inside the popup: tunnelling on its child, so Enter and Escape are seen before the picker's boxes act on them
			_popup.Child?.AddHandler(KeyDownEvent, OnPopupKeyDown, RoutingStrategies.Tunnel);
			_popup.IsOpen = IsChecked == true;
		}
		if (_picker != null)
		{
			_picker[!ColorPicker.ColorProperty] = this[!!ColorProperty];
		}
	}

	/// <inheritdoc />
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == IsCheckedProperty && _popup != null)
		{
			_popup.IsOpen = IsChecked == true;
		}
	}

	private void OnOpened(object? sender, EventArgs e)
	{
		_original = Color;
		Dispatcher.UIThread.Post(() => _picker?.FocusSquare(), DispatcherPriority.Input);
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		SetCurrentValue(IsCheckedProperty, false);
		Focus();
	}

	/// <summary>Enter commits a text box being edited and keeps the colour; Escape puts the original back; both close.</summary>
	private void OnPopupKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Enter:
				if (e.Source is TextBox box)
				{
					BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
				}
				SetCurrentValue(IsCheckedProperty, false);
				e.Handled = true;
				break;
			case Key.Escape:
				SetCurrentValue(ColorProperty, _original);
				SetCurrentValue(IsCheckedProperty, false);
				e.Handled = true;
				break;
		}
	}

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Down && IsChecked != true)
		{
			SetCurrentValue(IsCheckedProperty, true);
			e.Handled = true;
			return;
		}
		base.OnKeyDown(e);
	}
}
