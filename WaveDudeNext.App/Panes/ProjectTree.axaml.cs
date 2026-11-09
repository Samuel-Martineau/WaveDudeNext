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

using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NLog;
using WaveDudeNext.App.Commands;
using WaveDudeNext.App.Controls;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using INamed = WaveDudeNext.Model.INamed;

namespace WaveDudeNext.App.Panes;

/// <summary>
/// The project tree: the <see cref="Project"/> as root with its sections, sources, optical parts, probes and colour
/// schemes beneath (upstream Panes/ProjectTree). Nodes are the model objects themselves; the templates in the XAML decide
/// how each type is shown, and the four lists appear as <see cref="NamedCollection"/> folders this pane makes. Sources,
/// parts and probes can be reordered by dragging a row onto a sibling (a line above or below) or onto their folder
/// (append); a drop of project objects from elsewhere pastes them. Several sources, parts and probes can be selected at
/// once: Ctrl+click or Insert toggles a row, Shift+click or Shift with the arrows selects the range from the anchor (the
/// last plain selection), Ctrl+A everything; the extra rows carry the class <c>inSelection</c> and the automation status
/// "InSelection", the TreeView's own selection stays the current item. Every action runs a member of
/// <see cref="Commands"/> with the row's node as its parameter; the pane itself changes the model only for a drag
/// (<c>Project.Move</c>, <c>ProjectClipboard.Paste</c>) and through the rows' Enabled check boxes. UI thread.
/// </summary>
public partial class ProjectTree : UserControl
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly StyledProperty<Project?> ProjectProperty =
		AvaloniaProperty.Register<ProjectTree, Project?>(nameof(Project));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly StyledProperty<object?> SelectedItemProperty =
		AvaloniaProperty.Register<ProjectTree, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="SelectedItems"/> property.</summary>
	public static readonly StyledProperty<ObservableCollection<object>?> SelectedItemsProperty =
		AvaloniaProperty.Register<ProjectTree, ObservableCollection<object>?>(nameof(SelectedItems));

	/// <summary>Identifies the <see cref="Commands"/> property.</summary>
	public static readonly StyledProperty<AppCommands> CommandsProperty =
		AvaloniaProperty.Register<ProjectTree, AppCommands>(nameof(Commands), new AppCommands());

	/// <summary>The drag format of project objects: upstream's clipboard format name as an application format, the text <see cref="ProjectClipboard.Write"/> makes.</summary>
	internal static readonly DataFormat<string> ObjectsFormat = DataFormat.CreateStringApplicationFormat(ProjectClipboard.Format);

	/// <summary>The folder keys, in tree order, with the drawing each folder shows (the handle glyph of what it holds; the palette for schemes).</summary>
	private static readonly (string Key, string Drawing)[] Folders =
	[
		("TreeSources", "Source"),
		("TreeParts", "Part"),
		("TreeProbes", "Probe"),
		("TreeColorSchemes", "ColorSchemes"),
	];

	/// <summary>
	/// The part kinds of the Add part menu in the WPF head's order (its PartMenuEntries): the optics with the two submenus
	/// among them, Diffraction gratings and Meta surfaces (2026-11-07/08, the user's requests), then the plain shapes, then
	/// the gates of the Optical logic submenu; each with the key of its menu text and the name of its icon drawing (also
	/// its automation id). The eight kinds of 2026-11-07/08 came after the port's version (2026-11-09).
	/// </summary>
	private static readonly (Type Type, string Header, string Drawing)[] OpticKinds =
	[
		(typeof(LensPart), "MenuAddLens", "Lens"),
		(typeof(FresnelLensPart), "MenuAddFresnelLens", "FresnelLens"),
		(typeof(FiberPart), "MenuAddFiber", "Fiber"),
		(typeof(BraggReflectorPart), "MenuAddBraggReflector", "BraggReflector"),
		(typeof(GrinLensPart), "MenuAddGrinLens", "GrinLens"),
		(typeof(ZonePlatePart), "MenuAddZonePlate", "ZonePlate"),
		(typeof(DiffuserPart), "MenuAddDiffuser", "Diffuser"),
		(typeof(InterferenceFilterPart), "MenuAddInterferenceFilter", "InterferenceFilter"),
	];

	private static readonly (Type Type, string Header, string Drawing)[] GratingKinds =
	[
		(typeof(SimpleGratingPart), "MenuAddSimpleGrating", "SimpleGrating"),
		(typeof(FacettedGratingPart), "MenuAddFacettedGrating", "FacettedGrating"),
	];

	private static readonly (Type Type, string Header, string Drawing)[] MetaSurfaceKinds =
	[
		(typeof(MetaLensPart), "MenuAddMetaLens", "MetaLens"),
		(typeof(MetaPrismPart), "MenuAddMetaPrism", "MetaPrism"),
		(typeof(MetaAxiconPart), "MenuAddMetaAxicon", "MetaAxicon"),
	];

	private static readonly (Type Type, string Header, string Drawing)[] ShapeKinds =
	[
		(typeof(PolygonPart), "MenuAddPolygon", "Polygon"),
		(typeof(RectanglePart), "MenuAddRectangle", "Rectangle"),
		(typeof(EllipsePart), "MenuAddEllipse", "Ellipse"),
		(typeof(TrianglePart), "MenuAddTriangle", "Triangle"),
		(typeof(OutlinePart), "MenuAddOutline", "Outline"),
	];

	private static readonly (Type Type, string Header, string Drawing)[] LogicKinds =
	[
		(typeof(XorGatePart), "MenuAddXorGate", "XorGate"),
		(typeof(AndGatePart), "MenuAddAndGate", "AndGate"),
		(typeof(OrGatePart), "MenuAddOrGate", "OrGate"),
	];

	/// <summary>Pixels the pointer must travel with the button down before a row drag starts (the usual system drag distance).</summary>
	private const double DragThreshold = 4.0;

	private const string InSelectionClass = "inSelection";
	private const string InSelectionStatus = "InSelection";
	private const string InactiveClass = "inactive";
	private const string HeaderPart = "PART_LayoutRoot";

	/// <summary>The root's children: Scene, Simulation, Display, then the four folders; rebuilt with the project, an entry replaced when the project swaps a settings object.</summary>
	private readonly ObservableCollection<object> _rootChildren = [];
	private readonly ContextMenu _rowMenu;
	private readonly Separator _probeSeparator;
	private readonly MenuItem _exportProbeCsv;

	private object? _anchor;
	private bool _mirroring;
	/// <summary>The modifiers of the key being handled, for the TreeView's own arrow navigation: Shift extends the range (upstream read Keyboard.Modifiers).</summary>
	private KeyModifiers _keyModifiers;

	private object? _dragCandidate;
	private PointerPressedEventArgs? _dragPress;
	private Point _dragStart;
	/// <summary>Whether a drag started in this tree is under way: its drops reorder, and never paste a copy of themselves.</summary>
	private bool _dragging;
	/// <summary>The list item a drag from this tree carries, which a drop on this tree reorders; null while a settings row is dragged.</summary>
	private object? _dragItem;
	/// <summary>The objects a drag from elsewhere carries, parsed once per drag.</summary>
	private readonly DraggedObjects _dragged = new();
	private InsertionLine? _insertionLine;
	private AdornerLayer? _insertionLayer;

	/// <summary>Creates the pane: the XAML, the root's template, the menus and the handlers of the tree.</summary>
	public ProjectTree()
	{
		InitializeComponent();

		// the root's template is code: its children are this pane's list, which no binding on the Project could reach
		tree.DataTemplates.Insert(0, new FuncTreeDataTemplate<Project>((_, _) => BuildProjectHeader(), _ => _rootChildren));

		_probeSeparator = new Separator();
		_exportProbeCsv = Entry("MenuExportProbeCsv", nameof(AppCommands.ExportProbeCsv), null, "ContextExportProbeCsv");
		_rowMenu = BuildRowMenu();
		addPartButton.Flyout = CreatePartKindsFlyout();

		tree.SelectionChanged += OnTreeSelectionChanged;
		tree.ContainerPrepared += OnContainerPrepared;
		tree.PropertyChanged += OnTreePropertyChanged;
		// before the TreeView and its rows see the key: Space would toggle their selection, Alt+Up would navigate
		tree.AddHandler(KeyDownEvent, OnTreeKeyDownTunnel, RoutingStrategies.Tunnel);
		tree.AddHandler(KeyDownEvent, OnTreeKeyDownBubble, RoutingStrategies.Bubble, handledEventsToo: true);
		// before the row makes a Ctrl+click or Shift+click its plain selection
		tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
		tree.PointerMoved += OnTreePointerMoved;
		tree.PointerReleased += OnTreePointerReleased;
		tree.ContextRequested += OnTreeContextRequested;
		tree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
		tree.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
		tree.AddHandler(DragDrop.DropEvent, OnDrop);
		tree.Classes.Set(InactiveClass, true);
	}

	// ---- properties

	/// <summary>The project shown as the root node; null shows an empty tree.</summary>
	public Project? Project
	{
		get => GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>The model object of the current row, or null. Set from outside, it selects that row without taking the focus (a selection mirrored from the preview).</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>The shared selection (the main window's); the tree adds and removes items on Ctrl, Shift and Insert and marks their rows.</summary>
	public ObservableCollection<object>? SelectedItems
	{
		get => GetValue(SelectedItemsProperty);
		set => SetValue(SelectedItemsProperty, value);
	}

	/// <summary>The commands the rows, the toolbar, the keys and the context menu run: the window's, once it hands them over.</summary>
	public AppCommands Commands
	{
		get => GetValue(CommandsProperty);
		set => SetValue(CommandsProperty, value);
	}

	/// <summary>Raised when the current row changes, by the user or by a selection mirrored from outside.</summary>
	public event EventHandler<SelectedItemChangedEventArgs>? SelectedItemChanged;

	/// <summary>
	/// A binding to the member <paramref name="member"/> of <see cref="Commands"/>, for the menu entries built in code (the
	/// XAML binds <c>#root.Commands.X</c> the same way): it follows the window's wiring and a replaced
	/// <see cref="Commands"/>, since <see cref="AppCommands"/> announces its members.
	/// </summary>
	private Binding CommandBinding(string member) => new($"{nameof(Commands)}.{member}") { Source = this };

	/// <summary>The drawing of a folder row's icon from the folder's key (the XAML's converter).</summary>
	public static IValueConverter FolderIcon { get; } = new FolderIconConverter();

	/// <summary>A colour scheme's stops as the 36 x 10 gradient strip of its row (the XAML's converter).</summary>
	public static IValueConverter SchemeBrush { get; } = new SchemeBrushConverter();

	/// <summary>The TreeView, for the tests.</summary>
	internal TreeView Tree => tree;

	/// <summary>The "+ Part" button with the menu of kinds as its flyout, for the tests.</summary>
	internal Button AddPartButton => addPartButton;

	/// <summary>The rows' context menu, one instance configured for the row it opens on, for the tests.</summary>
	internal ContextMenu RowMenu => _rowMenu;

	// ---- selection API (the window's calls)

	/// <summary>Deselects the current row, if any; the tree then raises its change with null (a click on empty preview space).</summary>
	public void ClearSelection()
	{
		if (tree.SelectedItem != null)
		{
			WhileMirroring(() => tree.SelectedItem = null);
		}
	}

	/// <summary>
	/// Selects the row of a model object, expanding the rows above it; with <paramref name="focus"/> the row also takes the
	/// keyboard focus (not when the selection mirrors a handle on the preview, which keeps the focus). A selection made here
	/// leaves the shared selection as it is. A row the tree has not generated yet is revealed once it has.
	/// </summary>
	public void Select(object item, bool focus = true)
	{
		WhileMirroring(() => tree.SelectedItem = item);
		if (!Reveal(item, focus))
		{
			Dispatcher.UIThread.Post(() => Reveal(item, focus), DispatcherPriority.Loaded);
		}
	}

	/// <summary>Selects the root node; call after setting <see cref="Project"/>.</summary>
	public void SelectRoot()
	{
		if (Project is { } project)
		{
			Select(project);
		}
	}

	/// <summary>Selects the folder row that shows the list of <paramref name="folder"/> (the project's sources or probes, after an add).</summary>
	public void SelectFolder(NamedCollection folder)
	{
		NamedCollection own = _rootChildren.OfType<NamedCollection>().FirstOrDefault(f => ReferenceEquals(f.Items, folder.Items)) ?? folder;
		Select(own);
	}

	/// <summary>Marks the rows of the items in <see cref="SelectedItems"/> again (after the window changed the list wholesale); a row not generated yet is marked once it is.</summary>
	public void Mirror()
	{
		ObservableCollection<object>? set = SelectedItems;
		bool missing = false;
		foreach (Control container in tree.GetRealizedTreeContainers())
		{
			if (container is TreeViewItem row)
			{
				Mark(row, set);
			}
		}
		if (set != null)
		{
			foreach (object item in set)
			{
				missing |= tree.TreeContainerFromItem(item) == null;
			}
		}
		if (missing)
		{
			Dispatcher.UIThread.Post(() =>
			{
				foreach (Control container in tree.GetRealizedTreeContainers())
				{
					if (container is TreeViewItem row)
					{
						Mark(row, SelectedItems);
					}
				}
			}, DispatcherPriority.Loaded);
		}
	}

	private static void Mark(TreeViewItem row, ObservableCollection<object>? set)
	{
		bool inSelection = set != null && row.DataContext is { } node && set.Contains(node);
		row.Classes.Set(InSelectionClass, inSelection);
		if (inSelection)
		{
			AutomationProperties.SetItemStatus(row, InSelectionStatus);
		}
		else
		{
			row.ClearValue(AutomationProperties.ItemStatusProperty);
		}
	}

	private bool Reveal(object item, bool focus)
	{
		if (tree.TreeContainerFromItem(item) is not TreeViewItem container)
		{
			return false;
		}
		for (TreeViewItem? parent = container.Parent as TreeViewItem; parent != null; parent = parent.Parent as TreeViewItem)
		{
			parent.IsExpanded = true;
		}
		if (focus)
		{
			container.Focus();
		}
		return true;
	}

	private void WhileMirroring(Action action)
	{
		_mirroring = true;
		try
		{
			action();
		}
		finally
		{
			_mirroring = false;
		}
	}

	// ---- property changes

	/// <inheritdoc />
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == ProjectProperty)
		{
			OnProjectChanged(change.GetOldValue<Project?>(), change.GetNewValue<Project?>());
		}
		else if (change.Property == SelectedItemsProperty)
		{
			if (change.GetOldValue<ObservableCollection<object>?>() is { } old)
			{
				old.CollectionChanged -= OnSelectedItemsChanged;
			}
			if (change.GetNewValue<ObservableCollection<object>?>() is { } set)
			{
				set.CollectionChanged += OnSelectedItemsChanged;
			}
			Mirror();
		}
		else if (change.Property == SelectedItemProperty)
		{
			// set from outside (the window's two-way binding): mirror it without taking the focus
			object? item = change.GetNewValue<object?>();
			if (!_mirroring && !ReferenceEquals(tree.SelectedItem, item))
			{
				if (item == null)
				{
					ClearSelection();
				}
				else
				{
					Select(item, focus: false);
				}
			}
		}
	}

	private void OnProjectChanged(Project? old, Project? project)
	{
		if (old != null)
		{
			old.PropertyChanged -= OnProjectPropertyChanged;
		}
		_rootChildren.Clear();
		if (project != null)
		{
			_rootChildren.Add(project.Scene);
			_rootChildren.Add(project.Simulation);
			_rootChildren.Add(project.Display);
			_rootChildren.Add(new NamedCollection(Folders[0].Key, project.Sources, typeof(Source)));
			_rootChildren.Add(new NamedCollection(Folders[1].Key, project.Parts, typeof(OpticalPart)));
			_rootChildren.Add(new NamedCollection(Folders[2].Key, project.Probes, typeof(Probe)));
			_rootChildren.Add(new NamedCollection(Folders[3].Key, project.ColorSchemes, typeof(ColorScheme)));
			project.PropertyChanged += OnProjectPropertyChanged;
		}
		_anchor = null;
		tree.ItemsSource = project == null ? Array.Empty<object>() : new object[] { project };
	}

	/// <summary>A paste of Scene, Simulation or Display settings replaces the project's object: the row follows (upstream's MultiBinding did the same).</summary>
	private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (Project is not { } project || _rootChildren.Count < 3)
		{
			return;
		}
		switch (e.PropertyName)
		{
			case nameof(Model.Project.Scene):
				_rootChildren[0] = project.Scene;
				break;
			case nameof(Model.Project.Simulation):
				_rootChildren[1] = project.Simulation;
				break;
			case nameof(Model.Project.Display):
				_rootChildren[2] = project.Display;
				break;
		}
	}

	private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Mirror();

	private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
	{
		if (e.Container is TreeViewItem row)
		{
			Mark(row, SelectedItems);
		}
	}

	/// <summary>The selected row shows the inactive colours while the keyboard focus is elsewhere (upstream's IsSelectionActive).</summary>
	private void OnTreePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == IsKeyboardFocusWithinProperty)
		{
			tree.Classes.Set(InactiveClass, !tree.IsKeyboardFocusWithin);
		}
	}

	// ---- selection

	/// <summary>
	/// The TreeView's selection changed: by the user (a click, the arrows) the shared selection becomes that item alone,
	/// or with Shift held the range from the anchor to it; mirrored from elsewhere it is left alone. Then the window hears.
	/// </summary>
	private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		object? item = tree.SelectedItem;
		if (!_mirroring && SelectedItems is { } set && item is Source or OpticalPart or Probe)
		{
			if (_keyModifiers.HasFlag(KeyModifiers.Shift) && _anchor != null && !ReferenceEquals(_anchor, item))
			{
				SelectRange(_anchor, item);
			}
			else
			{
				set.Clear();
				set.Add(item);
				_anchor = item;
			}
		}
		object? old = SelectedItem;
		SetCurrentValue(SelectedItemProperty, item);
		SelectedItemChanged?.Invoke(this, new SelectedItemChangedEventArgs(old, item));
	}

	/// <summary>Every source, part and probe in tree order.</summary>
	private IEnumerable<object> Selectable() =>
		Project is { } project ? project.Sources.Cast<object>().Concat(project.Parts).Concat(project.Probes) : [];

	/// <summary>Makes the selection the items from <paramref name="from"/> to <paramref name="to"/> in tree order, either way round.</summary>
	private void SelectRange(object from, object to)
	{
		if (SelectedItems is not { } set)
		{
			return;
		}
		object[] all = [.. Selectable()];
		int a = Array.IndexOf(all, from), b = Array.IndexOf(all, to);
		if (a < 0 || b < 0)
		{
			return;
		}
		set.Clear();
		for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
		{
			set.Add(all[i]);
		}
	}

	/// <summary>Ctrl+click toggles the row; Shift+click selects the range from the anchor. Both make the row current without the TreeView's own plain selection.</summary>
	private bool TryModifiedClick(object item, KeyModifiers modifiers)
	{
		if (SelectedItems is not { } set || item is not (Source or OpticalPart or Probe))
		{
			return false;
		}
		if (modifiers.HasFlag(KeyModifiers.Control))
		{
			if (set.Contains(item))
			{
				set.Remove(item);
				if (set.LastOrDefault() is { } current)
				{
					WhileMirroring(() => tree.SelectedItem = current);
					Reveal(current, focus: true);
				}
				else
				{
					ClearSelection();
				}
			}
			else
			{
				set.Add(item);
				_anchor = item;
				WhileMirroring(() => tree.SelectedItem = item);
				Reveal(item, focus: true);
			}
			return true;
		}
		if (modifiers.HasFlag(KeyModifiers.Shift))
		{
			SelectRange(_anchor ?? tree.SelectedItem ?? item, item);
			WhileMirroring(() => tree.SelectedItem = item);
			Reveal(item, focus: true);
			return true;
		}
		return false;
	}

	// ---- keys, only while the tree has the focus

	/// <summary>
	/// The keys of the manual's table act on the focused row (Delete, Space, Insert, Ctrl+Space, Ctrl+A, Ctrl+D, Alt+Up,
	/// Alt+Down), before the TreeView sees them. Home and End go to the first and last visible row, which Avalonia's
	/// TreeView leaves to its owner (its navigation knows Up, Down and Right); the ColorPickerCheck reaches the scheme row
	/// with End. Shift with the arrows extends the range through the TreeView's own selection change (OnTreeSelectionChanged).
	/// </summary>
	private void OnTreeKeyDownTunnel(object? sender, KeyEventArgs e)
	{
		_keyModifiers = e.KeyModifiers;
		TreeViewItem? row = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
		object? node = row?.DataContext ?? tree.SelectedItem;
		ICommand? command = (e.Key, e.KeyModifiers) switch
		{
			(Key.Delete, KeyModifiers.None) => Commands.Remove,
			(Key.Space, KeyModifiers.None) => Commands.ToggleEnabled,
			(Key.Insert, KeyModifiers.None) or (Key.Space, KeyModifiers.Control) => Commands.ToggleSelected,
			(Key.A, KeyModifiers.Control) => Commands.SelectAll,
			(Key.D, KeyModifiers.Control) => Commands.Duplicate,
			(Key.Up, KeyModifiers.Alt) => Commands.MoveUp,
			(Key.Down, KeyModifiers.Alt) => Commands.MoveDown,
			_ => null,
		};
		if (command != null)
		{
			object? parameter = ReferenceEquals(command, Commands.SelectAll) ? null : node;
			if (command.CanExecute(parameter))
			{
				command.Execute(parameter);
			}
			e.Handled = true;
			return;
		}
		if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Home or Key.End)
		{
			TreeViewItem? target = e.Key == Key.Home ? VisibleRows(tree).FirstOrDefault() : VisibleRows(tree).LastOrDefault();
			if (target != null)
			{
				// a directional focus makes the TreeView select the row too
				target.Focus(NavigationMethod.Directional);
				e.Handled = true;
			}
		}
	}

	private void OnTreeKeyDownBubble(object? sender, KeyEventArgs e) => _keyModifiers = KeyModifiers.None;

	/// <summary>The rows in tree order, those under a collapsed row left out.</summary>
	private static IEnumerable<TreeViewItem> VisibleRows(ItemsControl parent)
	{
		for (int i = 0; i < parent.ItemCount; i++)
		{
			if (parent.ContainerFromIndex(i) is TreeViewItem row)
			{
				yield return row;
				if (row.IsExpanded)
				{
					foreach (TreeViewItem child in VisibleRows(row))
					{
						yield return child;
					}
				}
			}
		}
	}

	// ---- the context menu and the menu of kinds

	private ContextMenu BuildRowMenu()
	{
		var menu = new ContextMenu();
		menu.Items.Add(Entry("MenuProjectAddSource", nameof(AppCommands.AddSource)));
		MenuItem addPart = Entry("MenuProjectAddPart", null);
		foreach (Control entry in PartKindEntries())
		{
			addPart.Items.Add(entry);
		}
		menu.Items.Add(addPart);
		menu.Items.Add(Entry("MenuProjectAddProbe", nameof(AppCommands.AddProbe)));
		menu.Items.Add(new Separator());
		menu.Items.Add(Entry("MenuProjectToggleSelected", nameof(AppCommands.ToggleSelected), "Insert", "ContextToggleSelected"));
		menu.Items.Add(Entry("MenuProjectSelectAll", nameof(AppCommands.SelectAll), "Ctrl+A", "ContextSelectAll"));
		menu.Items.Add(new Separator());
		menu.Items.Add(Entry("MenuProjectToggleEnabled", nameof(AppCommands.ToggleEnabled), "Space"));
		menu.Items.Add(Entry("MenuProjectDuplicate", nameof(AppCommands.Duplicate), "Ctrl+D"));
		menu.Items.Add(Entry("MenuEditCut", nameof(AppCommands.Cut), "Ctrl+X"));
		menu.Items.Add(Entry("MenuEditCopy", nameof(AppCommands.Copy), "Ctrl+C"));
		menu.Items.Add(Entry("MenuEditPaste", nameof(AppCommands.Paste), "Ctrl+V"));
		menu.Items.Add(Entry("MenuProjectRemove", nameof(AppCommands.Remove), "Delete"));
		menu.Items.Add(new Separator());
		menu.Items.Add(Entry("MenuProjectMoveUp", nameof(AppCommands.MoveUp), "Alt+Up"));
		menu.Items.Add(Entry("MenuProjectMoveDown", nameof(AppCommands.MoveDown), "Alt+Down"));
		// a probe's recording as CSV: only a probe row shows the entry, last
		menu.Items.Add(_probeSeparator);
		menu.Items.Add(_exportProbeCsv);
		return menu;
	}

	/// <summary>A menu entry: the text from the string table, following a language switch; the gesture shown as text, not bound (the tree's keys do that).</summary>
	private MenuItem Entry(string headerKey, string? member, string? gesture = null, string? automationId = null)
	{
		var item = new MenuItem();
		item[!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension(headerKey);
		if (member != null)
		{
			item.Bind(MenuItem.CommandProperty, CommandBinding(member));
		}
		if (gesture != null)
		{
			item.InputGesture = KeyGesture.Parse(gesture);
		}
		if (automationId != null)
		{
			AutomationProperties.SetAutomationId(item, automationId);
		}
		return item;
	}

	/// <summary>The menu of part kinds for an add button (the "+ Part" button, the Parts folder's plus): one instance per button, as upstream.</summary>
	private MenuFlyout CreatePartKindsFlyout()
	{
		// under the button, left-aligned with it, as the WPF head's context menu drops; Avalonia's default centres a flyout
		// under its button (2026-11-09, the user's report)
		var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
		foreach (Control entry in PartKindEntries())
		{
			flyout.Items.Add(entry);
		}
		return flyout;
	}

	/// <summary>
	/// The part entries, the optics first and the plain shapes after a separator, then the Optical logic submenu with the
	/// three gates; each runs <see cref="AppCommands.AddPart"/> with the class as the parameter, shows the kind's icon and, as its
	/// tooltip, the kind's description (the Kind.&lt;Type&gt; string a part's form shows too). A fresh set per call: a
	/// control has one parent.
	/// </summary>
	private IEnumerable<Control> PartKindEntries()
	{
		foreach ((Type, string, string) kind in OpticKinds)
		{
			yield return KindEntry(kind);
		}
		yield return Submenu("MenuAddDiffractionGratings", "DiffractionGratings", GratingKinds);
		yield return Submenu("MenuAddMetaSurfaces", "MetaSurfaces", MetaSurfaceKinds);
		yield return new Separator();
		foreach ((Type, string, string) kind in ShapeKinds)
		{
			yield return KindEntry(kind);
		}
		yield return new Separator();
		yield return Submenu("MenuAddOpticalLogic", "OpticalLogic", LogicKinds);
	}

	/// <summary>A submenu of kinds: its header's text by key, its icon drawing (also its automation id), one entry per kind.</summary>
	private MenuItem Submenu(string header, string drawing, (Type Type, string Header, string Drawing)[] kinds)
	{
		var menu = new MenuItem { Icon = new IconView { Drawing = drawing } };
		menu[!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension(header);
		AutomationProperties.SetAutomationId(menu, "AddPart:" + drawing);
		foreach ((Type, string, string) kind in kinds)
		{
			menu.Items.Add(KindEntry(kind));
		}
		return menu;
	}

	private MenuItem KindEntry((Type Type, string Header, string Drawing) kind)
	{
		var item = new MenuItem
		{
			Icon = new IconView { Drawing = kind.Drawing },
			CommandParameter = kind.Type,
			// the Type marks a kind entry: the context menu passes its row's node to every other entry
			Tag = kind.Type,
		};
		item.Bind(MenuItem.CommandProperty, CommandBinding(nameof(AppCommands.AddPart)));
		item[!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension(kind.Header);
		item[!ToolTip.TipProperty] = new DynamicResourceExtension("Kind." + kind.Type.Name);
		AutomationProperties.SetAutomationId(item, "AddPart:" + kind.Drawing);
		return item;
	}

	/// <summary>A folder's plus button, once it is in place: the Parts folder's opens the menu of kinds, the others run Add with their folder.</summary>
	private void OnFolderAddLoaded(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button { DataContext: NamedCollection folder } button)
		{
			return;
		}
		if (folder.Key == Folders[1].Key)
		{
			button.Flyout ??= CreatePartKindsFlyout();
			button.ClearValue(Button.CommandProperty);
		}
		else
		{
			button.Flyout = null;
			button.Bind(Button.CommandProperty, CommandBinding(nameof(AppCommands.Add)));
			button.CommandParameter = folder;
		}
	}

	/// <summary>A right-click, the Apps key or Shift+F10 on a row: the row's context menu, its entries with the row's node.</summary>
	private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
	{
		if (e.Source is not Visual origin || origin.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { } row)
		{
			return;
		}
		OpenRowMenu(row, atPointer: e.TryGetPosition(row, out _));
		e.Handled = true;
	}

	/// <summary>Opens the context menu on <paramref name="row"/>: at the pointer, or below the row's header for the keyboard.</summary>
	internal void OpenRowMenu(TreeViewItem row, bool atPointer = false)
	{
		object? node = row.DataContext;
		bool probe = node is Probe;
		_probeSeparator.IsVisible = probe;
		_exportProbeCsv.IsVisible = probe;
		foreach (MenuItem item in Entries(_rowMenu.Items))
		{
			if (item.Tag is not Type)
			{
				item.CommandParameter = node;
			}
		}
		_rowMenu.Placement = atPointer ? PlacementMode.Pointer : PlacementMode.Bottom;
		_rowMenu.Open(HeaderOf(row) ?? row);
	}

	/// <summary>Every entry of a menu, submenus included, in order.</summary>
	internal static IEnumerable<MenuItem> Entries(IEnumerable items)
	{
		foreach (object entry in items)
		{
			if (entry is MenuItem item)
			{
				yield return item;
				foreach (MenuItem child in Entries(item.Items))
				{
					yield return child;
				}
			}
		}
	}

	private static Control BuildProjectHeader()
	{
		var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
		text[!TextBlock.TextProperty] = new DynamicResourceExtension("TreeProject");
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Children =
			{
				new IconView { Drawing = "Project", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center },
				text,
			},
		};
	}

	// ---- drag and drop: reordering within the tree, copying anywhere else
	// A drag carries the item as Copy writes it (the application's format and plain text) and nothing else; the item
	// itself stays in a field here, so a drop on this tree reorders it within its list, while any other target takes a
	// copy: another window's tree as Paste would, a picture at the drop pixel, a text editor as XML. A drop of objects on
	// this tree is placed by the paste logic: before or after a row of a list (by the half of the row), at the end of a
	// folder's list, or onto Scene, Simulation, Display or the project root.

	private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
	{
		_dragCandidate = null;
		_dragPress = null;
		if (e.Source is not Visual origin || !e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
		{
			return;
		}
		if (origin.FindAncestorOfType<Button>(includeSelf: true) != null)
		{
			return; // row buttons, check boxes and the expander are clicks, not drags
		}
		if (origin.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { DataContext: { } node } || Project is not { } project
			|| !ProjectClipboard.CanCopy(project, node))
		{
			return;
		}
		if (node is INamed && TryModifiedClick(node, e.KeyModifiers))
		{
			e.Handled = true;   // the TreeView would make the row its plain selection
			return;
		}
		_dragCandidate = node;
		_dragPress = e;
		_dragStart = e.GetPosition(tree);
	}

	private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		_dragCandidate = null;
		_dragPress = null;
	}

	private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
	{
		if (_dragCandidate is not { } item || _dragPress is not { } press || Project is not { } project
			|| !e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Point moved = e.GetPosition(tree) - _dragStart;
		if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
		{
			return;
		}
		_dragCandidate = null;
		_dragPress = null;
		string text = ProjectClipboard.Write(project, [item]);
		var payload = new DataTransferItem();
		payload.Set(ObjectsFormat, text);
		payload.SetText(text);
		var transfer = new DataTransfer();
		transfer.Add(payload);
		_dragItem = ListOf(item) != null ? item : null;
		_dragging = true;
		try
		{
			// the platform disposes the transfer when the drag ends (its contract); a drag the platform cannot run is
			// logged and dropped, the tree stays usable
			await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move | DragDropEffects.Copy);
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
		{
			Logger.Warn(ex, "The row drag could not start");
		}
		finally
		{
			_dragging = false;
			_dragItem = null;
			_dragged.Clear();
			ClearInsertionLine();
		}
	}

	/// <summary>
	/// The tree claims a drag only when it takes it (an own row to reorder, project objects to paste); anything else, a
	/// file above all, is left unhandled for the window, which opens a dropped file and refuses other data.
	/// </summary>
	private void OnDragOver(object? sender, DragEventArgs e)
	{
		ClearInsertionLine();
		Point position = e.GetPosition(tree);
		if (_dragItem is { } item)
		{
			e.Handled = true;
			e.DragEffects = DragDropEffects.None;
			if (FindDropTarget(item, position) is { } target)
			{
				e.DragEffects = DragDropEffects.Move;
				ShowInsertionLine(target.Header, target.Placement);
			}
			return;
		}
		if (!_dragging && _dragged.Clip(e.DataTransfer) is { } clip && FindPasteTarget(clip, position) is { } paste)
		{
			e.Handled = true;
			e.DragEffects = DragDropEffects.Copy;
			ShowInsertionLine(paste.Header, paste.Placement);
		}
	}

	private void OnDragLeave(object? sender, DragEventArgs e) => ClearInsertionLine();

	private void OnDrop(object? sender, DragEventArgs e)
	{
		ClearInsertionLine();
		Point position = e.GetPosition(tree);
		if (_dragItem is { } item)
		{
			e.Handled = true;
			if (Project is not { } own || FindDropTarget(item, position) is not { } target)
			{
				return;
			}
			bool moved = item switch
			{
				Source source => own.Move(source, target.Index),
				OpticalPart part => own.Move(part, target.Index),
				Probe probe => own.Move(probe, target.Index),
				ColorScheme scheme => own.Move(scheme, target.Index),
				_ => false,
			};
			if (moved)
			{
				Select(item);
			}
			return;
		}
		// taken, not kept: the paste makes the clip's objects the project's. A drag the tree does not take (a file) stays
		// unhandled for the window
		if (_dragging || Project is not { } project || _dragged.Take(e.DataTransfer) is not { } clip
			|| FindPasteTarget(clip, position) is not { } paste)
		{
			return;
		}
		e.Handled = true;
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(project, clip, paste.Current, paste.Before);
		if (pasted.Count == 0)
		{
			return;
		}
		Logger.Debug("Dropped {Count} object(s) into the tree", pasted.Count);
		if (SelectedItems is { } selection)
		{
			selection.Clear();
			foreach (object added in pasted.Where(added => added is INamed))
			{
				selection.Add(added);
			}
		}
		Select(pasted[^1]);
	}

	/// <summary>Where a drop of objects would go, as the paste logic takes it: the current item it is placed by, and before it or not.</summary>
	private sealed record PasteTarget(Control Header, InsertionPlacement Placement, object? Current, bool Before);

	/// <summary>
	/// Resolves a drop of <paramref name="clip"/> from elsewhere: on a list item before or after it (by the half of the
	/// row), on a folder at the end of its list, on Scene, Simulation, Display or the project root as a paste onto them.
	/// Null where the clip has nothing to give (settings onto a list row without list items, for instance).
	/// </summary>
	private PasteTarget? FindPasteTarget(ProjectClipboard.Clip clip, Point position)
	{
		if (Project is not { } project || RowAt(position) is not { } row || HeaderOf(row) is not { } header)
		{
			return null;
		}
		switch (row.DataContext)
		{
			case Source or OpticalPart or Probe or ColorScheme:
				bool before = IsUpperHalf(header, position);
				return clip.CanPaste(project, row.DataContext)
					? new PasteTarget(header, before ? InsertionPlacement.Before : InsertionPlacement.After, row.DataContext, before)
					: null;
			case NamedCollection:
				// the folder's list takes its items at the end, as do the others
				return clip.HasItems ? new PasteTarget(header, InsertionPlacement.Into, null, false) : null;
			case { } other when clip.CanPaste(project, other):
				return new PasteTarget(header, InsertionPlacement.Into, other, false);
			default:
				return null;
		}
	}

	/// <summary>Row under the pointer, where a drop would put the item, and how to show it.</summary>
	private sealed record DropTarget(Control Header, InsertionPlacement Placement, int Index);

	/// <summary>
	/// Resolves a drop position: onto a sibling of the dragged item (before or after, by the upper or lower half of its
	/// row) or onto the item's own folder (append). Null when the position is not a valid target or would not move anything.
	/// </summary>
	private DropTarget? FindDropTarget(object item, Point position)
	{
		IList? list = ListOf(item);
		if (list == null || RowAt(position) is not { } row || HeaderOf(row) is not { } header)
		{
			return null;
		}
		int current = list.IndexOf(item);
		if (row.DataContext is NamedCollection folder && ReferenceEquals(folder.Items, list))
		{
			int last = list.Count - 1;
			return current == last ? null : new DropTarget(header, InsertionPlacement.Into, last);
		}
		if (ReferenceEquals(row.DataContext, item) || ListOf(row.DataContext) != list)
		{
			return null;
		}
		int targetIndex = list.IndexOf(row.DataContext);
		bool before = IsUpperHalf(header, position);
		int insert = targetIndex + (before ? 0 : 1);           // slot in the list as it is now
		int final = insert - (current < insert ? 1 : 0);       // index after the item left its slot
		return final == current ? null : new DropTarget(header, before ? InsertionPlacement.Before : InsertionPlacement.After, final);
	}

	private IList? ListOf(object? node) => node switch
	{
		Source => Project?.Sources,
		OpticalPart => Project?.Parts,
		Probe => Project?.Probes,
		ColorScheme => Project?.ColorSchemes,
		_ => null,
	};

	private TreeViewItem? RowAt(Point position) =>
		tree.GetVisualAt(position)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);

	/// <summary>The row's header line (the template's layout root), the element a drop is judged against and the insertion line drawn over.</summary>
	private static Control? HeaderOf(TreeViewItem row) =>
		row.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == HeaderPart && ReferenceEquals(border.TemplatedParent, row));

	private bool IsUpperHalf(Control header, Point position) =>
		header.TranslatePoint(new Point(0, header.Bounds.Height / 2), tree) is { } middle && position.Y < middle.Y;

	private void ShowInsertionLine(Control header, InsertionPlacement placement)
	{
		if (AdornerLayer.GetAdornerLayer(header) is not { } layer
			|| !this.TryFindResource("Accent", ActualThemeVariant, out object? value) || value is not IBrush brush)
		{
			return;
		}
		_insertionLine = new InsertionLine(brush, placement);
		_insertionLayer = layer;
		AdornerLayer.SetAdornedElement(_insertionLine, header);
		AdornerLayer.SetIsClipEnabled(_insertionLine, false);
		layer.Children.Add(_insertionLine);
	}

	private void ClearInsertionLine()
	{
		if (_insertionLine != null)
		{
			_insertionLayer?.Children.Remove(_insertionLine);
			_insertionLine = null;
			_insertionLayer = null;
		}
	}

	/// <summary>Position of the drop feedback relative to the row.</summary>
	private enum InsertionPlacement
	{
		/// <summary>A line along the top edge: insert before this item.</summary>
		Before,

		/// <summary>A line along the bottom edge: insert after this item.</summary>
		After,

		/// <summary>An outline: append to this folder, or paste onto this node.</summary>
		Into,
	}

	/// <summary>Drop feedback over a row's header (upstream's InsertionAdorner): a 2 px accent line above or below it, or an outline around it.</summary>
	private sealed class InsertionLine : Control
	{
		private readonly IPen _pen;
		private readonly InsertionPlacement _placement;

		public InsertionLine(IBrush brush, InsertionPlacement placement)
		{
			_pen = new Pen(brush, 2);
			_placement = placement;
			IsHitTestVisible = false;
		}

		public override void Render(DrawingContext context)
		{
			Size size = Bounds.Size;
			switch (_placement)
			{
				case InsertionPlacement.Before:
					context.DrawLine(_pen, new Point(0, 1), new Point(size.Width, 1));
					break;
				case InsertionPlacement.After:
					context.DrawLine(_pen, new Point(0, size.Height - 1), new Point(size.Width, size.Height - 1));
					break;
				default:
					context.DrawRectangle(null, _pen, new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2)));
					break;
			}
		}
	}

	/// <summary>
	/// Project objects carried by a drag (upstream's ObjectsData): the text <see cref="ProjectClipboard.Write"/> makes, in
	/// the application's format or as plain text that may hold it (copied back from a text editor, which keeps text only;
	/// <see cref="ProjectClipboard.Read"/> decides, and text not starting with an element is not even parsed). An instance
	/// parses each text once, for the many drag-over events of one drag.
	/// </summary>
	private sealed class DraggedObjects
	{
		private (string Text, ProjectClipboard.Clip? Clip)? _cache;

		/// <summary>The objects <paramref name="data"/> carries, parsed once per text; null without any.</summary>
		public ProjectClipboard.Clip? Clip(IDataTransfer data)
		{
			if (Text(data) is not { } text)
			{
				return null;
			}
			if (_cache is not { } cache || cache.Text != text)
			{
				_cache = cache = (text, ProjectClipboard.Read(text));
			}
			return cache.Clip;
		}

		/// <summary>The objects as <see cref="Clip"/> gives them, then forgotten: a paste makes them the project's, so the same text is parsed afresh next time.</summary>
		public ProjectClipboard.Clip? Take(IDataTransfer data)
		{
			ProjectClipboard.Clip? clip = Clip(data);
			_cache = null;
			return clip;
		}

		/// <summary>Forgets the parsed text.</summary>
		public void Clear() => _cache = null;

		/// <summary>The objects' text in <paramref name="data"/>: the application's format, else plain text that starts with an element; null for neither (and when the data cannot be read).</summary>
		private static string? Text(IDataTransfer data)
		{
			try
			{
				if (data.Contains(ObjectsFormat))
				{
					return data.TryGetValue(ObjectsFormat);
				}
				return data.TryGetText() is { } text && text.TrimStart().StartsWith('<') ? text : null;
			}
			catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
			{
				// a drag source that cannot deliver its data is a drag of nothing for the tree
				Logger.Debug(ex, "The dragged data could not be read");
				return null;
			}
		}
	}

	/// <summary>The drawing of a folder row's icon from its key: the handle glyph of what it holds, the palette for the colour schemes.</summary>
	private sealed class FolderIconConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			value is string key ? Folders.FirstOrDefault(folder => folder.Key == key).Drawing : null;

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			throw new NotSupportedException();
	}

	/// <summary>
	/// A colour scheme's stop list as a horizontal gradient brush (upstream's ColorSchemeBrushConverter for the tree's
	/// strip): every stop becomes a gradient stop at its position when there are up to 64, else 64 samples of the
	/// gradient. Null or no stops give a transparent brush. The brush is immutable.
	/// </summary>
	private sealed class SchemeBrushConverter : IValueConverter
	{
		private const int MaxStops = 64;

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is not IEnumerable<ColorStop> stops)
			{
				return Brushes.Transparent;
			}
			List<ColorStop> ordered = [.. stops.OrderBy(stop => stop.Position)];
			if (ordered.Count == 0)
			{
				return Brushes.Transparent;
			}
			var gradient = new GradientStops();
			if (ordered.Count <= MaxStops)
			{
				foreach (ColorStop stop in ordered)
				{
					gradient.Add(new GradientStop(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), Math.Clamp(stop.Position, 0.0, 1.0)));
				}
			}
			else
			{
				Rgb[] samples = ColorScheme.Interpolate(ordered, MaxStops);
				for (int i = 0; i < samples.Length; i++)
				{
					gradient.Add(new GradientStop(Color.FromRgb(samples[i].R, samples[i].G, samples[i].B), i / (double)(samples.Length - 1)));
				}
			}
			var brush = new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
				GradientStops = gradient,
			};
			return brush.ToImmutable();
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			throw new NotSupportedException();
	}
}

/// <summary>The arguments of <see cref="ProjectTree.SelectedItemChanged"/>: the node that was current and the one that is now, either null for none.</summary>
public sealed class SelectedItemChangedEventArgs(object? oldValue, object? newValue) : EventArgs
{
	/// <summary>The node that was current before, or null.</summary>
	public object? OldValue { get; } = oldValue;

	/// <summary>The node that is current now, or null.</summary>
	public object? NewValue { get; } = newValue;
}
