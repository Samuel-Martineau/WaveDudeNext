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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using NLog;
using WaveDudeNext.App.Commands;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// The sources, optical parts and probes of a <see cref="Project"/> drawn over a picture of the canvas, as draggable
/// handles (<see cref="MarkerThumb"/>): a source's position, a part's centre, a point probe's point, a line probe's two
/// ends with the line between them, plus the emitters of a composite source as dots, its rotation handle on
/// the row axis (shown while the source is selected; drag turns the array; Shift snaps to 15°; the arrow keys
/// turn it by a degree, 2026-10-14), the same handle on a part's axis (2026-10-18), and, while a part is selected, its
/// outline in the accent colour with a size grip on a corner or vertex (drag sets the extents), a handle on each
/// point of an outline's list (2026-10-19, the user's request) and on each node of a fibre's path, with the node's two
/// Bézier handles on dotted lines (2026-10-22, the user's request; a smooth node turns the other handle with the one
/// moved, keeping its length), plus the item names as labels. Dragging a handle, or moving it with the arrow keys while
/// it has keyboard focus, writes the new canvas coordinates to the model; grabbing or focusing a handle makes its item
/// the <see cref="SelectedItem"/>. Several items can be selected at once (<see cref="SelectedItems"/>, 2026-10-19, the
/// user's request): a drag on empty picture space draws a marquee that selects the items whose handles it covers
/// (Shift adds, Ctrl toggles), Ctrl+click on a handle toggles its item and Shift+click adds it, and a drag of the
/// handle of one of several selected items, or the arrow keys on it, moves them all by the same delta, limited so
/// that none leaves the canvas (<see cref="Movable.Limit"/>); Escape during a drag puts things back. The rotation
/// lever, grips and point handles belong to the current item alone. The overlay fills the same rectangle as the
/// picture (an image stretched uniformly), so it maps canvas pixels to its own coordinates with the same uniform
/// scale and centring. UI thread; listens to the model's change notifications.
/// <para>
/// The port of upstream's WPF SceneOverlay (Controls/SceneOverlay.cs). What differs: the handle glyphs are control
/// themes in Themes/Markers.axaml picked here per role instead of seven style properties bound by the pane; the three
/// brushes are optional properties that fall back to the <c>PictureHandle</c> and <c>PictureSelection</c> resources;
/// the drag threshold of the marquee is a constant (Avalonia has no system parameter for it); the drag maths read the
/// pointer's absolute position from the thumb (<see cref="MarkerThumb.PointerPosition"/>, there is no static mouse
/// state); a Tab onto a handle is told apart from a press by the focus event's navigation method; Delete runs
/// <see cref="AppCommands.Remove"/> of <see cref="Commands"/>, since Avalonia has no routed commands.
/// </para>
/// </summary>
public sealed class SceneOverlay : Panel
{
	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly StyledProperty<Project?> ProjectProperty =
		AvaloniaProperty.Register<SceneOverlay, Project?>(nameof(Project));

	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly StyledProperty<int> CanvasWidthProperty =
		AvaloniaProperty.Register<SceneOverlay, int>(nameof(CanvasWidth), 1);

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly StyledProperty<int> CanvasHeightProperty =
		AvaloniaProperty.Register<SceneOverlay, int>(nameof(CanvasHeight), 1);

	/// <summary>Identifies the <see cref="SelectedItem"/> property; two-way by default, as upstream's was.</summary>
	public static readonly StyledProperty<object?> SelectedItemProperty =
		AvaloniaProperty.Register<SceneOverlay, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="SelectedItems"/> property.</summary>
	public static readonly StyledProperty<ObservableCollection<object>?> SelectedItemsProperty =
		AvaloniaProperty.Register<SceneOverlay, ObservableCollection<object>?>(nameof(SelectedItems));

	/// <summary>Identifies the <see cref="Layer"/> property.</summary>
	public static readonly StyledProperty<ProbeLayer?> LayerProperty =
		AvaloniaProperty.Register<SceneOverlay, ProbeLayer?>(nameof(Layer));

	/// <summary>Identifies the <see cref="HandlesInPreview"/> property.</summary>
	public static readonly StyledProperty<bool> HandlesInPreviewProperty =
		AvaloniaProperty.Register<SceneOverlay, bool>(nameof(HandlesInPreview), true);

	/// <summary>Identifies the <see cref="LabelsInPreview"/> property.</summary>
	public static readonly StyledProperty<bool> LabelsInPreviewProperty =
		AvaloniaProperty.Register<SceneOverlay, bool>(nameof(LabelsInPreview), true);

	/// <summary>Identifies the <see cref="Commands"/> property; a set of disabled commands until the pane hands the window's over, as the other panes' contracts do.</summary>
	public static readonly StyledProperty<AppCommands> CommandsProperty =
		AvaloniaProperty.Register<SceneOverlay, AppCommands>(nameof(Commands), new AppCommands());

	/// <summary>Identifies the <see cref="LineBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush?> LineBrushProperty =
		AvaloniaProperty.Register<SceneOverlay, IBrush?>(nameof(LineBrush));

	/// <summary>Identifies the <see cref="LabelBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush?> LabelBrushProperty =
		AvaloniaProperty.Register<SceneOverlay, IBrush?>(nameof(LabelBrush));

	/// <summary>Identifies the <see cref="AccentBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush?> AccentBrushProperty =
		AvaloniaProperty.Register<SceneOverlay, IBrush?>(nameof(AccentBrush));

	/// <summary>
	/// How close, in screen pixels, a probe's point A must come to its detached graph box for the box to snap back onto
	/// it (upstream's ProbeGraphOverlay.SnapRadius; the graph overlay snaps a box dragged onto the probe with the same radius).
	/// </summary>
	public const double ProbeBoxSnapRadius = 14.0;

	/// <summary>How far, in screen pixels on either axis, a press on empty space must travel before it opens the marquee (WPF took its system parameter, 4 px; Avalonia has none).</summary>
	public const double DragThreshold = 4.0;

	/// <summary>The clipboard format of project objects a drag may carry (<see cref="ProjectClipboard.Format"/>), as the data transfer API names it; the tree's drag source offers the text under it and as plain text.</summary>
	public static readonly DataFormat<string> ObjectsFormat = DataFormat.CreateStringApplicationFormat(ProjectClipboard.Format);

	/// <summary>Pixels a focused handle moves per arrow key, and with Shift.</summary>
	private const int KeyStep = 1;
	private const int KeyStepFast = 10;
	private const double LabelOffset = 13.0;
	private const double LabelSize = 11.0;

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Least distance of a rotation handle from its source's centre, in pixels.</summary>
	private const double AngleHandleReach = 28.0;

