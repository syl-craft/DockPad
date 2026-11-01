using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// La fenêtre Usage IA : chaque réglage passe par la configuration enregistrée, et rien ne s'écrit sans geste. Le profil
/// est le profil temporaire du processus de test.
/// </summary>
public class UsageConfigDialogTests
{
    private static UsageConfig Seed(UsagePlacement placement = UsagePlacement.Bottom, bool enabled = true,
                                    UsageFocusMode focusMode = UsageFocusMode.Manual)
    {
        var config = new UsageConfig { Enabled = enabled, Placement = placement, FocusMode = focusMode };
        config.Providers.Add(new AiProviderEntry { Id = "claude", Name = "Claude", Detected = true, Order = 0 });
        UsageConfigService.Save(config);
        return config;
    }

    private static void Choose(ListBox list, object tag) =>
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Where(item => Equals(item.Tag, tag)).First();

    private static void Click(ToggleButton button)
    {
        button.IsChecked = true;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    [Fact]
    public Task Ouverture_NEcritRien() => WpfTestHost.Run(() =>
    {
        Seed();
        var saves = 0;
        void OnChanged() => saves++;
        UsageConfigService.Changed += OnChanged;
        try
        {
            _ = new UsageConfigDialog();
        }
        finally
        {
            UsageConfigService.Changed -= OnChanged;
        }

        Assert.Equal(0, saves);
    });

    [Fact]
    public Task Vignettes_ImagesFixes_NonCliquables() => WpfTestHost.Run(() =>
    {
        Seed();
        var dialog = new UsageConfigDialog();

        var samples = new[] { dialog.SampleColumns, dialog.SampleHero, dialog.SampleRings, dialog.SampleReservoir };

        Assert.All(samples, sample =>
        {
            Assert.False(sample.IsHitTestVisible);
            Assert.Equal(System.Windows.Input.KeyboardNavigationMode.None,
                System.Windows.Input.KeyboardNavigation.GetTabNavigation(sample));
            Assert.NotNull(sample.ViewModel);
        });
    });

    [Fact]
    public Task Position_ADroite_EcritLaConfigEtActiveLeStyle() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Bottom);
        var dialog = new UsageConfigDialog();
        Assert.False(dialog.GrpBarStyle.IsEnabled);

        Choose(dialog.LstPlacement, UsagePlacement.Right);

