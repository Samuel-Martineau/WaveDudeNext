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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NLog;
using WaveDudeNext.App.Logging;

namespace WaveDudeNext.App.Panes;

/// <summary>
/// The Messages pane: the entries kept by a <see cref="MessagesTarget"/>, filtered by level toggles and a text or
/// regex search, coloured per level, with auto-scroll, clear and copy. The target is found by
/// <see cref="TargetName"/> in the NLog configuration when the pane loads, so the pane shows what was logged before
/// it existed. Avalonia has no CollectionView: <see cref="Visible"/> is the filtered copy of the target's entries,
/// rebuilt when a filter changes and appended to as entries arrive.
/// </summary>
public partial class MessagesPane : UserControl, INotifyPropertyChanged
{
	/// <summary>Identifies the <see cref="TargetName"/> property.</summary>
	public static readonly StyledProperty<string> TargetNameProperty =
		AvaloniaProperty.Register<MessagesPane, string>(nameof(TargetName), MessagesTarget.DefaultName);

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	/// <summary>
	/// Trace and Debug start visible in Debug builds only; Info and above always do. A Release build logs no Trace at
	/// all (no rule reaches the pane's target), so its level toggles start at Debug.
	/// </summary>
#if DEBUG
	private const bool ShowDiagnosticsByDefault = true;
	private static readonly LogLevel[] PaneLevels = [LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];
#else
	private const bool ShowDiagnosticsByDefault = false;
	private static readonly LogLevel[] PaneLevels = [LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];
#endif

	/// <summary>Every level name a row may carry as its class (Themes/Messages.axaml colours the rows by it).</summary>
	private static readonly string[] LevelClasses = ["Trace", "Debug", "Info", "Warn", "Error", "Fatal"];

	private readonly ObservableCollection<LogEntry> _visible = new();
	private readonly DelegateCommand _copyCommand;
	private readonly DelegateCommand _copyAllCommand;
	private readonly DelegateCommand _clearCommand;
	private MessagesTarget? _target;
	private string _searchText = "";
	private bool _useRegex;
	private bool _autoScroll = true;
	private Regex? _regex;

	/// <summary>Creates the pane; it attaches to the target when it loads.</summary>
	public MessagesPane()
	{
		Levels = new ObservableCollection<LevelFilter>(
			PaneLevels.Select(level => new LevelFilter(level, visible: ShowDiagnosticsByDefault || level >= LogLevel.Info)));
		foreach (LevelFilter level in Levels)
		{
			level.PropertyChanged += (_, args) =>
			{
				if (args.PropertyName == nameof(LevelFilter.IsVisible))
				{
					Rebuild();
				}
			};
		}
		Visible = new ReadOnlyObservableCollection<LogEntry>(_visible);
		_copyCommand = new DelegateCommand(CopySelection, () => list.SelectedItems is { Count: > 0 });
		_copyAllCommand = new DelegateCommand(CopyAll, () => _visible.Count > 0);
		_clearCommand = new DelegateCommand(() => _target?.Clear());
		DataContext = this;
		InitializeComponent();
		list.ContainerPrepared += OnRowPrepared;
		list.SelectionChanged += (_, _) => _copyCommand.RaiseCanExecuteChanged();
		_visible.CollectionChanged += (_, _) => _copyAllCommand.RaiseCanExecuteChanged();
		Loaded += (_, _) => Attach();
	}

	/// <summary>Name of the <see cref="MessagesTarget"/> in the NLog configuration.</summary>
	public string TargetName
	{
		get => GetValue(TargetNameProperty);
		set => SetValue(TargetNameProperty, value);
	}

	/// <summary>One filter per level, in severity order; bound to the toolbar toggles.</summary>
	public ObservableCollection<LevelFilter> Levels { get; }

	/// <summary>The entries the list shows: the target's entries that pass the level and search filters, oldest first.</summary>
	public ReadOnlyObservableCollection<LogEntry> Visible { get; }

	/// <summary>Copies the selected rows, in the list's (that is, time) order, to the clipboard (Ctrl+C and the context menu).</summary>
	public ICommand CopyCommand => _copyCommand;

	/// <summary>Copies every visible row to the clipboard.</summary>
	public ICommand CopyAllCommand => _copyAllCommand;

	/// <summary>Drops every entry from the target.</summary>
	public ICommand ClearCommand => _clearCommand;

	/// <summary>Text every shown entry must contain (message, logger or exception); a regex when <see cref="UseRegex"/>.</summary>
	public string SearchText
	{
		get => _searchText;
		set
		{
			if (Set(ref _searchText, value ?? ""))
			{
				UpdateSearch();
			}
		}
	}

