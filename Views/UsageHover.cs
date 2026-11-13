using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DockPad.Services.Usage;

namespace DockPad.Views;

public static class UsageHover
{
    static UsageHover() => EventManager.RegisterClassHandler(typeof(FrameworkElement),
        ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(ToolTip_Opening));

    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card", typeof(UsageHoverCard), typeof(UsageHover), new PropertyMetadata(null, OnCardChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(HoverState), typeof(UsageHover));

    public static UsageHoverCard? GetCard(DependencyObject target) => (UsageHoverCard?)target.GetValue(CardProperty);
    public static void SetCard(DependencyObject target, UsageHoverCard? value) => target.SetValue(CardProperty, value);

    private static FrameworkElement? CardOwnerOf(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is FrameworkElement element && GetCard(element) != null) return element;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private static void ToolTip_Opening(object sender, ToolTipEventArgs e)
    {
        // ToolTipOpening est direct : les infobulles des textes enfants ne passent pas par leur jauge parente.
        if (sender is DependencyObject element && CardOwnerOf(element) != null) e.Handled = true;
    }

    private static void OnCardChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement owner) return;
        var state = (HoverState?)owner.GetValue(StateProperty);
        if (e.NewValue is UsageHoverCard card)
        {
            if (state == null)
            {
                state = new HoverState(owner);
                owner.SetValue(StateProperty, state);
            }
            state.Update(card);
        }
        else
        {
            state?.Detach();
            owner.ClearValue(StateProperty);
        }
    }

    private sealed class HoverState
    {
        private static HoverState? _active;
        private readonly FrameworkElement _owner;
        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;
        private UsageHoverCardView? _view;
        private Popup? _popup;
        private Window? _window;
        private bool _dismissed;

        public HoverState(FrameworkElement owner)
        {
            _owner = owner;
            _openTimer = new DispatcherTimer(DispatcherPriority.Input, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
            _closeTimer = new DispatcherTimer(DispatcherPriority.Input, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
            _openTimer.Tick += OpenTimer_Tick;
            _closeTimer.Tick += CloseTimer_Tick;
            owner.MouseEnter += Owner_MouseEnter;
            owner.MouseMove += Owner_MouseMove;
            owner.MouseLeave += Owner_MouseLeave;
            owner.GotKeyboardFocus += Owner_GotKeyboardFocus;
            owner.LostKeyboardFocus += Focus_Left;
            owner.PreviewKeyDown += Owner_PreviewKeyDown;
            owner.Unloaded += Owner_Unloaded;
            owner.IsVisibleChanged += Owner_IsVisibleChanged;
        }

        public void Update(UsageHoverCard card)
        {
            if (_view != null) _view.DataContext = card;
        }

        public void Detach()
        {
            Close();
            _owner.MouseEnter -= Owner_MouseEnter;
            _owner.MouseMove -= Owner_MouseMove;
            _owner.MouseLeave -= Owner_MouseLeave;
            _owner.GotKeyboardFocus -= Owner_GotKeyboardFocus;
            _owner.LostKeyboardFocus -= Focus_Left;
            _owner.PreviewKeyDown -= Owner_PreviewKeyDown;
            _owner.Unloaded -= Owner_Unloaded;
            _owner.IsVisibleChanged -= Owner_IsVisibleChanged;
        }

        private bool IsOverOwner()
        {
            if (_owner.IsMouseOver == false) return false;
            return ReferenceEquals(CardOwnerOf(Mouse.DirectlyOver as DependencyObject), _owner);
        }

        private bool IsInUse => IsOverOwner() || _owner.IsKeyboardFocusWithin
            || _view?.IsMouseOver == true || _view?.IsKeyboardFocusWithin == true;

        private void ScheduleOpen()
        {
            if (_dismissed || IsOverOwner() == false) return;
            _closeTimer.Stop();
            if (_popup?.IsOpen != true && _openTimer.IsEnabled == false) _openTimer.Start();
        }

        private void ScheduleClose()
        {
            _openTimer.Stop();
            if (_popup?.IsOpen == true && _closeTimer.IsEnabled == false) _closeTimer.Start();
        }

        private void Open()
        {
            _openTimer.Stop();
            if (_dismissed || _owner.IsVisible == false || GetCard(_owner) is not { } card) return;
            if (_popup?.IsOpen == true) return;
            _active?.Close();
            _view ??= CreateView();
            _view.DataContext = card;
            _popup ??= new Popup
            {
                PlacementTarget = _owner, Placement = PlacementMode.Right,
                AllowsTransparency = true, StaysOpen = true, Child = _view,
            };
            _window = Window.GetWindow(_owner);
            if (_window != null) _window.Deactivated += Window_Deactivated;
            InputManager.Current.PreProcessInput += Input_PreProcessInput;
            _active = this;
            _popup.IsOpen = true;
        }

        private UsageHoverCardView CreateView()
        {
            var view = new UsageHoverCardView();
            view.MouseEnter += Card_MouseEnter;
            view.MouseLeave += Card_MouseLeave;
            view.GotKeyboardFocus += Card_GotKeyboardFocus;
            view.LostKeyboardFocus += Focus_Left;
            view.PreviewKeyDown += Card_PreviewKeyDown;
            return view;
        }

        private void Close()
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            if (_popup != null) _popup.IsOpen = false;
            if (_window != null) _window.Deactivated -= Window_Deactivated;
            _window = null;
            InputManager.Current.PreProcessInput -= Input_PreProcessInput;
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void OpenTimer_Tick(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            if (IsOverOwner()) Open();
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (IsInUse == false) Close();
        }

        private void Owner_MouseEnter(object sender, MouseEventArgs e) => ScheduleOpen();
        private void Owner_MouseMove(object sender, MouseEventArgs e)
        {
            if (IsOverOwner()) ScheduleOpen();
            else ScheduleClose();
        }
        private void Owner_MouseLeave(object sender, MouseEventArgs e) { _dismissed = false; ScheduleClose(); }
        private void Owner_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ReferenceEquals(e.NewFocus, _owner)) { _dismissed = false; Open(); }
        }
        private void Focus_Left(object sender, KeyboardFocusChangedEventArgs e) => ScheduleClose();
        private void Card_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();
        private void Card_MouseLeave(object sender, MouseEventArgs e) => ScheduleClose();
        private void Card_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _closeTimer.Stop();
        private void Owner_Unloaded(object sender, RoutedEventArgs e) => Close();
        private void Owner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_owner.IsVisible == false) Close();
        }
        private void Window_Deactivated(object? sender, EventArgs e) => Close();

        private void Input_PreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is not KeyEventArgs { RoutedEvent: var routedEvent, Key: Key.Escape } key
                || routedEvent != Keyboard.PreviewKeyDownEvent) return;
            if (_view?.IsKeyboardFocusWithin == true) _owner.Focus();
            _dismissed = true;
            Close();
            key.Handled = true;
        }

        private void Owner_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && _popup?.IsOpen == true
                && _view?.FocusLink() == true) e.Handled = true;
        }

        private void Card_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) return;
            _owner.Focus();
            Close();
            if (Keyboard.Modifiers == ModifierKeys.Shift) e.Handled = true;
            else
            {
                _owner.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }
    }
}
