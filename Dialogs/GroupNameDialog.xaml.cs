using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DockPad.Services;

namespace DockPad;

public partial class GroupNameDialog : Window
{
    public string GroupName => NameBox.Text.Trim();
    public string GroupColor { get; private set; }

    public GroupNameDialog(string name, string? color = null)
    {
        InitializeComponent();
        NameBox.Text = name;
        GroupColor = TileGroupService.EffectiveColor(color);
        UpdateSwatch();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void UpdateSwatch()
    {
        ColorSwatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(GroupColor));
        ColorValue.Text = GroupColor;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.ColorTranslator.FromHtml(GroupColor), FullOpen = true,
        };
        if (picker.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
        GroupColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        UpdateSwatch();
    }

    private sealed class DialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SaveButton is not null) SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NameBox.Text)) DialogResult = true;
    }
}