	/// <summary>Whether <see cref="SearchText"/> is a regular expression.</summary>
	public bool UseRegex
	{
		get => _useRegex;
		set
		{
			if (Set(ref _useRegex, value))
			{
				UpdateSearch();
			}
		}
	}

	/// <summary>Whether the list follows new entries.</summary>
	public bool AutoScroll
	{
		get => _autoScroll;
		set
		{
			if (Set(ref _autoScroll, value) && value)
			{
				ScrollToEnd();
			}
		}
	}

	// explicit, because AvaloniaObject has a PropertyChanged event of its own (for its styled properties); the
	// compiled bindings to SearchText, UseRegex and AutoScroll subscribe through the interface
	private PropertyChangedEventHandler? _propertyChanged;

	/// <inheritdoc/>
	event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
	{
		add => _propertyChanged += value;
		remove => _propertyChanged -= value;
	}

	/// <summary>The list control, for the headless tests.</summary>
	internal ListBox List => list;

	/// <summary>Puts keyboard focus on the list (its newest line when nothing is selected) once the pane is laid out.</summary>
	public void FocusList() =>
		Dispatcher.UIThread.Post(() =>
		{
			if (list.SelectedItem == null && _visible.Count > 0)
			{
				list.SelectedIndex = _visible.Count - 1;
			}
			if (list.SelectedIndex >= 0)
			{
				list.ScrollIntoView(list.SelectedIndex);
			}
			// the row's container exists only once the virtualising panel has realised it; the list itself is the
			// fallback, and the arrow keys reach the selection from there
			if (list.SelectedIndex >= 0 && list.ContainerFromIndex(list.SelectedIndex) is ListBoxItem row)
			{
				row.Focus();
			}
			else
			{
				list.Focus();
			}
		}, DispatcherPriority.Background);

	/// <summary>The clipboard text of Copy all: every visible row's <see cref="LogEntry.ToLine"/>, one per line.</summary>
	internal string CopyAllText() => LinesOf(_visible);

	private static string LinesOf(IEnumerable<LogEntry> entries)
	{
		var text = new StringBuilder();
		foreach (LogEntry entry in entries)
		{
			text.AppendLine(entry.ToLine());
		}
		return text.ToString();
	}

