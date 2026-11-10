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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using NLog;
using WaveDudeNext.App.Theming;

namespace WaveDudeNext.App.Dialogs;

/// <summary>What a <see cref="MessageWindow"/> reports: an error (red disc) or a question (accent disc).</summary>
public enum MessageKind
{
	/// <summary>Something failed; the text names it.</summary>
	Error,
	/// <summary>A decision the user has to take.</summary>
	Question,
}

/// <summary>The button sets a <see cref="MessageWindow"/> offers.</summary>
public enum MessageButtons
{
	/// <summary>OK alone; Enter, Esc and the close box all give <see cref="MessageAnswer.Ok"/>.</summary>
	Ok,
	/// <summary>Yes (default) and No (Esc and the close box).</summary>
	YesNo,
	/// <summary>Save (default), Don't save, and Cancel (Esc and the close box).</summary>
	SaveDiscardCancel,
}

/// <summary>The button a <see cref="MessageWindow"/> was closed with.</summary>
public enum MessageAnswer
{
	/// <summary>Cancel, Esc or the close box of a <see cref="MessageButtons.SaveDiscardCancel"/> window.</summary>
	Cancel,
	/// <summary>OK, or any way of closing a <see cref="MessageButtons.Ok"/> window.</summary>
	Ok,
	/// <summary>Yes.</summary>
	Yes,
	/// <summary>No, Esc or the close box of a <see cref="MessageButtons.YesNo"/> window.</summary>
	No,
	/// <summary>Save.</summary>
	Save,
	/// <summary>Don't save.</summary>
	Discard,
}

/// <summary>
/// The application's message box, as upstream's: a modal window owned by its caller in the theme's colours, the text
/// wrapped beside an error or question disc, and the buttons of a <see cref="MessageButtons"/> set labelled from the
/// string tables (<c>Dialog*</c> keys), so they speak the app's language, not the desktop's. Enter takes the default
/// button, Esc the cancel button, the underlined letters work without Alt; the buttons carry <c>Message&lt;Answer&gt;</c>
/// automation ids. Avalonia's dialogs are asynchronous, so <see cref="Show"/> returns a task the caller awaits; upstream's
/// system sounds are not played (Avalonia has no portable equivalent). UI thread only.
/// </summary>
public partial class MessageWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private Button? _defaultButton;

	/// <summary>The parameterless constructor the XAML loader needs; <see cref="Show"/> builds the real window.</summary>
	public MessageWindow() : this("", MessageKind.Question, MessageButtons.Ok)
	{
	}

	/// <summary>Builds the window for a text, a kind and a button set; internal so the tests can show it without a modal owner.</summary>
	internal MessageWindow(string text, MessageKind kind, MessageButtons buttons)
	{
		InitializeComponent();
		messageText.Text = text;
		bool error = kind == MessageKind.Error;
		iconDisc[!Shape.FillProperty] = new DynamicResourceExtension(error ? "StatusBarErrorBackground" : "Accent");
		iconGlyph[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(error ? "StatusBarErrorForeground" : "StatusBarForeground");
		iconGlyph.Text = error ? "!" : "?";
		// the answer for the close box: what the cancel button of the set would give
		switch (buttons)
		{
			case MessageButtons.Ok:
				Answer = MessageAnswer.Ok;
				AddButton("DialogOk", MessageAnswer.Ok, isDefault: true, isCancel: true);
				break;
			case MessageButtons.YesNo:
				Answer = MessageAnswer.No;
				AddButton("DialogYes", MessageAnswer.Yes, isDefault: true, isCancel: false);
				AddButton("DialogNo", MessageAnswer.No, isDefault: false, isCancel: true);
				break;
			default:
				Answer = MessageAnswer.Cancel;
				AddButton("DialogSave", MessageAnswer.Save, isDefault: true, isCancel: false);
				AddButton("DialogDiscard", MessageAnswer.Discard, isDefault: false, isCancel: false);
				AddButton("DialogCancel", MessageAnswer.Cancel, isDefault: false, isCancel: true);
				break;
		}
		Opened += (_, _) => _defaultButton?.Focus();
		AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
		// the UI zoom on every window of the app, as upstream's Window style applied it
		WindowZoom.Attach(this);
	}

	/// <summary>The answer the window closes with: the set's cancel answer until a button is pressed.</summary>
	public MessageAnswer Answer { get; private set; }

	/// <summary>
	/// A scripted answer for the headless tests: while set, <see cref="Show"/> completes with it at once and opens no
	/// window (the main window's tests confirm or discard edits this way, as they did with the stand-in before this
	/// window landed); null, the default, shows the window.
	/// </summary>
	public static MessageAnswer? NextAnswer { get; set; }

	/// <summary>
	/// Shows <paramref name="text"/> modally over <paramref name="owner"/> and completes with the button it was closed
	/// with (the set's cancel answer for Esc or the close box). UI thread only.
	/// </summary>
	public static async Task<MessageAnswer> Show(Window owner, string text, MessageKind kind, MessageButtons buttons)
	{
		Logger.Debug("Message ({Kind}, {Buttons}): {Text}", kind, buttons, text.ReplaceLineEndings(" "));
		if (NextAnswer is { } scripted)
		{
			Logger.Debug("Message answered by the test script: {Answer}", scripted);
			return scripted;
		}
		var window = new MessageWindow(text, kind, buttons);
		await window.ShowDialog(owner);
		Logger.Debug("Message answered: {Answer}", window.Answer);
		return window.Answer;
	}

	private void AddButton(string labelKey, MessageAnswer answer, bool isDefault, bool isCancel)
	{
		var button = new Button { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
		button[!ContentProperty] = new DynamicResourceExtension(labelKey);
		AutomationProperties.SetAutomationId(button, "Message" + answer);
		button.Click += (_, _) =>
		{
			Answer = answer;
			Close();
		};
		buttonRow.Children.Add(button);
		if (isDefault)
		{
			_defaultButton = button;
		}
	}

	/// <summary>Esc closes with the cancel answer wherever the focus is; a bare letter presses the button it underlines.</summary>
	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
		else if (DialogStrings.TryPressAccessLetter(this, e))
		{
			e.Handled = true;
		}
	}
}
