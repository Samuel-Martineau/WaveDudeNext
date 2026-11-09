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
//   Mark <mark@pab-ai.com>, author of the Avalonia port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using WaveDudeNext.App.Commands;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Docking;

/// <summary>
/// Visual for one <see cref="DockGroup"/>: title bar with position menu and close button, the
/// active item's content, and a tab strip when the group holds more than one item. The control theme
/// lives in <c>Themes/Docking.axaml</c>; it binds the parts to the read-only properties this control mirrors from the
/// group (<see cref="Title"/>, <see cref="ActiveContent"/>, <see cref="CanCloseActive"/>, <see cref="HasTabs"/>), and
/// its menu items get <see cref="MoveToSideCommand"/> and <see cref="CloseCommand"/> from the code behind, since
/// Avalonia has no routed commands. The data context is left untouched so hosted content inherits the window's.
/// </summary>
public sealed class DockGroupControl : TemplatedControl
{
	/// <summary>Backs <see cref="IsActive"/>.</summary>
	public static readonly StyledProperty<bool> IsActiveProperty =
		AvaloniaProperty.Register<DockGroupControl, bool>(nameof(IsActive));

	/// <summary>Backs <see cref="Title"/>.</summary>
	public static readonly DirectProperty<DockGroupControl, string> TitleProperty =
		AvaloniaProperty.RegisterDirect<DockGroupControl, string>(nameof(Title), o => o.Title);

	/// <summary>Backs <see cref="ActiveContent"/>.</summary>
	public static readonly DirectProperty<DockGroupControl, object?> ActiveContentProperty =
		AvaloniaProperty.RegisterDirect<DockGroupControl, object?>(nameof(ActiveContent), o => o.ActiveContent);

	/// <summary>Backs <see cref="CanCloseActive"/>.</summary>
	public static readonly DirectProperty<DockGroupControl, bool> CanCloseActiveProperty =
		AvaloniaProperty.RegisterDirect<DockGroupControl, bool>(nameof(CanCloseActive), o => o.CanCloseActive);

	/// <summary>Backs <see cref="HasTabs"/>.</summary>
	public static readonly DirectProperty<DockGroupControl, bool> HasTabsProperty =
		AvaloniaProperty.RegisterDirect<DockGroupControl, bool>(nameof(HasTabs), o => o.HasTabs);

	private readonly RelayCommand<DockSide> _moveToSide;
	private readonly RelayCommand _close;
	private string _title = "";
	private object? _activeContent;
	private bool _canCloseActive = true;
	private bool _hasTabs;
	private bool _detached;
	private DockItem? _observedItem;
	private Border? _titleBar;
	private Label? _titleLabel;
	private ListBox? _tabs;
	private ContentPresenter? _content;
	private Button? _menuButton;
	private Button? _closeButton;

	internal DockGroupControl(DockHost host, DockGroup group)
	{
		Host = host;
		Group = group;
		Focusable = false;
		_moveToSide = new RelayCommand<DockSide>(side =>
		{
			if (Group.ActiveItem is { } item)
			{
				Host.DockToEdge(item, side);
			}
		}, _ => Group.ActiveItem != null);
		_close = new RelayCommand(() =>
		{
			if (Group.ActiveItem is { } item)
			{
				Host.Hide(item);
			}
		}, () => Group.ActiveItem is { CanClose: true });
		group.PropertyChanged += OnGroupPropertyChanged;
		group.Items.CollectionChanged += OnItemsChanged;
		Refresh();
	}

	/// <summary>Host this control was built by.</summary>
	public DockHost Host { get; }

	/// <summary>Group this control shows.</summary>
	public DockGroup Group { get; }

	/// <summary>True for the group that last received pointer or keyboard focus; the theme switches the title bar colours on it.</summary>
	public bool IsActive
	{
		get => GetValue(IsActiveProperty);
		set => SetValue(IsActiveProperty, value);
	}

	/// <summary>Title of the active item, shown in the title bar.</summary>
	public string Title
	{
		get => _title;
		private set => SetAndRaise(TitleProperty, ref _title, value);
	}

	/// <summary>Content of the active item, shown in <c>PART_Content</c>; null once <see cref="Detach"/> released it.</summary>
	public object? ActiveContent
	{
		get => _activeContent;
		private set => SetAndRaise(ActiveContentProperty, ref _activeContent, value);
	}

	/// <summary>Whether the active item may be closed; the theme hides the close button otherwise.</summary>
	public bool CanCloseActive
	{
		get => _canCloseActive;
		private set => SetAndRaise(CanCloseActiveProperty, ref _canCloseActive, value);
	}

	/// <summary>True when the group holds more than one item; the theme hides the tab strip otherwise.</summary>
	public bool HasTabs
	{
		get => _hasTabs;
		private set => SetAndRaise(HasTabsProperty, ref _hasTabs, value);
	}

	/// <summary>Moves the active item to the outer edge given as <see cref="DockSide"/> parameter (the position menu's four entries).</summary>
	public ICommand MoveToSideCommand => _moveToSide;

	/// <summary>Hides the active item (the close button and the menu's Close entry); cannot execute when the item has <see cref="DockItem.CanClose"/> off.</summary>
	public ICommand CloseCommand => _close;