	private void Attach()
	{
		if (_target != null)
		{
			return;
		}
		_target = MessagesTarget.Find(TargetName);
		if (_target == null)
		{
			// nothing to show and nowhere to say so: the pane stays empty until a target of that name exists and the
			// pane loads again (the dock host re-parents panes, which raises Loaded)
			return;
		}
		((INotifyCollectionChanged)_target.Entries).CollectionChanged += OnEntriesChanged;
		Rebuild();
	}

	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add:
				foreach (LogEntry entry in e.NewItems!)
				{
					Count(entry, +1);
					if (Passes(entry))
					{
						_visible.Add(entry);
					}
				}
				if (AutoScroll)
				{
					ScrollToEnd();
				}
				break;
			case NotifyCollectionChangedAction.Remove:
				foreach (LogEntry entry in e.OldItems!)
				{
					Count(entry, -1);
					// the target drops its oldest entry, which is the first visible one when it is shown at all
					if (_visible.Count > 0 && ReferenceEquals(_visible[0], entry))
					{
						_visible.RemoveAt(0);
					}
					else
					{
						_visible.Remove(entry);
					}
				}
				break;
			case NotifyCollectionChangedAction.Reset:
				foreach (LevelFilter level in Levels)
				{
					level.Count = 0;
				}
				_visible.Clear();
				break;
			case NotifyCollectionChangedAction.Replace:
			case NotifyCollectionChangedAction.Move:
				// the target only appends, drops from the front and clears; anything else means the target changed
				// and the counts can no longer be trusted, so start over from its entries
				Rebuild();
				break;
		}
	}

	private void Count(LogEntry entry, int delta)
	{
		LevelFilter? level = Levels.FirstOrDefault(l => l.Level == entry.Level);
		if (level != null)
		{
			level.Count += delta;
		}
	}

	private bool Passes(LogEntry entry)
	{
		if (Levels.FirstOrDefault(l => l.Level == entry.Level) is { IsVisible: false })
		{
			return false;
		}
		if (UseRegex)
		{
			// no pattern, or one that does not parse yet (an unfinished "[" while typing): nothing to filter by, so
			// everything shows until it parses. Upstream's comment says the same; its code fell through to the
			// Contains below and hid every line without a literal "[" in it, which the port does not reproduce.
			return _regex == null || _regex.IsMatch(entry.FullMessage) || _regex.IsMatch(entry.Logger);
		}
		return SearchText.Length == 0
			|| entry.FullMessage.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
			|| entry.Logger.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateSearch()
	{
		_regex = null;
		if (UseRegex && SearchText.Length > 0)
		{
			try
			{
				_regex = new Regex(SearchText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			}
			catch (ArgumentException)
			{
				// an unfinished pattern while typing: show everything until it parses
			}
		}
		Rebuild();
	}

	/// <summary>Recounts and refilters from the target's entries; a rebuild loses the scroll position, so follow the end again when asked to.</summary>
	private void Rebuild()
	{
		foreach (LevelFilter level in Levels)
		{
			level.Count = 0;
		}
		_visible.Clear();
		if (_target != null)
		{
			foreach (LogEntry entry in _target.Entries)
			{
				Count(entry, +1);
				if (Passes(entry))
				{
					_visible.Add(entry);
				}
			}
		}
		if (AutoScroll)
		{
			ScrollToEnd();
		}
	}

	private void ScrollToEnd() =>
		Dispatcher.UIThread.Post(() =>
		{
			if (_visible.Count > 0)
			{
				list.ScrollIntoView(_visible.Count - 1);
			}
		}, DispatcherPriority.Background);

	/// <summary>Marks a realised (or recycled) row with its entry's level, so the row theme colours it.</summary>
	private void OnRowPrepared(object? sender, ContainerPreparedEventArgs e)
	{
		if (e.Container is not ListBoxItem row)
		{
			return;
		}
		string? levelName = e.Index >= 0 && e.Index < _visible.Count ? _visible[e.Index].LevelName : null;
		foreach (string name in LevelClasses)
		{
			row.Classes.Set(name, name == levelName);
		}
	}

	/// <summary>
	/// Copies the selection in the order of the list, which is the order of logging. Sorting by <see cref="LogEntry.Time"/>
	/// (upstream's way) cannot tell apart entries NLog stamped within the same cached millisecond, and would leave
	/// those in the order they were clicked. Reference equality, because equal-valued records are distinct lines.
	/// </summary>
	private void CopySelection()
	{
		if (list.SelectedItems is { Count: > 0 } selectedItems)
		{
			var selected = new HashSet<LogEntry>(selectedItems.OfType<LogEntry>(), ReferenceEqualityComparer.Instance);
			CopyToClipboard(LinesOf(_visible.Where(selected.Contains)));
		}
	}

	private void CopyAll() => CopyToClipboard(CopyAllText());

	private async void CopyToClipboard(string text)
	{
		IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard == null)
		{
			Log.Warn("Copy: the Messages pane has no window, nothing was copied");
			return;
		}
		try
		{
			await clipboard.SetTextAsync(text);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// the clipboard is a platform call whose failures share no exception type (X11, Wayland, Win32 each throw
			// their own); the user asked for a copy and gets told here, in the pane, that it did not happen
			Log.Error(ex, "Copy to the clipboard failed");
		}
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		OnPropertyChanged(name);
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		_propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Visibility toggle and entry count of one log level in the Messages pane.</summary>
public sealed class LevelFilter : INotifyPropertyChanged
{
	private bool _isVisible;
	private int _count;

	/// <param name="level">The level this filter stands for.</param>
	/// <param name="visible">Initial state of the toggle.</param>
	public LevelFilter(LogLevel level, bool visible)
	{
		Level = level;
		_isVisible = visible;
	}

	/// <summary>The level this filter stands for.</summary>
	public LogLevel Level { get; }

	/// <summary>Level name for the toggle caption.</summary>
	public string Name => Level.Name;

	/// <summary>The toggle's text, "Name (count)".</summary>
	public string Caption => $"{Name} ({Count})";

	/// <summary>The toggle's automation id, "MessagesLevel:Name", as upstream's ui_check scripts look it up.</summary>
	public string AutomationId => "MessagesLevel:" + Name;

	/// <summary>Whether entries of this level are shown.</summary>
	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			if (_isVisible != value)
			{
				_isVisible = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>Number of kept entries of this level, shown or not.</summary>
	public int Count
	{
		get => _count;
		set
		{
			if (_count != value)
			{
				_count = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(Caption));
			}
		}
	}

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// An <see cref="ICommand"/> over two delegates, for the pane's Copy, Copy all and Clear (WPF's RoutedUICommand has no
/// Avalonia counterpart; the application has no MVVM library, by choice). Kept here until a second pane needs one.
/// </summary>
internal sealed class DelegateCommand : ICommand
{
	private readonly Action _execute;
	private readonly Func<bool>? _canExecute;

	/// <param name="execute">What the command does.</param>
	/// <param name="canExecute">Whether it may run now; null for always.</param>
	public DelegateCommand(Action execute, Func<bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged;

	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

	/// <inheritdoc/>
	public void Execute(object? parameter)
	{
		if (CanExecute(parameter))
		{
			_execute();
		}
	}

	/// <summary>Tells the bound controls to ask <see cref="CanExecute"/> again.</summary>
	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
