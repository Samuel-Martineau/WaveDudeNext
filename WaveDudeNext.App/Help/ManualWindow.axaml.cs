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

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NLog;
using WaveDudeNext.App.Theming;

namespace WaveDudeNext.App.Help;

/// <summary>
/// The manual window (Help > Manual, F1): the embedded MANUAL.md rendered by <see cref="MarkdownDocument"/> beside a
/// contents tree of its chapters and their sections, every branch open; the title, the one level-1 heading, is no row.
/// The window opens with the text at its top and the first chapter's row taken; taking a row goes to its heading, which
/// then stands at the top of the text; a link to "#anchor" goes to that heading, a web link opens in the browser. Not
/// modal, but owned by the main window (the user's choice): minimised and closed with it, kept above it, opened once
/// (<see cref="ShowOrActivate"/>). Upstream reloaded its WPF document when the UI zoom changed the text formatting
/// mode; Avalonia has one text renderer, so there is nothing to reload. UI thread only.
/// </summary>
public partial class ManualWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static ManualWindow? _instance;
	private readonly MarkdownDocument _document;
	/// <summary>Set while the first row is taken on opening, so the text stays at its top instead of going to the heading.</summary>
	private bool _opening;

	/// <summary>Renders the manual and its headings; the window opens with the text at its top and the first row taken, the tree focused.</summary>
	public ManualWindow()
	{
		long start = Stopwatch.GetTimestamp();
		string markdown = MarkdownDocument.ReadEmbedded("MANUAL.md") ?? "MANUAL.md is not embedded in this build.";
		_document = MarkdownDocument.Render(markdown, new MarkdownRenderOptions { TightLists = true, Name = "MANUAL.md" });
		double rendered = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
		_document.LinkActivated += OnLink;
		IReadOnlyList<ManualHeading> all = _document.Contents();
		// the chapters are the rows: the title, the document's one level-1 heading, would be a lone root that indents
		// every other row, and the title bar names the manual already
		Headings = all is [{ Level: 1 } title] ? title.Children : all;
		InitializeComponent();
		documentView.Content = _document.Root;
		contents.ItemsSource = Headings;
		contents.SelectionChanged += OnContentsSelected;
		contents.AddHandler(KeyDownEvent, OnContentsKeyDown, RoutingStrategies.Bubble);
		contents.AddHandler(PointerReleasedEvent, OnContentsPointerReleased, RoutingStrategies.Bubble);
		AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
		Opened += (_, _) => Dispatcher.UIThread.Post(TakeFirstRow, DispatcherPriority.Loaded);
		Opened += (_, _) => Logger.Debug("Manual open: {Headings} headings, {Figures} figures, rendered in {Rendered:0} ms, on screen after {Shown:0} ms",
			_document.Headings.Count, _document.FigureCount, rendered, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
		Closed += (_, _) =>
		{
			if (ReferenceEquals(_instance, this))
			{
				_instance = null;
			}
		};
		// the UI zoom on every window of the app, as upstream's Window style applied it
		WindowZoom.Attach(this);
	}

	/// <summary>The contents tree's rows: the manual's chapters, each with its sections under it.</summary>
	public IReadOnlyList<ManualHeading> Headings { get; }

	/// <summary>The rendered manual, for the tests and the window's own navigation.</summary>
	public MarkdownDocument Document => _document;

	/// <summary>
	/// Opens the manual owned by <paramref name="owner"/>, or brings the open one forward: there is one manual window at
	/// a time, as upstream kept it (MainWindow.Help.cs).
	/// </summary>
	public static ManualWindow ShowOrActivate(Window owner)
	{
		if (_instance == null)
		{
			_instance = new ManualWindow();
			_instance.Show(owner);
		}
		else
		{
			_instance.Activate();
		}
		return _instance;
	}

	/// <summary>The first row taken without going there: the text opens at its top, the title in view above the first chapter.</summary>
	private void TakeFirstRow()
	{
		if (Headings.Count == 0)
		{
			return;
		}
		_opening = true;
		contents.SelectedItem = Headings[0];
		_opening = false;
		if (contents.ContainerFromItem(Headings[0]) is TreeViewItem first)
		{
			first.Focus();
		}
		else
		{
			contents.Focus();
		}
	}

	/// <summary>Esc closes even while the text has the focus.</summary>
	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
	}

	private void OnContentsSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (!_opening && contents.SelectedItem is ManualHeading heading)
		{
			GoTo(heading);
		}
	}

	/// <summary>Enter on a row goes to its heading again and moves the focus into the text, to read on with the keys.</summary>
	private void OnContentsKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && contents.SelectedItem is ManualHeading heading)
		{
			GoTo(heading);
			documentView.Focus();
			e.Handled = true;
		}
	}

	/// <summary>
	/// A click on the row already taken goes to its heading again (a click on another row selects it, which goes there);
	/// the innermost row takes the click, the expander's chevron is no part of it.
	/// </summary>
	private void OnContentsPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton != MouseButton.Left)
		{
			return;
		}
		TreeViewItem? row = null;
		for (Visual? element = e.Source as Visual; element != null && element != contents; element = element.GetVisualParent())
		{
			if (element is ToggleButton)
			{
				return;
			}
			if (element is TreeViewItem item)
			{
				row = item;
				break;
			}
		}
		if (row is { IsSelected: true, DataContext: ManualHeading heading } && ReferenceEquals(contents.SelectedItem, heading))
		{
			GoTo(heading);
		}
	}

	/// <summary>
	/// Scrolls the text so the heading stands the page's padding below the top, the end of the section before it out of
	/// sight (its own margin above would show it); offsets go to whole pixels, so a screenshot that fits the column
	/// stays drawn 1:1.
	/// </summary>
	public void GoTo(ManualHeading heading)
	{
		Point? top = heading.Block.TranslatePoint(new Point(0, 0), documentView);
		if (top == null)
		{
			heading.Block.BringIntoView();
			return;
		}
		double padding = _document.Root.Margin.Top;
		double offset = documentView.Offset.Y + top.Value.Y - padding;
		double most = Math.Max(0, documentView.Extent.Height - documentView.Viewport.Height);
		documentView.Offset = new Vector(documentView.Offset.X, Math.Round(Math.Clamp(offset, 0, most)));
	}

	/// <summary>A link: "#anchor" goes to that heading, anything else opens in the browser.</summary>
	private void OnLink(object? sender, MarkdownLinkEventArgs e)
	{
		if (e.Target.StartsWith('#'))
		{
			if (_document.Find(e.Target[1..]) is { } heading)
			{
				GoTo(heading);
			}
			else
			{
				Logger.Debug("Manual: no heading for the link {Target}", e.Target);
			}
			return;
		}
		AboutWindow.OpenInBrowser(e.Target);
	}
}
