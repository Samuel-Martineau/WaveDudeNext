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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NLog;
using WaveDudeNext.App.Theming;
using WaveDudeNext.Engine;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Dialogs;

/// <summary>What the memory warning was answered with.</summary>
/// <param name="Quality">The preview quality to run at: the current one with each ticked lever a notch lower, the current one itself when nothing was ticked.</param>
/// <param name="CacheAuto">"RAM cache size to Auto" was ticked: the caller sets the share to automatic.</param>
/// <param name="Mute">"Do not warn me again" was ticked: the caller clears the setting.</param>
public sealed record MemoryWarningChoice(PreviewQuality Quality, bool CacheAuto, bool Mute);

/// <summary>The four levers of the memory warning as ticks or as availability, in their order on screen.</summary>
/// <param name="Cache">The RAM cache size to Auto.</param>
/// <param name="Resolution">Halve the resolution.</param>
/// <param name="Frames">Store half the frames.</param>
/// <param name="Samples">8-bit samples.</param>
public readonly record struct MemoryWarningLevers(bool Cache, bool Resolution, bool Frames, bool Samples)
{
	/// <summary>How many levers are set.</summary>
	public int Count => (Cache ? 1 : 0) + (Resolution ? 1 : 0) + (Frames ? 1 : 0) + (Samples ? 1 : 0);
}

/// <summary>
/// The memory warning, as upstream's (2026-11-03, the user's design): shown before a preview run whose frames would not
/// all fit in the RAM cache (Simulation > Warn when a run does not fit in RAM). Names the run's need and the cache's
/// budget, offers a lever that sets the RAM cache size to Auto (the budget rises; greyed when that gains less than a
/// frame) and three that each lower the preview quality a notch (half the resolution, half the frames, 8-bit samples),
/// pre-ticked with the fewest that make the run fit and the mildest among as many (<see cref="Proposal"/>), a line under
/// them with the ticks' result, and "Do not warn me again". Run completes the task with the choice, Cancel, Esc or the
/// close box with null; the caller applies it. The arithmetic is <see cref="RunSpec.StoreBytesAt"/>. UI thread only.
/// </summary>
public partial class MemoryWarningWindow : Window
{
	/// <summary>The largest divisor the View menu offers: a lever there has no notch left.</summary>
	public const int MaxDivisor = 4;

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private readonly RunSpec? _spec;
	private readonly long _budget;
	private readonly long _autoBudget;
	private readonly PreviewQuality _current = PreviewQuality.Full;

	/// <summary>The parameterless constructor the XAML loader needs; <see cref="Show"/> builds the real window.</summary>
	public MemoryWarningWindow()
	{
		InitializeComponent();
	}

	/// <summary>Builds the window for a run; internal so the tests can show it without a modal owner.</summary>
	internal MemoryWarningWindow(RunSpec spec, long budget, long autoBudget, PreviewQuality current)
	{
		InitializeComponent();
		_spec = spec;
		_budget = budget;
		_autoBudget = autoBudget;
		_current = current;
		messageText.Text = string.Format(DialogStrings.Lookup("MemoryWarningTextFormat"), FrameMemory.Gigabytes(spec.StoreBytes), FrameMemory.Gigabytes(budget));
		cacheBox.Content = string.Format(DialogStrings.Lookup("MemoryWarningCacheFormat"), FrameMemory.Gigabytes(autoBudget));
		MemoryWarningLevers available = Available(spec, budget, autoBudget, current);
		cacheBox.IsEnabled = available.Cache;
		resolutionBox.IsEnabled = available.Resolution;
		framesBox.IsEnabled = available.Frames;
		samplesBox.IsEnabled = available.Samples;
		MemoryWarningLevers proposal = Proposal(spec, budget, autoBudget, current);
		cacheBox.IsChecked = proposal.Cache;
		resolutionBox.IsChecked = proposal.Resolution;
		framesBox.IsChecked = proposal.Frames;
		samplesBox.IsChecked = proposal.Samples;
		foreach (CheckBox box in Levers)
		{
			box.IsCheckedChanged += (_, _) => ShowResult();
		}
		ShowResult();
		Opened += (_, _) => (Levers.FirstOrDefault(box => box.IsEnabled) ?? (InputElement)runButton).Focus();
		AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
		// the UI zoom on every window of the app, as upstream's Window style applied it
		WindowZoom.Attach(this);
	}

	/// <summary>The choice Run was pressed with; null until then, and after Cancel.</summary>
	public MemoryWarningChoice? Choice { get; private set; }

	/// <summary>The lever boxes in their order on screen.</summary>
	private CheckBox[] Levers => [cacheBox, resolutionBox, framesBox, samplesBox];

	/// <summary>
	/// Shows the warning modally over <paramref name="owner"/> for a run of <paramref name="spec"/> whose frames exceed
	/// <paramref name="budget"/> bytes of memory, <paramref name="autoBudget"/> being the budget with the RAM cache size at
	/// Auto; <paramref name="current"/> is the preview quality the spec was made with.
	/// </summary>
	/// <returns>A task completing with the choice Run was pressed with, or null for Cancel, Esc or the close box.</returns>
	public static async Task<MemoryWarningChoice?> Show(Window owner, RunSpec spec, long budget, long autoBudget, PreviewQuality current)
	{
		Logger.Debug("Memory warning: the run's frames need {Need:0.0} GB, the RAM cache has {Budget:0.0} GB ({Auto:0.0} GB at Auto)",
			FrameMemory.ToGigabytes(spec.StoreBytes), FrameMemory.ToGigabytes(budget), FrameMemory.ToGigabytes(autoBudget));
		var window = new MemoryWarningWindow(spec, budget, autoBudget, current);
		await window.ShowDialog(owner);
		if (window.Choice == null)
		{
			Logger.Debug("Memory warning answered: cancelled");
		}
		else
		{
			Logger.Debug("Memory warning answered: run at {Quality} ({Need:0.0} GB), RAM cache to Auto: {Cache}, warn again: {Again}",
				window.Choice.Quality, FrameMemory.ToGigabytes(spec.StoreBytesAt(window.Choice.Quality)), window.Choice.CacheAuto, !window.Choice.Mute);
		}
		return window.Choice;
	}

