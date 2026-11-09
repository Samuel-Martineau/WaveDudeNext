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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace WaveDudeNext.App.Logging;

/// <summary>
/// NLog target that keeps the last <see cref="MaxCount"/> log events in memory for the Messages pane. Events may
/// arrive on any thread (the simulation runs on its own); they are queued and moved into <see cref="Entries"/> on the
/// UI thread in batches every 100 ms, so a burst of logging does not stall the UI. The pane finds the target by name
/// (<see cref="Find"/>); <see cref="Install(LogLevel)"/> registers it the way upstream's App.xaml.cs did.
/// </summary>
[Target("Messages")]
public sealed class MessagesTarget : Target
{
	/// <summary>The name the target is registered under and the pane looks for: "messages", as upstream.</summary>
	public const string DefaultName = "messages";

	private readonly ObservableCollection<LogEntry> _entries = new();
	private readonly ConcurrentQueue<LogEntry> _pending = new();
	private readonly Dispatcher _dispatcher;
	private readonly DispatcherTimer _flush;
	private int _maxCount = 5000;

	/// <summary>
	/// Creates the target bound to the UI thread's dispatcher. Avalonia 12 binds a timer to the current thread's
	/// dispatcher by default, so the UI dispatcher is named explicitly: the target may be created from any thread
	/// once Avalonia is initialised, and the flush always lands on the UI thread.
	/// </summary>
	public MessagesTarget()
	{
		Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);
		_dispatcher = Dispatcher.UIThread;
		_flush = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, _dispatcher);
		_flush.Tick += (_, _) => Flush();
	}

	/// <summary>Upper bound of kept entries; the oldest are dropped beyond it. At least 1.</summary>
	public int MaxCount
	{
		get => _maxCount;
		set => _maxCount = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "MaxCount must be at least 1.");
	}

	/// <summary>The kept entries, oldest first. UI thread only.</summary>
	public ReadOnlyObservableCollection<LogEntry> Entries { get; }

	/// <summary>
	/// The lowest level the Messages pane receives, as upstream's App.xaml.cs rules it: everything in a Debug build;
	/// Debug and above in a Release build, where Trace is not logged at all (the presenter traces every frame it
	/// draws, and a Release build must not pay for entries nobody can see).
	/// </summary>
	public static LogLevel DefaultMinimum =>
#if DEBUG
		LogLevel.Trace;
#else
		LogLevel.Debug;
#endif

	/// <summary>
	/// Adds a target named <see cref="DefaultName"/> to NLog's configuration (creating the configuration when there
	/// is none) with a rule from <paramref name="minimum"/> to Fatal, and applies it. Idempotent: an installed target
	/// is returned as it is. Call it once Avalonia is initialised, since the target's timer needs the UI dispatcher.
	/// </summary>
	/// <param name="minimum">The lowest level routed into the pane; <see cref="DefaultMinimum"/> for upstream's rule.</param>
	/// <returns>The installed target.</returns>
	public static MessagesTarget Install(LogLevel minimum)
	{
		LoggingConfiguration? existingConfig = LogManager.Configuration;
		if (existingConfig?.FindTargetByName<MessagesTarget>(DefaultName) is { } installed)
		{
			return installed;
		}
		LoggingConfiguration config = existingConfig ?? new LoggingConfiguration();
		var target = new MessagesTarget { Name = DefaultName };
		config.AddTarget(target);
		config.AddRule(minimum, LogLevel.Fatal, target);
		if (existingConfig == null)
		{
			LogManager.Configuration = config;
		}
		else
		{
			// the configuration is live: NLog initialises the new target and re-evaluates the loggers' rules only
			// when told, and assigning the same instance back would close and reopen every other target too
			LogManager.ReconfigExistingLoggers();
		}
		return target;
	}

	/// <summary>Installs the target with <see cref="DefaultMinimum"/>: Trace in a Debug build, Debug in a Release build.</summary>
	/// <returns>The installed target.</returns>
	public static MessagesTarget Install() => Install(DefaultMinimum);

	/// <summary>The installed target of that name in NLog's configuration, or null when there is none.</summary>
	/// <param name="name">The target name; <see cref="DefaultName"/> unless a pane names another.</param>
	public static MessagesTarget? Find(string name = DefaultName) => LogManager.Configuration?.FindTargetByName<MessagesTarget>(name);

	/// <summary>Drops every kept and every pending entry. UI thread only.</summary>
	public void Clear()
	{
		_dispatcher.VerifyAccess();
		while (_pending.TryDequeue(out _))
		{
		}
		_entries.Clear();
	}

	/// <summary>
	/// Moves the pending entries into <see cref="Entries"/> now and trims to <see cref="MaxCount"/>; the timer calls
	/// it every 100 ms while entries are pending, a test calls it to observe the result without waiting. UI thread only.
	/// </summary>
	public void Flush()
	{
		_dispatcher.VerifyAccess();
		_flush.Stop();
		while (_pending.TryDequeue(out LogEntry? entry))
		{
			_entries.Add(entry);
		}
		while (_entries.Count > _maxCount)
		{
			_entries.RemoveAt(0);
		}
	}

	/// <inheritdoc/>
	protected override void Write(LogEventInfo logEvent)
	{
		_pending.Enqueue(new LogEntry(logEvent.TimeStamp, logEvent.Level, logEvent.LoggerName ?? "", logEvent.FormattedMessage, logEvent.Exception?.ToString()));
		// Start() locks instead of checking the thread, so a worker's log call may arm the timer; a tick on the UI
		// thread drains whatever has queued up by then
		if (!_flush.IsEnabled)
		{
			_flush.Start();
		}
	}

	/// <inheritdoc/>
	protected override void CloseTarget()
	{
		_flush.Stop();
		base.CloseTarget();
	}
}

/// <summary>One line of the Messages pane.</summary>
/// <param name="Time">When the event was logged.</param>
/// <param name="Level">Severity.</param>
/// <param name="Logger">Full logger name, usually the class.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception with stack trace, or null.</param>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Logger, string Message, string? Exception)
{
	/// <summary>Level name as shown in the pane ("Info", "Warn", ...).</summary>
	public string LevelName => Level.Name;

	/// <summary>Logger name without its namespace.</summary>
	public string LoggerShort => Logger[(Logger.LastIndexOf('.') + 1)..];

	/// <summary>Message plus the exception text, for display and copy.</summary>
	public string FullMessage => Exception == null ? Message : $"{Message}\n{Exception}";

	/// <summary>Tab-separated line for the clipboard.</summary>
	public string ToLine() => $"{Time:HH:mm:ss.fff}\t{LevelName}\t{Logger}\t{FullMessage}";
}