        Assert.Equal(UsagePlacement.Right, UsageConfigService.Load().Placement);
        Assert.True(dialog.GrpBarStyle.IsEnabled);
        Assert.True(dialog.GrpSystemLoad.IsEnabled);
    });

    [Fact]
    public Task Style_Choisi_EcritLaConfig() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();

        Choose(dialog.LstBarStyle, SideStripStyle.Rings);

        Assert.Equal(SideStripStyle.Rings, UsageConfigService.Load().BarStyle);
    });

    [Fact]
    public Task Auto_ChoisiDansLaFenetre_PasseParLaConfigEtLeveLEvenement() => WpfTestHost.Run(() =>
    {
        Seed();
        var dialog = new UsageConfigDialog();
        var saves = 0;
        void OnChanged() => saves++;
        UsageConfigService.Changed += OnChanged;
        try
        {
            Click(dialog.RbFocusAuto);
        }
        finally
        {
            UsageConfigService.Changed -= OnChanged;
        }

        Assert.Equal(UsageFocusMode.AutoClosestToLimit, UsageConfigService.Load().FocusMode);
        Assert.Equal(1, saves);
    });

    [Fact]
    public Task Manuel_ChoisiApresAuto_RevientDansLaConfig() => WpfTestHost.Run(() =>
    {
        Seed(focusMode: UsageFocusMode.AutoClosestToLimit);
        var dialog = new UsageConfigDialog();
        Assert.True(dialog.RbFocusAuto.IsChecked);

        Click(dialog.RbFocusManual);

        Assert.Equal(UsageFocusMode.Manual, UsageConfigService.Load().FocusMode);
    });

    [Fact]
    public Task ChargeDecochee_CourbeGriseeEtConfigEcrite() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();
        Assert.True(dialog.ChkShowLoadHistory.IsEnabled);

        dialog.ChkShowSystemLoad.IsChecked = false;
        dialog.ChkShowSystemLoad.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.False(UsageConfigService.Load().ShowSystemLoad);
        Assert.False(dialog.ChkShowLoadHistory.IsEnabled);
    });

    [Fact]
    public Task UsageDesactive_ToutGrise_ReglagesConserves() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();

        dialog.ChkEnabled.IsChecked = false;
        dialog.ChkEnabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.False(dialog.GrpPosition.IsEnabled);
        Assert.False(dialog.GrpBarStyle.IsEnabled);
        Assert.False(dialog.GrpFocus.IsEnabled);
        Assert.False(dialog.GrpSystemLoad.IsEnabled);
        Assert.Equal(UsagePlacement.Right, UsageConfigService.Load().Placement);
    });

    private static int CountChanges(Action gesture)
    {
        var saves = 0;
        void OnChanged() => saves++;
        UsageConfigService.Changed += OnChanged;
        try
        {
            gesture();
        }
        finally
        {
            UsageConfigService.Changed -= OnChanged;
        }

        return saves;
    }

    [Fact]
    public Task Position_Changee_LeveChangedUneSeuleFois() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Bottom);
        var dialog = new UsageConfigDialog();

        Assert.Equal(1, CountChanges(() => Choose(dialog.LstPlacement, UsagePlacement.Right)));
    });

    [Fact]
    public Task Style_Change_LeveChangedUneSeuleFois() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();

        Assert.Equal(1, CountChanges(() => Choose(dialog.LstBarStyle, SideStripStyle.Reservoir)));
    });

    [Fact]
    public Task Charge_Decochee_LeveChangedUneSeuleFois() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();

        Assert.Equal(1, CountChanges(() => ToggleOff(dialog.ChkShowSystemLoad)));
    });

    [Fact]
    public Task Courbe_Decochee_LeveChangedUneSeuleFois() => WpfTestHost.Run(() =>
    {
        Seed(UsagePlacement.Right);
        var dialog = new UsageConfigDialog();

        Assert.Equal(1, CountChanges(() => ToggleOff(dialog.ChkShowLoadHistory)));
    });

    private static void ToggleOff(ToggleButton box)
    {
        box.IsChecked = false;
        box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    [Fact]
    public Task Radio_CocheSansClic_EstEnregistre() => WpfTestHost.Run(() =>
    {
        Seed();
        var dialog = new UsageConfigDialog();

        // Les flèches du clavier cochent le bouton sans le cliquer.
        dialog.RbFocusAuto.IsChecked = true;

        Assert.Equal(UsageFocusMode.AutoClosestToLimit, UsageConfigService.Load().FocusMode);
    });

    [Fact]
    public Task Auto_DejaCoche_RecliqueEmetLeChoixSansRienEcrire() => WpfTestHost.Run(() =>
    {
        Seed(focusMode: UsageFocusMode.AutoClosestToLimit);
        var dialog = new UsageConfigDialog();
        var chosen = 0;
        void OnChosen() => chosen++;
        UsageConfigService.AutoChosen += OnChosen;
        try
        {
            Assert.Equal(0, CountChanges(() => Click(dialog.RbFocusAuto)));
        }
        finally
        {
            UsageConfigService.AutoChosen -= OnChosen;
        }

        Assert.Equal(1, chosen);
    });

    [Fact]
    public Task Manuel_Clique_NEmetPasLeChoixAuto() => WpfTestHost.Run(() =>
    {
        Seed(focusMode: UsageFocusMode.AutoClosestToLimit);
        var dialog = new UsageConfigDialog();
        var chosen = 0;
        void OnChosen() => chosen++;
        UsageConfigService.AutoChosen += OnChosen;
        try
        {
            Click(dialog.RbFocusManual);
        }
        finally
        {
            UsageConfigService.AutoChosen -= OnChosen;
        }

        Assert.Equal(0, chosen);
    });
}