	/// <summary>
	/// Which levers have something to offer: the cache when Auto gains at least a frame (the share may be Auto already,
	/// or the free memory binds it, which two readings of the memory status can differ about by a few kilobytes), a
	/// quality lever that is not at its lowest notch, 8-bit samples only when the frames are halves now.
	/// </summary>
	public static MemoryWarningLevers Available(RunSpec spec, long budget, long autoBudget, PreviewQuality current) => new(
		Cache: autoBudget - budget >= FrameStore.MemoryOf(spec.Shape, spec.Layers, 1),
		Resolution: current.ResolutionDivisor < MaxDivisor,
		Frames: current.FrameDivisor < MaxDivisor,
		Samples: current.Format == FrameFormat.Half);

	/// <summary>The quality a set of ticks stands for: <paramref name="current"/> with each ticked divisor doubled (to the View menu's largest) and the samples to palette indices.</summary>
	public static PreviewQuality QualityOf(PreviewQuality current, bool resolution, bool frames, bool samples) => current with
	{
		ResolutionDivisor = resolution ? Math.Min(current.ResolutionDivisor * 2, MaxDivisor) : current.ResolutionDivisor,
		FrameDivisor = frames ? Math.Min(current.FrameDivisor * 2, MaxDivisor) : current.FrameDivisor,
		Format = samples ? FrameFormat.Index : current.Format,
	};

	/// <summary>
	/// The ticks to propose: among the combinations of the levers that have something to offer, the fewest ticks that
	/// make the run fit, and the mildest among as many (the largest need that still fits, so the cache, which leaves the
	/// need as it is, comes before any loss of quality); a tie goes to the first-start ladder's order, 8-bit samples
	/// before half the frames before half the resolution (the mask's bits, the sort being stable); every lever that can
	/// when none does.
	/// </summary>
	public static MemoryWarningLevers Proposal(RunSpec spec, long budget, long autoBudget, PreviewQuality current)
	{
		MemoryWarningLevers available = Available(spec, budget, autoBudget, current);
		var fitting = new List<(MemoryWarningLevers Levers, long Bytes)>();
		for (int mask = 1; mask < 16; mask++)
		{
			var levers = new MemoryWarningLevers(Cache: (mask & 8) != 0, Resolution: (mask & 4) != 0, Frames: (mask & 2) != 0, Samples: (mask & 1) != 0);
			if ((levers.Cache && !available.Cache) || (levers.Resolution && !available.Resolution) || (levers.Frames && !available.Frames) || (levers.Samples && !available.Samples))
			{
				continue;
			}
			long bytes = spec.StoreBytesAt(QualityOf(current, levers.Resolution, levers.Frames, levers.Samples));
			if (bytes <= (levers.Cache ? autoBudget : budget))
			{
				fitting.Add((levers, bytes));
			}
		}
		if (fitting.Count == 0)
		{
			return available;
		}
		return fitting.OrderBy(c => c.Levers.Count).ThenByDescending(c => c.Bytes).First().Levers;
	}

	/// <summary>The line under the levers for a set of ticks: what the choice needs, and whether it fits the ticked budget or how much of it still goes to disk.</summary>
	public static string ResultText(RunSpec spec, long budget, long autoBudget, PreviewQuality current, MemoryWarningLevers ticks)
	{
		long bytes = spec.StoreBytesAt(QualityOf(current, ticks.Resolution, ticks.Frames, ticks.Samples));
		long chosenBudget = ticks.Cache ? autoBudget : budget;
		return bytes <= chosenBudget
			? string.Format(DialogStrings.Lookup("MemoryWarningFitsFormat"), FrameMemory.Gigabytes(bytes))
			: string.Format(DialogStrings.Lookup("MemoryWarningSpillsFormat"), FrameMemory.Gigabytes(bytes), FrameMemory.Gigabytes(bytes - chosenBudget));
	}

	/// <summary>The levers as ticked right now.</summary>
	private MemoryWarningLevers Ticks => new(cacheBox.IsChecked == true, resolutionBox.IsChecked == true, framesBox.IsChecked == true, samplesBox.IsChecked == true);

	private void ShowResult()
	{
		if (_spec != null)
		{
			resultText.Text = ResultText(_spec, _budget, _autoBudget, _current, Ticks);
		}
	}

	private void OnRun(object? sender, RoutedEventArgs e)
	{
		MemoryWarningLevers ticks = Ticks;
		Choice = new MemoryWarningChoice(QualityOf(_current, ticks.Resolution, ticks.Frames, ticks.Samples), ticks.Cache, muteBox.IsChecked == true);
		Close();
	}

	private void OnCancel(object? sender, RoutedEventArgs e)
	{
		Choice = null;
		Close();
	}

	/// <summary>Esc cancels wherever the focus is; a bare letter presses the button it underlines.</summary>
	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Choice = null;
			Close();
		}
		else if (DialogStrings.TryPressAccessLetter(this, e))
		{
			e.Handled = true;
		}
	}
}