	/// <summary>Room between the end of the first row and the rotation handle, in pixels.</summary>
	private const double AngleHandleGap = 14.0;
	private const double ElementDotRadius = 2.5;
	private static readonly IBrush HaloBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
	private static readonly IBrush FallbackHandleBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
	private static readonly IBrush FallbackAccentBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xE0, 0x00));

	/// <summary>The handle glyphs (Themes/Markers.axaml), loaded once for every overlay: control themes are shared.</summary>
	private static readonly Uri MarkersUri = new("avares://WaveDudeNext.App/Themes/Markers.axaml");
	private static ResourceDictionary? _markers;

	private readonly HashSet<INotifyPropertyChanged> _subscribed = [];
	private readonly HashSet<INotifyCollectionChanged> _pointLists = [];
	private bool _regenerating;

	/// <summary>Where a press on empty picture space happened, in this element's coordinates; a move beyond the drag threshold opens the marquee.</summary>
	private Point? _marqueeStart;
	/// <summary>The marquee while it is drawn.</summary>
	private Rect? _marquee;
	/// <summary>The pointer the marquee captured, to let it go.</summary>
	private IPointer? _marqueePointer;
	/// <summary>The items a drag of one of their handles moves together; null for a single-item drag.</summary>
	private List<IMovable>? _group;
	/// <summary>How far the group has been moved in the current drag (canvas pixels), to take it back on Escape.</summary>
	private (int Dx, int Dy) _groupApplied;
	/// <summary>A press with Ctrl or Shift on a handle changes the selection and does not drag.</summary>
	private bool _suppressDrag;
	/// <summary>The angle a rotation handle's item had when its drag started, to restore on Escape.</summary>
	private double _dragStartAngle;

	/// <summary>The drawing's three bands as children, below the handles; see <see cref="DrawingLayer"/>.</summary>
	private readonly DrawingLayer[] _layers;

	/// <summary>The objects a drag carries, parsed once per drag.</summary>
	private readonly DraggedObjects _dragged = new();

	static SceneOverlay()
	{
		AffectsMeasure<SceneOverlay>(CanvasWidthProperty, CanvasHeightProperty);
	}

	/// <summary>Creates the overlay: the three drawing bands as its first children, a transparent background for the marquee, drops allowed.</summary>
	public SceneOverlay()
	{
		// a transparent background takes the press for the marquee; the press still bubbles on to the pane, which
		// focuses itself and clears the selection unless Shift or Ctrl is held (2026-10-19; until then no background,
		// so a click fell through to the graph boxes, which lie over this layer since 2026-10-18 anyway)
		Background = Brushes.Transparent;
		IsHitTestVisible = true;
		Focusable = false;
		// objects dragged from another window's tree are dropped onto the picture (2026-10-23)
		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragOverEvent, OnDragOver);
		AddHandler(DragDrop.DropEvent, OnDrop);
		_layers = [new DrawingLayer(this, Band.ProbeLines), new DrawingLayer(this, Band.Sources), new DrawingLayer(this, Band.PartsAndLabels)];
		AddLayers();
	}

	/// <summary>Puts the bands below the handles, first in the children, after a <see cref="Rebuild"/> cleared them.</summary>
	private void AddLayers()
	{
		foreach (DrawingLayer layer in _layers)
		{
			Children.Add(layer);
		}
	}

	/// <summary>Has the bands drawn again, all of them or one.</summary>
	private void InvalidateDrawing(Band? band = null)
	{
		foreach (DrawingLayer layer in _layers)
		{
			if (band == null || layer.Band == band)
			{
				layer.InvalidateVisual();
			}
		}
	}

	/// <summary>The overlay's drawing in the order it is laid down: the line probes, then the sources with their emitter dots, then the parts, the marquee and the labels.</summary>
	private enum Band
	{
		ProbeLines,
		Sources,
		PartsAndLabels,
	}

	/// <summary>
	/// One band of the overlay's drawing as a child of its own, the three in the order the one drawing had (upstream,
	/// 2026-11-04, the user's report: Example 7, a lattice of thousands of emitter dots, stuttered in playback). Not
	/// hit-testable, not focusable: the overlay takes the pointer and the handles the keyboard. Upstream made the
	/// sources' band a WPF bitmap cache, rasterised once per change; Avalonia's compositor retains each visual's drawing
	/// list and re-rasterises only the dirty visual, so the bands are plain controls here and the labels keep their
	/// text rendering.
	/// </summary>
	private sealed class DrawingLayer : Control
	{
		private readonly SceneOverlay _owner;

		public DrawingLayer(SceneOverlay owner, Band band)
		{
			_owner = owner;
			Band = band;
			IsHitTestVisible = false;
		}

		public Band Band { get; }

		/// <inheritdoc/>
		public override void Render(DrawingContext context) => _owner.RenderBand(context, Band, Bounds.Size);
	}

	/// <summary>The project whose sources and probes are shown; null shows nothing.</summary>
	public Project? Project
	{
		get => GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>Canvas width in pixels, the x range of the model coordinates.</summary>
	public int CanvasWidth
	{
		get => GetValue(CanvasWidthProperty);
		set => SetValue(CanvasWidthProperty, value);
	}

	/// <summary>Canvas height in pixels.</summary>
	public int CanvasHeight
	{
		get => GetValue(CanvasHeightProperty);
		set => SetValue(CanvasHeightProperty, value);
	}

	/// <summary>The source, part or probe selected in the project (highlighted); set when a handle is grabbed or focused. The current item of <see cref="SelectedItems"/>.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>The shared selection (the main window's): every item in it is highlighted and moves with a group drag; the overlay adds and removes items by marquee, Ctrl and Shift.</summary>
	public ObservableCollection<object>? SelectedItems
	{
		get => GetValue(SelectedItemsProperty);
		set => SetValue(SelectedItemsProperty, value);
	}

	/// <summary>
	/// Whether the handles, lines, emitter dots and levers are shown for every item (<see cref="Display.HandlesInPreview"/>);
	/// off, a selected item still shows its own (2026-10-29, the user's design), and the others are collapsed, out of the Tab
	/// cycle too, so the tree is where they are picked.
	/// </summary>
	public bool HandlesInPreview
	{
		get => GetValue(HandlesInPreviewProperty);
		set => SetValue(HandlesInPreviewProperty, value);
	}

	/// <summary>Whether the names beside the handles are drawn for every item (<see cref="Display.LabelsInPreview"/>); off, a selected item still shows its own.</summary>
	public bool LabelsInPreview
	{
		get => GetValue(LabelsInPreviewProperty);
		set => SetValue(LabelsInPreviewProperty, value);
	}

	/// <summary>The window's commands: Delete on a handle runs <see cref="AppCommands.Remove"/> with the handle's item. Every command is disabled until the pane hands the window's over, so Delete does nothing then.</summary>
	public AppCommands Commands
	{
		get => GetValue(CommandsProperty);
		set => SetValue(CommandsProperty, value);
	}

	/// <summary>
	/// The picture's layer: only the probes measuring it get handles and lines here (2026-10-18, the user's rule: a probe
	/// on both pictures confused, above all in a single-picture view). Sources drive both layers and show on both.
	/// Null shows every probe.
	/// </summary>
	public ProbeLayer? Layer
	{
		get => GetValue(LayerProperty);
		set => SetValue(LayerProperty, value);
	}

	/// <summary>Line of a line probe and the emitter dots of a composite source; unset, the <c>PictureHandle</c> resource.</summary>
	public IBrush? LineBrush
	{
		get => GetValue(LineBrushProperty);
		set => SetValue(LineBrushProperty, value);
	}

	/// <summary>The item names next to the handles; unset, the <c>PictureHandle</c> resource.</summary>
	public IBrush? LabelBrush
	{
		get => GetValue(LabelBrushProperty);
		set => SetValue(LabelBrushProperty, value);
	}

	/// <summary>Line and label of the selected item; unset, the <c>PictureSelection</c> resource.</summary>
	public IBrush? AccentBrush
	{
		get => GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	/// <summary>The handles, in their hit-test order (the last wins).</summary>
	public IEnumerable<MarkerThumb> Handles => Children.OfType<MarkerThumb>();

	/// <summary>Whether <paramref name="item"/> is the current item or one of the selected ones.</summary>
	private bool IsSelected(object item) => ReferenceEquals(item, SelectedItem) || (SelectedItems?.Contains(item) ?? false);

	/// <summary>The handle roles that move an item as a whole (not a lever, grip or point): the ones a group drag starts from.</summary>
	private static bool MovesWhole(MarkerRole role) => role is MarkerRole.Source or MarkerRole.Part or MarkerRole.ProbeA or MarkerRole.ProbeB;

	/// <summary>Whether <paramref name="probe"/> belongs on this picture (see <see cref="Layer"/>).</summary>
	private bool Shows(Probe probe) => Layer is not { } layer || probe.Layer == layer;

	/// <summary>The brush a property gives, else the named resource (from the application's dictionaries), else the built-in value of that resource.</summary>
	private IBrush BrushOf(IBrush? set, string resourceKey, IBrush fallback) =>
		set ?? (this.TryFindResource(resourceKey, out object? found) && found is IBrush brush ? brush : fallback);

	private IBrush LineBrushNow => BrushOf(LineBrush, "PictureHandle", FallbackHandleBrush);
	private IBrush LabelBrushNow => BrushOf(LabelBrush, "PictureHandle", FallbackHandleBrush);
	private IBrush AccentBrushNow => BrushOf(AccentBrush, "PictureSelection", FallbackAccentBrush);

	/// <summary>The control theme of the handle glyph with <paramref name="key"/> in Themes/Markers.axaml; null when the dictionary lacks it (the thumb then shows Fluent's plain thumb).</summary>
	private static ControlTheme? ThemeOf(string key)
	{
		_markers ??= (ResourceDictionary)AvaloniaXamlLoader.Load(MarkersUri);
		return _markers.TryGetResource(key, null, out object? value) && value is ControlTheme theme ? theme : null;
	}

	// ---------------------------------------------------------------- model wiring

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == ProjectProperty)
		{
			if (change.OldValue is Project old)
			{
				old.Sources.CollectionChanged -= OnCollectionChanged;
				old.Parts.CollectionChanged -= OnCollectionChanged;
				old.Probes.CollectionChanged -= OnCollectionChanged;
			}
			if (change.NewValue is Project project)
			{
				project.Sources.CollectionChanged += OnCollectionChanged;
				project.Parts.CollectionChanged += OnCollectionChanged;
				project.Probes.CollectionChanged += OnCollectionChanged;
			}
			Rebuild();
		}
		else if (change.Property == SelectedItemsProperty)
		{
			if (change.OldValue is ObservableCollection<object> old)
			{
				old.CollectionChanged -= OnSelectionChanged;
			}
			if (change.NewValue is ObservableCollection<object> set)
			{
				set.CollectionChanged += OnSelectionChanged;
			}
			UpdateSelection();
		}
		else if (change.Property == SelectedItemProperty || change.Property == HandlesInPreviewProperty || change.Property == LabelsInPreviewProperty)
		{
			UpdateSelection();
		}
		else if (change.Property == LayerProperty)
		{
			Rebuild();
		}
		else if (change.Property == CanvasWidthProperty || change.Property == CanvasHeightProperty)
		{
			InvalidateArrange();
			InvalidateDrawing();
		}
		else if (change.Property == LineBrushProperty || change.Property == LabelBrushProperty || change.Property == AccentBrushProperty)
		{
			InvalidateDrawing();
		}
	}

	private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSelection();

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

	/// <summary>Recreates the handles from the project's lists and re-subscribes to every item.</summary>
	private void Rebuild()
	{
		foreach (INotifyPropertyChanged item in _subscribed)
		{
			item.PropertyChanged -= OnItemPropertyChanged;
		}
		_subscribed.Clear();
		foreach (INotifyCollectionChanged list in _pointLists)
		{
			list.CollectionChanged -= OnCollectionChanged;
		}
		_pointLists.Clear();
		Children.Clear();
		AddLayers();
		if (Project is not { } project)
		{
			InvalidateDrawing();
			return;
		}
		foreach (Source source in project.Sources)
		{
			Subscribe(source);
			if (source.Array != null)
			{
				Subscribe(source.Array);
			}
			Regenerate(source);
			AddThumb(source, MarkerRole.Source, "SourceMarker", source.Name, "Source:" + source.Name);
			if (source.Array != null)
			{
				AddThumb(source, MarkerRole.SourceAngle, "AngleMarker", source.Name, "SourceAngle:" + source.Name, "TipAngleMarkerFormat");
			}
		}
		foreach (OpticalPart part in project.Parts)
		{
			Subscribe(part);
			AddThumb(part, MarkerRole.Part, "PartMarker", part.Name, "Part:" + part.Name);
			AddThumb(part, MarkerRole.PartAngle, "AngleMarker", part.Name, "PartAngle:" + part.Name, "TipAngleMarkerFormat");
			if (GripOf(part) != null)
			{
				AddThumb(part, MarkerRole.PartGrip, "PartGripMarker", part.Name, "PartGrip:" + part.Name, "TipGripFormat");
			}
			if (part is LensPart)
			{
				// a handle on each surface's vertex, which sets its radius (2026-10-26, Jeroen's proposal)
				AddThumb(part, MarkerRole.PartSurface, "FiberHandleMarker", part.Name, $"PartSurface:{part.Name}:1", "TipSurfaceFormat", 1);
				AddThumb(part, MarkerRole.PartSurface, "FiberHandleMarker", part.Name, $"PartSurface:{part.Name}:2", "TipSurfaceFormat", 2);
			}
			if (PointsOf(part) is { } points)
			{
				// the list and its points: a point added, removed or moved rebuilds or re-arranges
				if (_pointLists.Add(points))
				{
					points.CollectionChanged += OnCollectionChanged;
				}
				for (int i = 0; i < points.Count; i++)
				{
					Subscribe(points[i]);
					AddThumb(part, MarkerRole.PartPoint, "PartPointMarker", part.Name, $"PartPoint:{part.Name}:{i}", "TipPointFormat", i);
				}
			}
			else if (part is FiberPart fiber)
			{
				// the path's nodes, each with its Bézier handles added first so the node wins where they overlap; the
				// first node's in-handle and the last node's out-handle shape nothing and get no thumb
				if (_pointLists.Add(fiber.Nodes))
				{
					fiber.Nodes.CollectionChanged += OnCollectionChanged;
				}
				for (int i = 0; i < fiber.Nodes.Count; i++)
				{
					Subscribe(fiber.Nodes[i]);
					if (i > 0)
					{
						AddThumb(part, MarkerRole.FiberIn, "FiberHandleMarker", part.Name, $"FiberIn:{part.Name}:{i}", "TipInHandleFormat", i);
					}
					if (i < fiber.Nodes.Count - 1)
					{
						AddThumb(part, MarkerRole.FiberOut, "FiberHandleMarker", part.Name, $"FiberOut:{part.Name}:{i}", "TipOutHandleFormat", i);
					}
					AddThumb(part, MarkerRole.PartPoint, "PartPointMarker", part.Name, $"PartPoint:{part.Name}:{i}", "TipNodeFormat", i);
				}
			}
		}
		foreach (Probe probe in project.Probes)
		{
			Subscribe(probe);
			if (!Shows(probe))
			{
				continue;
			}
			AddThumb(probe, MarkerRole.ProbeA, "ProbeMarker", probe.Name, "Probe:" + probe.Name + ":A");
			if (probe.Kind == ProbeKind.Line)
			{
				AddThumb(probe, MarkerRole.ProbeB, "ProbeMarker", probe.Name + " B", "Probe:" + probe.Name + ":B");
			}
		}
		UpdateSelection();
		InvalidateDrawing();
	}

	private void Subscribe(INotifyPropertyChanged item)
	{
		if (_subscribed.Add(item))
		{
			item.PropertyChanged += OnItemPropertyChanged;
		}
	}

	private void AddThumb(object item, MarkerRole role, string themeKey, string name, string automationId, string tipKey = "TipMarkerFormat", int index = -1)
	{
		var thumb = new MarkerThumb(item, role, index) { Theme = ThemeOf(themeKey) };
		ToolTip.SetTip(thumb, TipFormat(tipKey) is { } format ? string.Format(CultureInfo.CurrentUICulture, format, name, index) : name);
		AutomationProperties.SetName(thumb, name);
		AutomationProperties.SetAutomationId(thumb, automationId);
		thumb.DragStarted += OnDragStarted;
		thumb.DragDelta += OnDragDelta;
		thumb.DragCompleted += OnDragCompleted;
		// Tab to a handle selects its item; a press is left to the drag start, which reads Ctrl and Shift first (the
		// focus manager focuses the pressed handle before the press reaches it, with the pointer as the method)
		thumb.GotFocus += (_, e) =>
		{
			if (e.NavigationMethod != NavigationMethod.Pointer)
			{
				SelectedItem = item;
			}
		};
		UpdateOpacity(thumb);
		Children.Add(thumb);
	}

	/// <summary>The tooltip format of the string table (reached through the tree when attached, else the application's resources); null when the table lacks it.</summary>
	private string? TipFormat(string key)
	{
		if (this.TryFindResource(key, out object? value) && value is string format)
		{
			return format;
		}
		return Application.Current is { } application && application.TryGetResource(key, null, out value) && value is string text ? text : null;
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (sender)
		{
			case Probe when e.PropertyName is nameof(Probe.Kind) or nameof(Probe.Layer):
			case Source when e.PropertyName == nameof(Source.Array):
			case OutlinePart when e.PropertyName == nameof(OutlinePart.Points):
			case FiberPart when e.PropertyName == nameof(FiberPart.Nodes):
				Rebuild();
				return;
			case Source source:
				Regenerate(source);
				break;
			case SourceArray array when Project?.Sources.FirstOrDefault(s => ReferenceEquals(s.Array, array)) is { } owner:
				// a grid edit (size, spacing, rotation) moves the emitter dots at once
				Regenerate(owner);
				break;
		}
		if (e.PropertyName is nameof(Source.Name) or nameof(Source.Enabled) or nameof(Probe.Active))
		{
			Rebuild();
			return;
		}
		InvalidateArrange();
		InvalidateDrawing();
	}

	/// <summary>Keeps a composite source's emitter dots current while it is edited (they are only regenerated for a run otherwise).</summary>
	private void Regenerate(Source source)
	{
		if (_regenerating || Project is not { } project || source.Array == null)
		{
			return;
		}
		_regenerating = true;
		try
		{
			source.GenerateElements(project.Seed);
		}
		finally
		{
			_regenerating = false;
		}
	}

	private void UpdateSelection()
	{
		object? selected = SelectedItem;
		foreach (MarkerThumb thumb in Handles)
		{
			thumb.IsSelectedMarker = IsSelected(thumb.Item);
			// handles hidden by Display (2026-10-29, the user's design) except a selected item's, which shows them either way; the
			// rotation handle, grip, point, node and Bézier handles only while their source or part is the current item (the
			// user's rule, 2026-10-14); hidden, they are out of the Tab cycle too
			bool shown = HandlesInPreview || thumb.IsSelectedMarker;
			thumb.IsVisible = shown && (!IsDetail(thumb.Role) || ReferenceEquals(thumb.Item, selected));
		}
		InvalidateArrange();
		InvalidateDrawing();
	}

	/// <summary>The items with the selected one last, so what is drawn for it lies over the others (the user's rule, 2026-10-18).</summary>
	private static IEnumerable<T> SelectedLast<T>(IEnumerable<T> items, object? selected) where T : class =>
		items.OrderBy(item => ReferenceEquals(item, selected));

	private static void UpdateOpacity(MarkerThumb thumb) =>
		thumb.Opacity = thumb.Item switch { Source { Enabled: false } => 0.45, OpticalPart { Enabled: false } => 0.45, Probe { Active: false } => 0.45, _ => 1.0 };

	// ---------------------------------------------------------------- geometry

	/// <summary>
	/// Pixels per canvas pixel: the picture's uniform scale inside an element of <paramref name="size"/>. Every mapping
	/// takes the element size explicitly, because during <see cref="ArrangeOverride"/> <see cref="Visual.Bounds"/> still
	/// holds the previous layout's size (the bounds are set after arrange); placing the handles from it left them where
	/// the picture used to be after every resize, while the labels moved with it (upstream, 2026-10-10).
	/// </summary>
	public double ScaleFor(Size size) => Math.Min(size.Width / Math.Max(1, CanvasWidth), size.Height / Math.Max(1, CanvasHeight));

	/// <summary>The current scale, for rendering and dragging (after layout).</summary>
	private double Scale => ScaleFor(Bounds.Size);

	/// <summary>The centre of canvas pixel (x, y) in this element's coordinates, for an element of <paramref name="size"/>; the picture is centred in it.</summary>
	public Point ToView(int x, int y, Size size)
	{
		double scale = ScaleFor(size);
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		return new Point(originX + (x + 0.5) * scale, originY + (y + 0.5) * scale);
	}

	/// <summary>The centre of canvas pixel (x, y) in this element's coordinates at its current size.</summary>
	public Point ToView(int x, int y) => ToView(x, y, Bounds.Size);

	/// <summary>A continuous canvas point (pixel (x, y) spanning [x, x + 1)) in this element's coordinates, for an element of <paramref name="size"/>.</summary>
	public Point ToViewExact(double x, double y, Size size)
	{
		double scale = ScaleFor(size);
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		return new Point(originX + x * scale, originY + y * scale);
	}

	/// <summary>A continuous canvas point in this element's coordinates at its current size.</summary>
	public Point ToViewExact(double x, double y) => ToViewExact(x, y, Bounds.Size);

	/// <summary>The inverse of <see cref="ToViewExact(double, double)"/> at the current size: a point of this element as continuous canvas coordinates (may lie outside the canvas).</summary>
	public (double X, double Y) ToCanvasExact(Point view)
	{
		Size size = Bounds.Size;
		double scale = Scale;
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		return ((view.X - originX) / scale, (view.Y - originY) / scale);
	}

	// ---------------------------------------------------------------- a part's grip and points

	/// <summary>The point list a part's point handles edit: an outline's; null for the other kinds (a fibre has nodes, see <see cref="NodeOf"/>).</summary>
	private static ObservableCollection<PartPoint>? PointsOf(OpticalPart part) => part switch
	{
		OutlinePart outline => outline.Points,
		_ => null,
	};

	/// <summary>The fibre node a node or Bézier handle stands on, or null for another handle or an index past the list.</summary>
	private static FiberNode? NodeOf(MarkerThumb thumb) =>
		thumb.Item is FiberPart fiber && thumb.Index >= 0 && thumb.Index < fiber.Nodes.Count ? fiber.Nodes[thumb.Index] : null;

	/// <summary>The length of the other Bézier handle of the node a handle thumb stands on, which a smooth node keeps while that handle turns; null for another thumb.</summary>
	private static double? MirrorLengthOf(MarkerThumb thumb)
	{
		if (thumb.Role is not (MarkerRole.FiberIn or MarkerRole.FiberOut) || NodeOf(thumb) is not { } node)
		{
			return null;
		}
		(int dx, int dy) = thumb.Role == MarkerRole.FiberOut ? (node.InX, node.InY) : (node.OutX, node.OutY);
		return Math.Sqrt((double)dx * dx + (double)dy * dy);
	}

	/// <summary>Whether a handle is shown for the current item alone: a rotation handle, a grip, a point, node or Bézier handle.</summary>
	private static bool IsDetail(MarkerRole role) => role is MarkerRole.SourceAngle or MarkerRole.PartAngle || HasCanvasPosition(role);

	/// <summary>Whether a handle sits on a continuous canvas position of its own (<see cref="CanvasOf"/>): a grip, a point, node, Bézier handle or lens vertex.</summary>
	private static bool HasCanvasPosition(MarkerRole role) => role is MarkerRole.PartGrip or MarkerRole.PartPoint or MarkerRole.PartSurface or MarkerRole.FiberIn or MarkerRole.FiberOut;

	/// <summary>What a drag of a handle holds still (<see cref="MarkerThumb.DragAnchors"/>): a lens vertex's rim and the other vertex, in canvas pixels; null for the other handles.</summary>
	private static (double X, double Y)[]? AnchorsOf(MarkerThumb thumb) =>
		thumb is { Item: LensPart lens, Role: MarkerRole.PartSurface }
			? [lens.ToCanvas(lens.RimX(thumb.Index), 0.0), lens.ToCanvas(lens.VertexX(thumb.Index == 1 ? 2 : 1), 0.0)]
			: null;

	/// <summary>
	/// Where a part's size grip sits in the part frame: the corner of a rectangle or an ellipse's box, lens (thickness, diameter), GRIN lens,
	/// Bragg stack or zone plate, the first vertex of a polygon, the second end of a triangle's base; null for a kind without one.
	/// </summary>
	private static (double X, double Y)? GripOf(OpticalPart part) => part switch
	{
		RectanglePart rectangle => (rectangle.Length / 2.0, rectangle.Width / 2.0),
		EllipsePart ellipse => (ellipse.Length / 2.0, ellipse.Width / 2.0),
		LensPart lens => (lens.Thickness / 2.0, lens.Diameter / 2.0),
		PolygonPart polygon => polygon.Vertices()[0],
		TrianglePart triangle => triangle.Vertices()[1],
		GrinLensPart grin => (grin.Length / 2.0, grin.Diameter / 2.0),
		BraggReflectorPart bragg => (bragg.Length / 2.0, bragg.Width / 2.0),
		ZonePlatePart plate => (plate.Thickness / 2.0, plate.Diameter / 2.0),
		_ => null,
	};

	/// <summary>Sets the extents a grip stands for from the grip's new canvas position: whole pixels, never below the kind's least size.</summary>
	private static void SetGrip(OpticalPart part, double canvasX, double canvasY)
	{
		(double x, double y) = part.ToFrame(canvasX, canvasY);
		int across = (int)Math.Round(2.0 * Math.Abs(y));
		int along = (int)Math.Round(2.0 * Math.Abs(x));
		switch (part)
		{
			case RectanglePart rectangle:
				// linked (a square): the farther of the two coordinates sets both
				rectangle.Length = Math.Max(1, rectangle.Linked ? Math.Max(along, across) : along);
				rectangle.Width = Math.Max(1, rectangle.Linked ? Math.Max(along, across) : across);
				break;
			case EllipsePart ellipse:
				// the grip sits on the corner of the bounding box, as the rectangle's does
				ellipse.Length = Math.Max(1, ellipse.Linked ? Math.Max(along, across) : along);
				ellipse.Width = Math.Max(1, ellipse.Linked ? Math.Max(along, across) : across);
				break;
			case LensPart lens:
				// stretched like a rectangle: across the diameter, along the whole profile, the arcs refitted (2026-10-26)
				lens.SetExtents(across, along);
				break;
			case PolygonPart polygon:
				// the vertex stays on its ray; its distance is the circumradius
				polygon.Side = Math.Max(2, (int)Math.Round(2.0 * Math.Sqrt(x * x + y * y) * Math.Sin(Math.PI / Math.Clamp(polygon.Sides, PolygonPart.MinSides, PolygonPart.MaxSides))));
				break;
			case TrianglePart triangle:
				triangle.Base = Math.Max(2, across);
				break;
			case GrinLensPart grin:
				grin.Length = Math.Max(1, along);
				grin.Diameter = Math.Max(2, across);
				break;
			case BraggReflectorPart bragg:
				bragg.Width = Math.Max(2, across);
				break;
			case ZonePlatePart plate:
				plate.Thickness = Math.Max(1, along);
				plate.Diameter = Math.Max(2, across);
				break;
		}
	}

	/// <summary>The canvas position of a grip, point, node or Bézier handle (continuous coordinates), or null when the handle has nothing to stand on.</summary>
	private static (double X, double Y)? CanvasOf(MarkerThumb thumb) => (thumb.Item, thumb.Role) switch
	{
		(OpticalPart part, MarkerRole.PartGrip) when GripOf(part) is { } grip => part.ToCanvas(grip.X, grip.Y),
		(LensPart lens, MarkerRole.PartSurface) => lens.ToCanvas(lens.VertexX(thumb.Index), 0.0),
		(OpticalPart part, MarkerRole.PartPoint) when PointsOf(part) is { } points && thumb.Index >= 0 && thumb.Index < points.Count => part.ToCanvas(points[thumb.Index].X, points[thumb.Index].Y),
		(FiberPart fiber, MarkerRole.PartPoint) when NodeOf(thumb) is { } node => fiber.ToCanvas(node.X, node.Y),
		(FiberPart fiber, MarkerRole.FiberIn) when NodeOf(thumb) is { } node => fiber.ToCanvas(node.In.X, node.In.Y),
		(FiberPart fiber, MarkerRole.FiberOut) when NodeOf(thumb) is { } node => fiber.ToCanvas(node.Out.X, node.Out.Y),
		_ => null,
	};

	/// <summary>The picture's rectangle in this element's coordinates for an element of <paramref name="size"/>: the canvas at its uniform scale, centred (the element fills the cell, the picture is letterboxed in it).</summary>
	private Rect PictureRect(Size size)
	{
		double scale = ScaleFor(size);
		double width = CanvasWidth * scale, height = CanvasHeight * scale;
		return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
	}

	/// <summary>
	/// Where the rotation handle of a composite source or a part sits, for an element of <paramref name="size"/>: on
	/// the array's row axis or the part's axis, turned by its angle (counter-clockwise on the screen), just beyond the
	/// first row's half-width or the part's reach along the axis, and at least <see cref="AngleHandleReach"/> from the centre.
	/// </summary>
	private Point AnglePoint(object item, Size size)
	{
		(int x, int y, double angle, double extent) = item switch
		{
			Source { Array: { } array } source => (source.X, source.Y, array.Angle, (array.SizeX - 1) / 2.0 * array.SpacingX),
			OpticalPart part => (part.X, part.Y, part.Angle, part.AxisReach),
			_ => (0, 0, 0.0, 0.0),
		};
		Point centre = ToView(x, y, size);
		double reach = Math.Max(AngleHandleReach, extent * ScaleFor(size) + AngleHandleGap);
		double radians = angle * Math.PI / 180.0;
		return new Point(centre.X + reach * Math.Cos(radians), centre.Y - reach * Math.Sin(radians));
	}

	/// <summary>The angle of a composite source's array or a part, in degrees.</summary>
	private static double AngleOf(object item) => item switch
	{
		Source source => source.Array?.Angle ?? 0.0,
		OpticalPart part => part.Angle,
		_ => 0.0,
	};

	/// <summary>Turns the array or part so its axis points from its centre to <paramref name="view"/>: to whole degrees, or to the nearest 15 with <paramref name="snap"/>.</summary>
	private void SetAngle(object item, Point view, bool snap)
	{
		(int x, int y) = item switch { Source source => (source.X, source.Y), OpticalPart part => (part.X, part.Y), _ => (0, 0) };
		Point centre = ToView(x, y);
		double degrees = Math.Atan2(-(view.Y - centre.Y), view.X - centre.X) * 180.0 / Math.PI;
		double unit = snap ? 15.0 : 1.0;
		SetAngle(item, Math.Round(degrees / unit) * unit);
	}

	/// <summary>Sets the array's or part's angle, kept in (-180, 180].</summary>
	private static void SetAngle(object item, double degrees)
	{
		switch (item)
		{
			case Source { Array: { } array }:
				array.Angle = degrees;
				array.Normalize();
				break;
			case OpticalPart part:
				part.Angle = degrees;
				part.Normalize();
				break;
		}
	}

	private static (int X, int Y) PointOf(MarkerThumb thumb) => (thumb.Item, thumb.Role) switch
	{
		(Source source, _) => (source.X, source.Y),
		(OpticalPart, _) when HasCanvasPosition(thumb.Role) && CanvasOf(thumb) is { } canvas => ((int)Math.Round(canvas.X), (int)Math.Round(canvas.Y)),
		(OpticalPart part, _) => (part.X, part.Y),
		(Probe probe, MarkerRole.ProbeB) => (probe.BX, probe.BY),
		(Probe probe, _) => (probe.AX, probe.AY),
		_ => (0, 0),
	};

	private void SetPoint(MarkerThumb thumb, int x, int y)
	{
		x = Math.Clamp(x, 0, Math.Max(0, CanvasWidth - 1));
		y = Math.Clamp(y, 0, Math.Max(0, CanvasHeight - 1));
		switch (thumb.Item, thumb.Role)
		{
			case (Source source, _):
				source.X = x;
				source.Y = y;
				break;
			case (OpticalPart part, MarkerRole.PartGrip):
				SetGrip(part, x, y);
				break;
			case (LensPart lens, MarkerRole.PartSurface):
				// along the axis: the pointer's place in the part frame, its x alone; the rim and the other vertex as the drag found them
				(double axisX, _) = lens.ToFrame(x, y);
				if (thumb.DragAnchors is [var rim, var other])
				{
					lens.SetVertex(thumb.Index, axisX, lens.ToFrame(rim.X, rim.Y).X, lens.ToFrame(other.X, other.Y).X);
				}
				else
				{
					lens.SetVertex(thumb.Index, axisX);
				}
				break;
			case (FiberPart fiber, MarkerRole.PartPoint):
				// a node moves with its handles, which are offsets from it
				if (NodeOf(thumb) is { } moved)
				{
					(double fx, double fy) = fiber.ToFrame(x, y);
					moved.X = (int)Math.Round(fx);
					moved.Y = (int)Math.Round(fy);
				}
				break;
			case (FiberPart fiber, MarkerRole.FiberIn or MarkerRole.FiberOut):
				if (NodeOf(thumb) is { } node)
				{
					(double fx, double fy) = fiber.ToFrame(x, y);
					node.SetHandle(thumb.Role == MarkerRole.FiberOut, (int)Math.Round(fx) - node.X, (int)Math.Round(fy) - node.Y, thumb.MirrorLength);
				}
				break;
			case (OpticalPart part, MarkerRole.PartPoint):
				if (PointsOf(part) is { } points && thumb.Index >= 0 && thumb.Index < points.Count)
				{
					(double fx, double fy) = part.ToFrame(x, y);
					points[thumb.Index].X = (int)Math.Round(fx);
					points[thumb.Index].Y = (int)Math.Round(fy);
				}
				break;
			case (OpticalPart part, _):
				part.X = x;
				part.Y = y;
				break;
			case (Probe probe, MarkerRole.ProbeB):
				probe.BX = x;
				probe.BY = y;
				break;
			case (Probe probe, _):
				probe.AX = x;
				probe.AY = y;
				// a probe dragged onto its (detached) graph box picks it up, as the box does when dragged onto the probe
				if (!probe.IsBoxAttached)
				{
					double scale = Scale;
					double dx = (probe.BoxX - x) * scale;
					double dy = (probe.BoxY - y) * scale;
					if (dx * dx + dy * dy <= ProbeBoxSnapRadius * ProbeBoxSnapRadius)
					{
						probe.BoxX = x;
						probe.BoxY = y;
					}
				}
				break;
		}
	}

	/// <summary>
	/// The picture's size, the canvas fitted uniformly into the room (as the picture control fits it), so the cell
	/// holding the picture and its overlays is sized to the picture and its edge line hugs it (2026-10-25); nothing without a bound.
	/// </summary>
	protected override Size MeasureOverride(Size availableSize)
	{
		foreach (Control child in Children)
		{
			child.Measure(Size.Infinity);
		}
		if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
		{
			return default;
		}
		double scale = Math.Min(availableSize.Width / Math.Max(1, CanvasWidth), availableSize.Height / Math.Max(1, CanvasHeight));
		return new Size(Math.Max(1, CanvasWidth) * scale, Math.Max(1, CanvasHeight) * scale);
	}

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize)
	{
		Rect picture = PictureRect(finalSize);
		foreach (DrawingLayer layer in _layers)
		{
			layer.Arrange(new Rect(finalSize));
		}
		foreach (MarkerThumb thumb in Handles)
		{
			(int x, int y) = PointOf(thumb);
			Point centre = thumb.Role switch
			{
				MarkerRole.SourceAngle or MarkerRole.PartAngle => AnglePoint(thumb.Item, finalSize),
				_ when HasCanvasPosition(thumb.Role) && CanvasOf(thumb) is { } canvas => ToViewExact(canvas.X, canvas.Y, finalSize),
				_ => ToView(x, y, finalSize),
			};
			Size size = thumb.DesiredSize;
			var rect = new Rect(centre.X - size.Width / 2, centre.Y - size.Height / 2, size.Width, size.Height);
			thumb.Arrange(rect);
			if (IsDetail(thumb.Role))
			{
				// the rotation handle stays on the row axis, however far out; what falls outside the picture is clipped
				// (2026-10-16, the user's rule: the lever bending to keep the handle inside made no sense). The other
				// handles sit on clamped canvas positions and show whole at the edge
				Rect visible = picture.Intersect(rect);
				thumb.Clip = visible == rect ? null : new RectangleGeometry(new Rect(visible.X - rect.X, visible.Y - rect.Y, Math.Max(0, visible.Width), Math.Max(0, visible.Height)));
			}
		}
		return finalSize;
	}

	/// <summary>
	/// Draws one band for its <see cref="DrawingLayer"/>, clipped to the picture; the labels, outside the clip, go with the last
	/// band. By the layer's size, <paramref name="size"/>, never this element's: the layers are arranged from inside this
	/// element's ArrangeOverride, before its own bounds are updated, so this element's size is a layout behind at that
	/// moment (upstream, 2026-11-04, the user's report: a resize left the dots, the lines and the labels at the previous size).
	/// </summary>
	private void RenderBand(DrawingContext dc, Band band, Size size)
	{
		if (Project is not { } project || size.Width <= 0 || size.Height <= 0)
		{
			return;
		}
		object? selected = SelectedItem;
		// lines and dots stay within the picture (2026-10-16, after a rotated array dragged to the border put its
		// emitter dots over the menu bar): an emitter or a line end outside the canvas is clipped here, the handles
		// are children and sit on clamped positions, and the labels below are kept inside by their placement. The
		// picture's rectangle, not this element's: the element fills the cell and the picture is letterboxed in it
		using (dc.PushClip(PictureRect(size)))
		{
			switch (band)
			{
				case Band.ProbeLines:
					RenderProbeLines(dc, project, selected, size);
					break;
				case Band.Sources:
					RenderSources(dc, project, selected, size);
					break;
				default:
					RenderParts(dc, project, selected, size);
					break;
			}
		}
		if (band == Band.PartsAndLabels)
		{
			RenderLabels(dc, size);
		}
	}

	/// <summary>The line probes: a dark line under a dashed light one, so the line reads on any picture.</summary>
	private void RenderProbeLines(DrawingContext dc, Project project, object? selected, Size size)
	{
		IBrush line = LineBrushNow;
		IBrush accent = AccentBrushNow;
		foreach (Probe probe in SelectedLast(project.Probes, selected))
		{
			bool isSelected = IsSelected(probe);
			if (probe.Kind != ProbeKind.Line || !Shows(probe) || (!HandlesInPreview && !isSelected))
			{
				continue;
			}
			Point a = ToView(probe.AX, probe.AY, size);
			Point b = ToView(probe.BX, probe.BY, size);
			// a dark line under the dashed light one, so the line reads on any picture
			dc.DrawLine(new Pen(HaloBrush, isSelected ? 3.5 : 3.0), a, b);
			dc.DrawLine(new Pen(isSelected ? accent : line, isSelected ? 1.5 : 1.0, DashStyle.Dash), a, b);
		}
	}

	/// <summary>The sources: the current one's axis to its rotation handle and every composite source's emitter dots.</summary>
	private void RenderSources(DrawingContext dc, Project project, object? selected, Size size)
	{
		IBrush line = LineBrushNow;
		IBrush accent = AccentBrushNow;
		// one pen for every dot: a lattice has thousands of them
		var halo = new Pen(HaloBrush, 1.5);
		foreach (Source source in SelectedLast(project.Sources, selected))
		{
			bool isSelected = IsSelected(source);
			if (source.Array == null || (!HandlesInPreview && !isSelected))
			{
				continue;
			}
			IBrush dots = isSelected ? accent : line;
			if (ReferenceEquals(source, selected))
			{
				// the row axis from the centre to the rotation handle, so the turn reads as a lever; with the handle, only while selected
				Point axisEnd = AnglePoint(source, size);
				dc.DrawLine(new Pen(HaloBrush, 3.0), ToView(source.X, source.Y, size), axisEnd);
				dc.DrawLine(new Pen(dots, 1.0, DashStyle.Dot), ToView(source.X, source.Y, size), axisEnd);
			}
			foreach (SourceElement element in source.Elements)
			{
				dc.DrawEllipse(dots, halo, ToView(element.X, element.Y, size), ElementDotRadius, ElementDotRadius);
			}
		}
	}

	/// <summary>The selected parts' outlines, the current one's axis and a fibre's Bézier handle lines, and the marquee.</summary>
	private void RenderParts(DrawingContext dc, Project project, object? selected, Size size)
	{
		IBrush accent = AccentBrushNow;
		foreach (OpticalPart part in SelectedLast(project.Parts, selected))
		{
			if (!IsSelected(part))
			{
				continue;
			}
			// the part's outline as painted, in the accent colour, so the shape reads apart from the scene's glow
			(double X, double Y)[] outline = part.Outline();
			if (outline.Length >= 2)
			{
				var geometry = new StreamGeometry();
				using (StreamGeometryContext context = geometry.Open())
				{
					(double x0, double y0) = part.ToCanvas(outline[0].X, outline[0].Y);
					context.BeginFigure(ToViewExact(x0, y0, size), false);
					for (int i = 1; i < outline.Length; i++)
					{
						(double x, double y) = part.ToCanvas(outline[i].X, outline[i].Y);
						context.LineTo(ToViewExact(x, y, size), true);
					}
					context.EndFigure(part.OutlineClosed);
				}
				dc.DrawGeometry(null, new Pen(HaloBrush, 3.0, lineJoin: PenLineJoin.Round), geometry);
				dc.DrawGeometry(null, new Pen(accent, 1.0, lineJoin: PenLineJoin.Round), geometry);
			}
			if (ReferenceEquals(part, selected))
			{
				// the part's axis from the centre to its rotation handle, the lever, while it is the current item
				Point axisEnd = AnglePoint(part, size);
				dc.DrawLine(new Pen(HaloBrush, 3.0), ToView(part.X, part.Y, size), axisEnd);
				dc.DrawLine(new Pen(accent, 1.0, DashStyle.Dot), ToView(part.X, part.Y, size), axisEnd);
				if (part is FiberPart fiber)
				{
					// each node's Bézier handles on dotted lines from the node (the handle thumbs sit at their ends)
					var handleHalo = new Pen(HaloBrush, 3.0);
					var handleLine = new Pen(accent, 1.0, DashStyle.Dot);
					for (int i = 0; i < fiber.Nodes.Count; i++)
					{
						FiberNode node = fiber.Nodes[i];
						(double nx, double ny) = fiber.ToCanvas(node.X, node.Y);
						Point at = ToViewExact(nx, ny, size);
						if (i > 0)
						{
							(double hx, double hy) = fiber.ToCanvas(node.In.X, node.In.Y);
							Point handle = ToViewExact(hx, hy, size);
							dc.DrawLine(handleHalo, at, handle);
							dc.DrawLine(handleLine, at, handle);
						}
						if (i < fiber.Nodes.Count - 1)
						{
							(double hx, double hy) = fiber.ToCanvas(node.Out.X, node.Out.Y);
							Point handle = ToViewExact(hx, hy, size);
							dc.DrawLine(handleHalo, at, handle);
							dc.DrawLine(handleLine, at, handle);
						}
					}
				}
			}
		}
		if (_marquee is { } marquee)
		{
			// the marquee: a faint accent fill under a dashed accent edge
			using (dc.PushOpacity(0.15))
			{
				dc.FillRectangle(accent, marquee);
			}
			dc.DrawRectangle(null, new Pen(accent, 1.0, DashStyle.Dash), marquee);
		}
	}

	/// <summary>The names beside the main handles, outside the picture's clip, kept inside by their placement.</summary>
	private void RenderLabels(DrawingContext dc, Size size)
	{
		IBrush labelBrush = LabelBrushNow;
		IBrush accent = AccentBrushNow;
		var typeface = new Typeface(FontFamily.Default);
		var halo = new Pen(HaloBrush, 3.0, lineJoin: PenLineJoin.Round);
		foreach (MarkerThumb thumb in Handles.OrderBy(thumb => thumb.IsSelectedMarker))
		{
			// the name beside the main handle; hidden by Display except a selected item's (2026-10-29, the user's design)
			if (thumb.Role is MarkerRole.ProbeB || IsDetail(thumb.Role) || (!LabelsInPreview && !thumb.IsSelectedMarker))
			{
				continue;
			}
			(int x, int y) = PointOf(thumb);
			Point centre = ToView(x, y, size);
			string name = thumb.Item switch { Source source => source.Name, OpticalPart part => part.Name, Probe probe => probe.Name, _ => "" };
			var label = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, LabelSize, thumb.IsSelectedMarker ? accent : labelBrush);
			// beside the point; below it for a composite source or a part, whose rotation handle sits beside it at 0 degrees
			bool below = thumb.Item is Source { Array: not null } or OpticalPart;
			double lx = below ? Math.Clamp(centre.X - label.Width / 2, 0, Math.Max(0, size.Width - label.Width)) : Math.Min(centre.X + LabelOffset, Math.Max(0, size.Width - label.Width));
			double ly = below ? Math.Min(centre.Y + LabelOffset, Math.Max(0, size.Height - label.Height)) : Math.Clamp(centre.Y - label.Height / 2, 0, Math.Max(0, size.Height - label.Height));
			var origin = new Point(lx, ly);
			// the text's outline in dark first (a halo), then the text
			if (label.BuildGeometry(origin) is { } outline)
			{
				dc.DrawGeometry(null, halo, outline);
			}
			dc.DrawText(label, origin);
		}
	}

	// ---------------------------------------------------------------- dropping objects onto the picture

	/// <summary>
	/// Project objects carried by a drag (upstream's ObjectsData, 2026-10-23): the text <see cref="ProjectClipboard.Write"/>
	/// makes, under the application's format or as plain text that may hold it (copied back from a text editor, which
	/// keeps text only; <see cref="ProjectClipboard.Read"/> decides, and text not starting with an element is not even
	/// parsed). Parses each text once, for the many drag-over events of one drag. The text is all a drag carries, from
	/// this window or another: the target decides what a drop does (the tree reorders its own drag, anything else pastes
	/// a copy). UI thread.
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

		/// <summary>Forgets the parsed text: a paste makes the objects the project's, so the same text is parsed afresh next time.</summary>
		public void Clear() => _cache = null;

		/// <summary>The objects' text in <paramref name="data"/>: the application's format, else plain text that starts with an element; null for neither (and when the data cannot be read).</summary>
		public static string? Text(IDataTransfer data)
		{
			try
			{
				if (data.Contains(ObjectsFormat))
				{
					return data.TryGetValue(ObjectsFormat);
				}
				return data.TryGetText() is { } text && text.TrimStart().StartsWith('<') ? text : null;
			}
			catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
			{
				// the platform's data object refused to hand its content over (another process let go of it, or a
				// format it cannot render): no objects, the drag is left for the window
				Logger.Debug(ex, "The dragged data could not be read");
				return null;
			}
		}
	}

	/// <summary>
	/// The objects a drag carries, when it carries sources, parts, probes or schemes (as Copy writes them, from this
	/// window's tree or another's); null for anything else.
	/// </summary>
	private ProjectClipboard.Clip? DroppableClip(IDataTransfer data) =>
		Project != null && _dragged.Clip(data) is { HasItems: true } clip ? clip : null;

	/// <summary>
	/// Claims a drag only when it carries droppable objects; anything else, a file above all, is left unhandled for the
	/// window, which opens a dropped file and refuses other data (2026-10-26, the user's report: handling every drag here
	/// had refused a project file over the pictures since 2026-10-23).
	/// </summary>
	private void OnDragOver(object? sender, DragEventArgs e)
	{
		if (DroppableClip(e.DataTransfer) != null)
		{
			e.DragEffects = DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	/// <summary>
	/// A drop of objects (the user's choices, 2026-10-23: from another window, and since the drag carries text alone, from
	/// this window's tree too, a drag to duplicate): pasted as Paste does, at the end of their lists, then moved as a group
	/// so the first one sits on the canvas pixel under the pointer, held inside the canvas as a group drag is; the dropped
	/// objects become the selection. One undo step.
	/// </summary>
	private void OnDrop(object? sender, DragEventArgs e)
	{
		// taken, not kept: the paste makes the clip's objects the project's; a drag without them (a file) stays unhandled
		// for the window
		if (Project is not { } project || DroppableClip(e.DataTransfer) is not { } clip)
		{
			return;
		}
		e.Handled = true;
		_dragged.Clear();
		(double x, double y) = ToCanvasExact(e.GetPosition(this));
		int canvasX = (int)Math.Floor(x);
		int canvasY = (int)Math.Floor(y);
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(project, clip, null);
		ProjectClipboard.PlaceAt(pasted, canvasX, canvasY, CanvasWidth, CanvasHeight);
		Logger.Debug("Dropped {Count} object(s) onto the picture at ({X}, {Y})", pasted.Count, canvasX, canvasY);
		if (SelectedItems is { } selection)
		{
			selection.Clear();
			foreach (object item in pasted)
			{
				selection.Add(item);
			}
		}
		SelectedItem = pasted.LastOrDefault();
	}

	// ---------------------------------------------------------------- the marquee

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		// a press on empty space (the handles are thumbs and handle their own press): maybe the start of a marquee;
		// not handled, so the pane focuses itself and clears the selection as before. The left button alone: the right
		// one opens the picture's context menu
		_marqueeStart = !e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e.GetPosition(this) : null;
	}

	/// <inheritdoc/>
	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (_marqueeStart is not { } start || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Point at = e.GetPosition(this);
		if (_marquee == null)
		{
			if (Math.Abs(at.X - start.X) < DragThreshold && Math.Abs(at.Y - start.Y) < DragThreshold)
			{
				return;
			}
			_marqueePointer = e.Pointer;
			e.Pointer.Capture(this);
		}
		_marquee = new Rect(Math.Min(start.X, at.X), Math.Min(start.Y, at.Y), Math.Abs(at.X - start.X), Math.Abs(at.Y - start.Y));
		InvalidateDrawing(Band.PartsAndLabels);
	}

	/// <inheritdoc/>
	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (_marquee is { } marquee)
		{
			EndMarquee();
			ApplyMarquee(marquee, e.KeyModifiers);
			e.Handled = true;
		}
		_marqueeStart = null;
	}

	/// <inheritdoc/>
	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		// also raised when a handle below lets its capture go: nothing to end then
		if (_marquee != null && !ReferenceEquals(e.Pointer.Captured, this))
		{
			EndMarquee();
		}
		if (_marquee == null)
		{
			_marqueeStart = null;
		}
	}

	private void EndMarquee()
	{
		_marquee = null;
		if (_marqueePointer is { } pointer)
		{
			_marqueePointer = null;
			if (ReferenceEquals(pointer.Captured, this))
			{
				pointer.Capture(null);
			}
		}
		InvalidateDrawing(Band.PartsAndLabels);
	}

	/// <summary>
	/// Selects the items whose whole-item handles lie in <paramref name="marquee"/>: with Ctrl each is toggled, with
	/// Shift they are added, else they become the selection (the pane cleared it on the press); the last one becomes
	/// the current item.
	/// </summary>
	private void ApplyMarquee(Rect marquee, KeyModifiers modifiers)
	{
		if (SelectedItems is not { } set)
		{
			return;
		}
		var covered = new List<object>();
		foreach (MarkerThumb thumb in Handles)
		{
			if (!MovesWhole(thumb.Role) || covered.Contains(thumb.Item))
			{
				continue;
			}
			(int x, int y) = PointOf(thumb);
			if (marquee.Contains(ToView(x, y)))
			{
				covered.Add(thumb.Item);
			}
		}
		if (modifiers.HasFlag(KeyModifiers.Control))
		{
			foreach (object item in covered)
			{
				if (!set.Remove(item))
				{
					set.Add(item);
				}
			}
		}
		else
		{
			if (!modifiers.HasFlag(KeyModifiers.Shift))
			{
				set.Clear();
			}
			foreach (object item in covered)
			{
				if (!set.Contains(item))
				{
					set.Add(item);
				}
			}
		}
		SelectedItem = set.LastOrDefault();
	}

	// ---------------------------------------------------------------- dragging and keys

	/// <summary>
	/// A press on a handle: with Ctrl the item is toggled in the selection, with Shift added, and no drag follows; a
	/// press on one of several selected items prepares a drag of them all; any other press makes the item the selection.
	/// </summary>
	private void OnDragStarted(object? sender, VectorEventArgs e)
	{
		if (sender is not MarkerThumb thumb)
		{
			return;
		}
		(thumb.DragStartX, thumb.DragStartY) = PointOf(thumb);
		(thumb.PressCanvasX, thumb.PressCanvasY) = ToCanvasExact(thumb.PointerPosition);
		_group = null;
		_groupApplied = (0, 0);
		_suppressDrag = false;
		_dragStartAngle = AngleOf(thumb.Item);
		if (HasCanvasPosition(thumb.Role) && CanvasOf(thumb) is { } handle)
		{
			thumb.GrabOffsetX = thumb.PressCanvasX - handle.X;
			thumb.GrabOffsetY = thumb.PressCanvasY - handle.Y;
			thumb.MirrorLength = MirrorLengthOf(thumb);
			thumb.DragAnchors = AnchorsOf(thumb);
		}
		if (MovesWhole(thumb.Role) && SelectedItems is { } set)
		{
			KeyModifiers modifiers = thumb.PointerModifiers;
			if (modifiers.HasFlag(KeyModifiers.Control))
			{
				_suppressDrag = true;
				if (set.Remove(thumb.Item))
				{
					SelectedItem = set.LastOrDefault();
					return;
				}
				set.Add(thumb.Item);
			}
			else if (modifiers.HasFlag(KeyModifiers.Shift))
			{
				_suppressDrag = true;
				if (!set.Contains(thumb.Item))
				{
					set.Add(thumb.Item);
				}
			}
			else if (set.Count > 1 && set.Contains(thumb.Item))
			{
				_group = [.. set.OfType<IMovable>()];
			}
		}
		SelectedItem = thumb.Item;
		thumb.Focus();
	}

	/// <summary>A drag that ends where it began on one of a group makes that item the selection (as a click would); a cancelled drag (Escape) puts the item or group back.</summary>
	private void OnDragCompleted(object? sender, VectorEventArgs e)
	{
		if (sender is not MarkerThumb thumb)
		{
			return;
		}
		if (thumb.DragCancelled)
		{
			if (_group != null)
			{
				Movable.MoveAll(_group, -_groupApplied.Dx, -_groupApplied.Dy);
			}
			else if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle)
			{
				SetAngle(thumb.Item, _dragStartAngle);
			}
			else if (!_suppressDrag)
			{
				SetPoint(thumb, thumb.DragStartX, thumb.DragStartY);
			}
		}
		else if (_group != null && _groupApplied == (0, 0) && SelectedItems is { } set && thumb.PointerModifiers == KeyModifiers.None)
		{
			set.Clear();
			set.Add(thumb.Item);
			SelectedItem = thumb.Item;
		}
		_group = null;
		_groupApplied = (0, 0);
		_suppressDrag = false;
	}

	private void OnDragDelta(object? sender, VectorEventArgs e)
	{
		if (sender is not MarkerThumb thumb)
		{
			return;
		}
		double scale = Scale;
		if (scale <= 0 || _suppressDrag)
		{
			return;
		}
		if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle)
		{
			// the handle follows the pointer around the centre; Shift snaps to 15 degrees
			SetAngle(thumb.Item, thumb.PointerPosition, thumb.PointerModifiers.HasFlag(KeyModifiers.Shift));
			return;
		}
		(double nowX, double nowY) = ToCanvasExact(thumb.PointerPosition);
		if (HasCanvasPosition(thumb.Role))
		{
			// absolute, from the pointer: a grip does not land under the pointer (a polygon's vertex stays on its ray, a
			// rectangle's corner on the positive quadrant), so thumb-relative deltas fed the miss back into the size
			// and a quick drag blew a hexagon up to the canvas (2026-10-19)
			SetPoint(thumb, (int)Math.Round(nowX - thumb.GrabOffsetX), (int)Math.Round(nowY - thumb.GrabOffsetY));
			return;
		}
		// the move so far, from the pointer's absolute position: the thumb's own deltas are measured from where the handle
		// is, and a handle held back by the canvas edge or a group's limit would add the miss again on every move
		// (2026-10-19: a group with a tall probe, once stopped at the top, could then only jump between the extremes)
		int movedX = (int)Math.Round(nowX - thumb.PressCanvasX);
		int movedY = (int)Math.Round(nowY - thumb.PressCanvasY);
		if (_group != null)
		{
			// the group follows the pointer as far as the canvas lets its outermost member go; the limit is taken step by
			// step on the anchors as they are, which is the same limit since the formation never changes
			int wantedX = movedX - _groupApplied.Dx;
			int wantedY = movedY - _groupApplied.Dy;
			(int dx, int dy) = Movable.Limit(_group, wantedX, wantedY, CanvasWidth, CanvasHeight);
			Movable.MoveAll(_group, dx, dy);
			_groupApplied = (_groupApplied.Dx + dx, _groupApplied.Dy + dy);
			return;
		}
		SetPoint(thumb, thumb.DragStartX + movedX, thumb.DragStartY + movedY);
	}

	/// <summary>The arrow keys move the focused handle by one canvas pixel, ten with Shift (every selected item when the handle's item is one of several); on a rotation handle they turn the array or part by a degree (Right and Up counter-clockwise), ten with Shift. Delete removes the item; Escape cancels a drag in progress.</summary>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.Source is not MarkerThumb thumb)
		{
			return;
		}
		if (e.Key == Key.Escape && thumb.IsDragging)
		{
			thumb.CancelDrag();   // raises the completed event as cancelled, which puts things back
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Delete)
		{
			// Del on a handle removes its item, or the selection it belongs to, as Del on a tree row does (2026-10-26, the user's request)
			Commands.Remove.Execute(thumb.Item);
			e.Handled = true;
			return;
		}
		int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? KeyStepFast : KeyStep;
		if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle)
		{
			int turn = e.Key switch { Key.Right or Key.Up => step, Key.Left or Key.Down => -step, _ => 0 };
			if (turn != 0)
			{
				SetAngle(thumb.Item, AngleOf(thumb.Item) + turn);
				e.Handled = true;
			}
			return;
		}
		(int dx, int dy) = e.Key switch
		{
			Key.Left => (-step, 0),
			Key.Right => (step, 0),
			Key.Up => (0, -step),
			Key.Down => (0, step),
			_ => (0, 0),
		};
		if (dx == 0 && dy == 0)
		{
			return;
		}
		if (MovesWhole(thumb.Role) && SelectedItems is { Count: > 1 } set && set.Contains(thumb.Item))
		{
			List<IMovable> group = [.. set.OfType<IMovable>()];
			(dx, dy) = Movable.Limit(group, dx, dy, CanvasWidth, CanvasHeight);
			Movable.MoveAll(group, dx, dy);
			e.Handled = true;
			return;
		}
		thumb.MirrorLength = MirrorLengthOf(thumb);
		// held from the first step while the handle keeps the focus, as a drag holds them (recomputed each step, the
		// position's rounding let the other surface creep)
		thumb.DragAnchors ??= AnchorsOf(thumb);
		(int x, int y) = PointOf(thumb);
		SetPoint(thumb, x + dx, y + dy);
		e.Handled = true;
	}
}
