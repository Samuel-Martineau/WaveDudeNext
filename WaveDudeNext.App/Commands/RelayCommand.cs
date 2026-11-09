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

using System.Windows.Input;

namespace WaveDudeNext.App.Commands;

/// <summary>
/// A command over two delegates. Avalonia has no CommandManager, so nothing requeries a command's state by itself:
/// the owner calls <see cref="Requery"/> after the state it depends on changed (the window does that for all of
/// <see cref="AppCommands"/> after a run, export, selection or project change), the way upstream called
/// <c>CommandManager.InvalidateRequerySuggested</c>.
/// </summary>
public sealed class RelayCommand : ICommand
{
	private readonly Action<object?> _execute;
	private readonly Func<object?, bool>? _canExecute;

	/// <summary>A command that never executes: the state of a pane's command before its window wires it.</summary>
	public static RelayCommand Disabled { get; } = new(_ => { }, _ => false);

	/// <summary>Creates the command.</summary>
	/// <param name="execute">What the command does; the parameter is the binding's CommandParameter.</param>
	/// <param name="canExecute">Whether it may run now; null means always.</param>
	public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	/// <summary>Creates a command that ignores its parameter.</summary>
	/// <param name="execute">What the command does.</param>
	/// <param name="canExecute">Whether it may run now; null means always.</param>
	public RelayCommand(Action execute, Func<bool>? canExecute = null)
		: this(_ => execute(), canExecute == null ? null : _ => canExecute())
	{
	}

	/// <inheritdoc />
	public event EventHandler? CanExecuteChanged;

	/// <inheritdoc />
	public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

	/// <inheritdoc />
	public void Execute(object? parameter)
	{
		if (CanExecute(parameter))
		{
			_execute(parameter);
		}
	}

	/// <summary>Tells the bound controls to ask <see cref="CanExecute"/> again.</summary>
	public void Requery() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A <see cref="RelayCommand"/> whose parameter has a type; a parameter of another type cannot execute.</summary>
/// <typeparam name="T">The parameter's type.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
	private readonly Action<T> _execute;
	private readonly Func<T, bool>? _canExecute;

	/// <summary>Creates the command.</summary>
	/// <param name="execute">What the command does with its parameter.</param>
	/// <param name="canExecute">Whether it may run now with that parameter; null means always.</param>
	public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	/// <inheritdoc />
	public event EventHandler? CanExecuteChanged;

	/// <inheritdoc />
	public bool CanExecute(object? parameter) => parameter is T value && (_canExecute?.Invoke(value) ?? true);

	/// <inheritdoc />
	public void Execute(object? parameter)
	{
		if (parameter is T value && (_canExecute?.Invoke(value) ?? true))
		{
			_execute(value);
		}
	}

	/// <summary>Tells the bound controls to ask <see cref="CanExecute"/> again.</summary>
	public void Requery() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
