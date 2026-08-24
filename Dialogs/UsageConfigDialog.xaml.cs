using System.Windows;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;

namespace DockPad;

/// <summary>
/// Réglages du bandeau Usage IA et liste des fournisseurs détectés.
/// </summary>
/// <remarks>
/// Sauvegarde immédiate, comme la fenêtre Navigateurs : pas de bouton Sauvegarder, seulement Fermer.
/// Chaque écriture est un load-modify-save sous <see cref="ConfigLock"/> — la fenêtre n'est pas
/// seule à écrire dans le profil.
/// </remarks>
public partial class UsageConfigDialog : Window
{
    /// <summary>Valeurs proposées pour le seuil d'alerte, en pourcentage restant.</summary>
    private static readonly int[] Thresholds = [5, 10, 15, 20, 25, 30, 35, 40, 45, 50];

    /// <summary>Une ligne de la liste des fournisseurs.</summary>
    private sealed class ProviderRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Glyph { get; init; }
        public required string Accent { get; init; }
        public required string Detail { get; init; }
        public bool Visible { get; init; }
        public bool Detected { get; init; }
        public bool IsDemo { get; init; }
    }

    /// <summary>Entrée de la liste « Fournisseur affiché ».</summary>
    private sealed class DefaultChoice
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
    }

    private UsageConfig _config = new();
    private bool _loading;

    public UsageConfigDialog()
    {
        InitializeComponent();
        TxtVersion.Text = AppInfo.VersionText;

        foreach (var threshold in Thresholds)
        {
            CmbThreshold.Items.Add($"{threshold} %");
        }

        Load();
    }

    private void Load(string? selectId = null)
    {
        _loading = true;
        try
        {
            _config = UsageConfigService.Load();

            ChkEnabled.IsChecked = _config.Enabled;
            ChkShowCost.IsChecked = _config.ShowCost;

            var index = Array.IndexOf(Thresholds, _config.AlertThreshold);
            CmbThreshold.SelectedIndex = index >= 0 ? index : Array.IndexOf(Thresholds, 15);

            BuildProviderRows(selectId);
            BuildDefaultChoices();
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildProviderRows(string? selectId)
    {
        var probes = Probe();

        LstProviders.Items.Clear();
        foreach (var entry in _config.Providers.OrderBy(p => p.Order))
        {
            probes.TryGetValue(entry.Id, out var probe);
            LstProviders.Items.Add(new ProviderRow
            {
                Id = entry.Id,
                Name = entry.Name.Length > 0 ? entry.Name : entry.Id,
                // Identité visuelle lue chez le fournisseur, jamais redéfinie ici : une seconde
                // table de littéraux aurait montré un rond gris au prochain assistant ajouté,
                // alors que le bandeau lui affichait sa vraie couleur.
                Glyph = probe?.Glyph is { Length: > 0 } glyph ? glyph : Initial(entry.Id),
                Accent = probe?.AccentColor is { Length: > 0 } accent ? accent : UnknownAccent,
                Detail = Detail(entry, probe),
                Visible = !entry.Hidden,
                Detected = entry.Detected,
                IsDemo = probe?.IsDemo ?? false,
            });
        }

        TxtEmpty.Visibility = LstProviders.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectId is not null)
        {
            LstProviders.SelectedItem = LstProviders.Items
                .OfType<ProviderRow>().FirstOrDefault(r => r.Id == selectId);
        }
    }

    /// <summary>
    /// Propose <b>tous</b> les fournisseurs, les masqués suffixés « (masqué) ».
    /// </summary>
    /// <remarks>
    /// N'en lister que les visibles créait une perte silencieuse : masquer le fournisseur choisi par
    /// défaut faisait retomber la liste sur « Premier disponible » à l'écran alors que le fichier
    /// gardait encore l'identifiant, et la modification suivante d'un autre réglage écrivait cette
    /// chaîne vide. Le réglage doit rester représentable pour survivre, comme le prévoit la
    /// conception.
    /// </remarks>
    private void BuildDefaultChoices()
    {
        CmbDefaultProvider.Items.Clear();
        CmbDefaultProvider.Items.Add(new DefaultChoice { Id = "", Label = "Premier disponible" });

        foreach (var entry in _config.Providers.OrderBy(p => p.Order))
        {
            var name = entry.Name.Length > 0 ? entry.Name : entry.Id;
            CmbDefaultProvider.Items.Add(new DefaultChoice
            {
                Id = entry.Id,
                Label = entry.Hidden ? $"{name} (masqué)" : name,
            });
        }

        var match = CmbDefaultProvider.Items.OfType<DefaultChoice>()
            .FirstOrDefault(c => c.Id == _config.DefaultProviderId);
        CmbDefaultProvider.SelectedItem = match ?? CmbDefaultProvider.Items[0];
    }

    /// <summary>
    /// Sonde les fournisseurs du registre pour enrichir l'affichage (badge démo, chemin détecté).
    /// N'écrit rien : la fusion dans la config n'a lieu que sur ↻ Redétecter.
    /// </summary>
    private static Dictionary<string, AiProbe> Probe()
    {
        var probes = new Dictionary<string, AiProbe>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in UsageProviderRegistry.All)
        {
            try { probes[provider.Id] = provider.Probe(); }
            catch (Exception ex) { LogService.Warn(ex, $"Sonde du fournisseur « {provider.Id} »"); }
        }
        return probes;
    }

    private static string Detail(AiProviderEntry entry, AiProbe? probe)
    {
        if (probe?.Detail.Length > 0) return probe.Detail;
        if (entry.DataPath.Length > 0) return entry.DataPath;
        return entry.Detected ? "" : Loc.T("UsageCfg_NoData");
    }

    /// <summary>Couleur des entrées dont le fournisseur n'est plus dans le registre.</summary>
    private const string UnknownAccent = "#8A8A8A";

    /// <summary>
    /// Repli pour une entrée conservée dans le fichier mais inconnue du registre — cas d'un retour
    /// arrière de version : son initiale reste lisible, à défaut de son identité.
    /// </summary>
    private static string Initial(string id) =>
        id.Length > 0 ? char.ToUpperInvariant(id[0]).ToString() : "?";

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Save();
    }

    private void Setting_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Save();
    }

    private void VisibleCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProviderRow row }) return;
        if (sender is not System.Windows.Controls.CheckBox box) return;

        lock (ConfigLock.Gate)
        {
            var config = UsageConfigService.Load();
            // Insensible à la casse, comme partout ailleurs sur cette clé : une entrée écrite
            // « Claude » dans le fichier était trouvée par le bandeau mais pas ici.
            var entry = config.Providers.FirstOrDefault(
                p => string.Equals(p.Id, row.Id, StringComparison.OrdinalIgnoreCase));

            // Sortir sans recharger laissait la case cochée alors que rien n'avait été enregistré :
            // l'utilisateur croyait avoir masqué un fournisseur. Le rechargement remet l'affichage
            // en accord avec le fichier, quelle que soit l'issue.
            if (entry is not null)
            {
                entry.Hidden = box.IsChecked != true;
                UsageConfigService.Save(config);
            }
        }

        Load(row.Id);
    }

    private void Redetect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            lock (ConfigLock.Gate)
            {
                var config = UsageConfigService.Load();
                UsageConfigService.Save(AiDetectionService.Detect(UsageProviderRegistry.All, config));
            }
            Load();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Redétection des fournisseurs IA");
            AppDialog.Error(Loc.T("UsageCfg_DetectFailed"), owner: this);
        }
    }

    /// <summary>
    /// Écrit les réglages du bandeau. Load-modify-save : la liste des fournisseurs, écrite par
    /// la détection et par les cases de visibilité, ne doit pas être écrasée par ce chemin.
    /// </summary>
    private void Save()
    {
        var threshold = Thresholds[Math.Max(CmbThreshold.SelectedIndex, 0)];
        var defaultId = (CmbDefaultProvider.SelectedItem as DefaultChoice)?.Id ?? "";

        lock (ConfigLock.Gate)
        {
            var config = UsageConfigService.Load();
            config.Enabled = ChkEnabled.IsChecked == true;
            config.ShowCost = ChkShowCost.IsChecked == true;
            config.AlertThreshold = threshold;
            config.DefaultProviderId = defaultId;
            UsageConfigService.Save(config);
            _config = config;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