	/// <inheritdoc/>
	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);
		Unhook();

		_titleBar = e.NameScope.Find<Border>("PART_TitleBar");
		_titleLabel = e.NameScope.Find<Label>("PART_Title");
		_tabs = e.NameScope.Find<ListBox>("PART_Tabs");
		_content = e.NameScope.Find<ContentPresenter>("PART_Content");
		_menuButton = e.NameScope.Find<Button>("PART_MenuButton");
		_closeButton = e.NameScope.Find<Button>("PART_CloseButton");

		if (_titleBar != null)
		{
			_titleBar.PointerPressed += OnTitleBarPointerPressed;
		}
		if (_tabs != null)
		{
			// tunnelling, as upstream's PreviewMouseLeftButtonDown: the list box handles the press for its selection and would
			// otherwise keep it from the drag
			_tabs.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
			_tabs.ItemsSource = _detached ? null : Group.Items;
			_tabs.SelectedItem = Group.ActiveItem;
			_tabs.SelectionChanged += OnTabSelectionChanged;
		}
		if (_menuButton != null)
		{
			_menuButton.Click += OnMenuButtonClick;
			if (_menuButton.ContextMenu is { } menu)
			{
				// the entries carrying a side move the active item there; the one without a parameter closes it
				foreach (MenuItem entry in menu.Items.OfType<MenuItem>())
				{
					entry.Command = entry.CommandParameter is DockSide ? _moveToSide : _close;
				}
			}
		}
		if (_closeButton != null)
		{
			_closeButton.Command = _close;
		}
		ApplyAutomationId();
	}

	/// <summary>
	/// Releases the hosted content so a new control can take it after a rebuild, and stops following the group: in Avalonia a
	/// control may have one visual parent, so the presenter of a control about to be discarded must let go before the next
	/// one takes the same content.
	/// </summary>
	internal void Detach()
	{
		if (_detached)
		{
			return;
		}
		_detached = true;
		Group.PropertyChanged -= OnGroupPropertyChanged;
		Group.Items.CollectionChanged -= OnItemsChanged;
		if (_observedItem != null)
		{
			_observedItem.PropertyChanged -= OnItemPropertyChanged;
			_observedItem = null;
		}
		if (_tabs != null)
		{
			_tabs.ItemsSource = null;
		}
		ActiveContent = null;
		_content?.UpdateChild();
	}

	private void Unhook()
	{
		if (_titleBar != null)
		{
			_titleBar.PointerPressed -= OnTitleBarPointerPressed;
		}
		if (_tabs != null)
		{
			_tabs.RemoveHandler(PointerPressedEvent, OnTabPointerPressed);
			_tabs.SelectionChanged -= OnTabSelectionChanged;
		}
		if (_menuButton != null)
		{
			_menuButton.Click -= OnMenuButtonClick;
		}
	}

	private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(DockGroup.ActiveItem))
		{
			Refresh();
		}
	}

	private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(DockItem.Title))
		{
			Title = Group.ActiveItem?.Title ?? "";
		}
	}

	/// <summary>Mirrors the group's state into the properties the theme binds, and keeps the tab selection and the commands in step.</summary>
	private void Refresh()
	{
		DockItem? active = Group.ActiveItem;
		if (!ReferenceEquals(_observedItem, active))
		{
			if (_observedItem != null)
			{
				_observedItem.PropertyChanged -= OnItemPropertyChanged;
			}
			_observedItem = active;
			if (active != null)
			{
				active.PropertyChanged += OnItemPropertyChanged;
			}
		}
		Title = active?.Title ?? "";
		CanCloseActive = active?.CanClose ?? false;
		HasTabs = Group.Items.Count > 1;
		ActiveContent = active?.Content;
		if (_tabs != null && !ReferenceEquals(_tabs.SelectedItem, active))
		{
			_tabs.SelectedItem = active;
		}
		ApplyAutomationId();
		_moveToSide.Requery();
		_close.Requery();
	}

	/// <summary>The title is read by the author's UI checks as <c>DockTitle:&lt;pane id&gt;</c>; a Label, not a TextBlock, so it has an automation peer.</summary>
	private void ApplyAutomationId()
	{
		if (_titleLabel != null)
		{
			AutomationProperties.SetAutomationId(_titleLabel, "DockTitle:" + (Group.ActiveItem?.Id ?? ""));
		}
	}

	private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Host.SetActiveGroup(this);
		Host.DragManager.Arm(this, null, e.GetPosition(Host));
	}

	private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_tabs == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		ListBoxItem? container = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
		if (container != null && _tabs.ItemFromContainer(container) is DockItem item)
		{
			Host.DragManager.Arm(this, item, e.GetPosition(Host));
		}
	}

	/// <summary>The list box pushes a tab the user picked into the group; the null it reports while an item leaves is left to the group, which picks the successor itself.</summary>
	private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (!_detached && _tabs?.SelectedItem is DockItem item && Group.Items.Contains(item))
		{
			Group.ActiveItem = item;
		}
	}

	/// <summary>The position menu is a context menu opened by a left click on the title bar button (a right click opens it too, as any context menu).</summary>
	private void OnMenuButtonClick(object? sender, RoutedEventArgs e)
	{
		if (_menuButton?.ContextMenu is { } menu)
		{
			menu.Placement = PlacementMode.Bottom;
			menu.PlacementTarget = _menuButton;
			menu.Open(_menuButton);
		}
	}
}
