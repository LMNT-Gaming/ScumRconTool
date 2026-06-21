using System.Windows;
using System.Windows.Input;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private bool _isRunning;

    public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        : this(parameter => { execute(parameter); return Task.CompletedTask; }, canExecute)
    {
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        try
        {
            _isRunning = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            await _executeAsync(parameter);
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("Command", ex);
            MessageBox.Show(ex.Message, "Red Raven Rcon Tool Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
