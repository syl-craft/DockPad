using System.Windows.Controls;
using System.Windows.Input;

namespace DockPad.Views;

/// <summary>Accessible par Tab, sans retirer le clavier de la recherche lors d'un clic de souris.</summary>
public sealed class UsageGaugeButton : Button
{
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var wasFocusable = Focusable;
        try
        {
            SetCurrentValue(FocusableProperty, false);
            base.OnMouseLeftButtonDown(e);
        }
        finally
        {
            SetCurrentValue(FocusableProperty, wasFocusable);
        }
    }
}
