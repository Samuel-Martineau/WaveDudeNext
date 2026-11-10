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

using Avalonia.Threading;
using NLog;
using NLog.Targets;

namespace WaveDudeNext.App.Logging;

/// <summary>One line the status bar shows: the message with the exception's message in brackets, and its level for the colour.</summary>
/// <param name="Level">The log level: Warn and Error recolour the bar.</param>
/// <param name="Text">The rendered message.</param>
/// <param name="Exception">The exception, shown as the tooltip.</param>
public sealed record StatusLine(LogLevel Level, string Text, Exception? Exception);

/// <summary>
/// The NLog target behind the status bar (upstream's StatusBarTarget): every entry of Info and above becomes the
/// status bar's text, warnings on orange, errors on red, the exception as the tooltip. Upstream's target found the
/// window's TextBlock by name; this one hands the line to whoever <see cref="Attach"/>ed (the main window), on the UI
/// thread, and keeps the last line so a window created after the first entries shows the latest start-up line.
/// </summary>
[Target("StatusBar")]
public sealed class StatusBarTarget : TargetWithLayout
{
	/// <summary>The target's name in the NLog configuration.</summary>
	public const string TargetName = "statusbar";

	private static readonly object Gate = new();
	private static Action<StatusLine>? _sink;
	private static StatusLine? _last;

	/// <summary>The last line written, for a window that attaches late; null before the first.</summary>
	public static StatusLine? Last
	{
		get
		{
			lock (Gate)
			{
				return _last;
			}
		}
	}

	/// <summary>Routes the lines to <paramref name="sink"/> on the UI thread; the last line so far is delivered at once.</summary>
	public static void Attach(Action<StatusLine> sink)
	{
		StatusLine? last;
		lock (Gate)
		{
			_sink = sink;
			last = _last;
		}
		if (last != null)
		{
			sink(last);
		}
	}

	/// <summary>
	/// Stops routing the lines to <paramref name="sink"/> (its window closed); a sink attached since, by a newer window,
	/// stays. A window's close can complete after the next window attached (the save question closes asynchronously),
	/// and clearing whatever was attached left that newer window's status bar on "Ready".
	/// </summary>
	public static void Detach(Action<StatusLine> sink)
	{
		lock (Gate)
		{
			if (Equals(_sink, sink))
			{
				_sink = null;
			}
		}
	}

	/// <summary>Adds the target to <paramref name="config"/> for Info and above.</summary>
	public static StatusBarTarget Install(NLog.Config.LoggingConfiguration config)
	{
		var target = new StatusBarTarget
		{
			Name = TargetName,
			Layout = "${message}${onexception: (${exception:format=message})}",
		};
		config.AddTarget(target);
		config.AddRule(LogLevel.Info, LogLevel.Fatal, target);
		return target;
	}

	/// <inheritdoc />
	protected override void Write(LogEventInfo logEvent)
	{
		var line = new StatusLine(logEvent.Level, RenderLogEvent(Layout, logEvent), logEvent.Exception);
		Action<StatusLine>? sink;
		lock (Gate)
		{
			_last = line;
			sink = _sink;
		}
		if (sink == null)
		{
			return;
		}
		if (Dispatcher.UIThread.CheckAccess())
		{
			sink(line);
		}
		else
		{
			// Background: a flood of lines during a run must not starve rendering and input
			Dispatcher.UIThread.Post(() => sink(line), DispatcherPriority.Background);
		}
	}
}
