using System;
using System.Windows.Input;
using Upscaler.App.Infrastructure;

namespace Upscaler.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        try
        {
            _execute();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unhandled command execution error.", ex);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
