// =================================================================================================
// FILE: RelayCommand.cs
// LOCATION: Mp3GainApp/ViewModels/RelayCommand.cs
//
// WHAT THIS FILE DOES:
//   This file implements the WPF ICommand pattern.
//   In plain terms, it acts as a messenger cable between clickable buttons on your screen
//   (like "Open", "Preview", "Save", "Revert") and the specific C# methods that execute the work.
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel initializes commands using this class and attaches them to button clicks.
//   - Also tells the button whether to be active or grayed out based on app state.
// =================================================================================================

using System.Windows.Input;

namespace Mp3GainApp.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute())
        {
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
