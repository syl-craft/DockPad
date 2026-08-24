using System.Windows.Input;

namespace DockPad.Services;

/// <summary>
/// Commande WPF construite à partir d'un délégué.
/// </summary>
/// <remarks>
/// <para>
/// L'implémentation habituelle branche <c>CanExecuteChanged</c> sur
/// <c>CommandManager.RequerySuggested</c>, ce qui abonne <b>faiblement</b> et évite la fuite
/// classique : WPF réévalue alors <c>CanExecute</c> à chaque respiration de l'interface, sans
/// retenir la commande.
/// </para>
/// <para>
/// Sans <paramref name="canExecute"/>, la commande est toujours exécutable et ne s'abonne à rien —
/// inutile de faire travailler le <c>CommandManager</c> pour une réponse constante.
/// </para>
/// </remarks>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { if (canExecute is not null) CommandManager.RequerySuggested += value; }
        remove { if (canExecute is not null) CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>Commande à paramètre : un numéro de page, une tuile…</summary>
public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { if (canExecute is not null) CommandManager.RequerySuggested += value; }
        remove { if (canExecute is not null) CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) =>
        parameter is T typed ? canExecute?.Invoke(typed) ?? true : parameter is null;

    public void Execute(object? parameter)
    {
        if (parameter is T typed) execute(typed);
    }
}
