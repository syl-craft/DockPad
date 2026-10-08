using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>
/// La fenêtre de l'injection : vérification, déverrouillage, travail, compte-rendu.
/// </summary>
/// <remarks>
/// <para>
/// Elle vit dans <c>Secrets/</c> et non dans <c>Dialogs/</c> : c'est elle qui reçoit le mot de passe
/// maître et affiche le compte-rendu, elle est donc dans le périmètre d'audit. Seule dérogation au
/// rangement habituel du projet, et délibérée.
/// </para>
/// <para>
/// <b>Elle n'est pas propriétaire du minuteur d'effacement</b> — elle l'observe. Fermer la fenêtre
/// une fois le texte collé est le geste naturel ; si le minuteur y vivait, ce geste laisserait le
/// secret dans le presse-papier pour toujours.
/// </para>
/// <para>
/// <b>Le mode est décidé par le fichier</b>, pas par l'utilisateur : marqueurs → presse-papier,
/// annotations <c>x-bw</c> → fichiers. Voir <see cref="SecretPlan"/>.
/// </para>
/// </remarks>
public partial class SecretInjectionWindow : Window
{
    private readonly string _filePath;
    private readonly CancellationTokenSource _cancellation = new();
    private string? _content;
    private SecretMode _mode;

    /// <summary>L'inventaire analysé, en mode GitHub — nul dans les autres modes.</summary>
    private GitHubInventory? _inventory;
    private string? _writtenFolder;
    private InjectionReport? _report;

    /// <summary>
    /// Le presse-papier a-t-il déjà été rempli une fois pour ce rendu ?
    /// </summary>
    /// <remarks>
    /// Distingue « en attente d'un clic » de « rempli puis effacé » : les deux montrent un
    /// presse-papier vide, et ils n'appellent pas les mêmes boutons.
    /// </remarks>
    private bool _armedOnce;
    private readonly bool _syncOnly;

    /// <summary>
    /// La fenêtre est fermée. Lu au retour d'un appel au coffre : la source d'annulation est déjà
    /// libérée à ce moment-là, et ce drapeau ne l'est jamais.
    /// </summary>
    private bool _closed;

    /// <summary>La session ouverte entre le déverrouillage et le rendu — nulle avant, et après.</summary>
    private InjectionSession? _session;

    /// <summary>Une saisie par champ manquant : l'item et le champ, et les deux contrôles qui le portent.</summary>
    private readonly List<(string Item, string Field, PasswordBox Hidden, TextBox Shown)> _inputs = [];

    /// <summary>Clé du libellé de <c>BtnClose</c>, gardée pour pouvoir le retraduire.</summary>
    /// <remarks>
    /// Le bouton dit « Annuler » tant qu'une action est en cours, « Fermer » une fois le
    /// compte-rendu affiché. Comme le code l'affecte, il ne peut pas porter de liaison
    /// <c>{loc:T}</c> — c'est la règle du dépôt, et l'oublier rendait le bouton sourd aux
    /// changements de langue pour le reste de sa vie.
    /// </remarks>
    private string _closeKey = "Common_Cancel";

    /// <summary>Synchronise le cache local de la CLI, sans toucher à aucun fichier.</summary>
    /// <remarks>
    /// Le mot de passe maître est recueilli <b>ici</b> et non dans la fenêtre des Options : lui
    /// seul est dans le périmètre d'audit, et l'y garder est tout l'objet de la frontière.
    /// </remarks>
    public static SecretInjectionWindow ForSync() => new(syncOnly: true);

    private SecretInjectionWindow(bool syncOnly) : this("", syncOnly) { }

    public SecretInjectionWindow(string filePath) : this(filePath, syncOnly: false) { }

    private SecretInjectionWindow(string filePath, bool syncOnly)
    {
        InitializeComponent();
        _filePath = filePath;
        _syncOnly = syncOnly;

        TxtFile.Text = syncOnly ? Loc.T("Inject_Sync_Subtitle") : Path.GetFileName(filePath);
        TxtVersion.Text = AppInfo.VersionText;

        ApplyCloseLabel();

        ClipboardGuard.Changed += OnGuardChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            ClipboardGuard.Changed -= OnGuardChanged;
            Loc.LanguageChanged -= OnLanguageChanged;
            _cancellation.Cancel();
            _cancellation.Dispose();
            ClearInputs();
            _session?.Close();
        };

        Loaded += async (_, _) => await StartAsync().ConfigureAwait(true);
    }

    // ───────────── Le fil de l'opération ─────────────

    private async Task StartAsync()
    {
        ShowBusy(Loc.T("Inject_State_Checking"));

        if (!_syncOnly)
        {
            // Lecture et analyse hors du thread d'interface : l'entrée de menu vit sur TOUS les
            // fichiers, et un clic droit sur un gros fichier gelait la fenêtre avant même que sa
            // barre de progression puisse se peindre — les deux étaient sur la même pompe.
            var (content, failure, mode) = await Task.Run(() =>
            {
                var (text, error) = SecretInjectionService.ReadTemplate(_filePath);
                return (text, error, text is null ? SecretMode.None : SecretPlan.Of(text));
            }).ConfigureAwait(true);

            if (failure is not null) { ShowFailure(failure); return; }
            _content = content;
            _mode = mode;

            if (_mode == SecretMode.GitHub)
            {
                var (inventory, failures) = GitHubInventory.Parse(content ?? "");
                if (inventory == null) { ShowFailure(InjectionReport.Failed(failures)); return; }
                _inventory = inventory;
            }

            if (Refusal() is { } refusal) { ShowFailure(refusal); return; }
        }

        InjectionReport? preflight;
        try
        {
            preflight = await SecretInjectionService.PreflightAsync(_cancellation.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Timeout()) { ShowTimeout(); return; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Vérification du coffre avant injection");
            ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), ex.GetType().Name));
            return;
        }

        if (preflight is not null) { ShowFailure(preflight); return; }

        // Le fichier porte les deux formats : on demande lesquels produire, AVANT le mot de passe.
        if (_mode == SecretMode.Both) { ShowChoice(); return; }

        // Un inventaire se compare à GitHub AVANT le mot de passe : la vérification ne lit que des
        // noms, et c'est elle qui dit ce que l'envoi va créer, écraser ou laisser.
        if (_inventory != null) { await CheckGitHubAsync(_inventory); return; }

        ShowUnlock(error: null);
    }

    /// <summary>
    /// Les deux cas où le fichier ne dit pas quoi faire de lui, avant tout accès au coffre.
    /// </summary>
    /// <remarks>
    /// Refuser ici évite de réclamer un mot de passe maître pour une opération qui n'aboutira pas.
    /// </remarks>
    private InjectionReport? Refusal() => _mode switch
    {
        SecretMode.None => InjectionReport.Fail(Loc.T("Inject_Error_NoMarkers"), YamlError()),
        _ => null,
    };

    /// <summary>
    /// La cause technique quand rien n'a été trouvé : le document n'était peut-être pas du YAML.
    /// </summary>
    /// <remarks>
    /// En infobulle derrière la phrase traduite, jamais à sa place : un <c>.env</c> ou un <c>.png</c>
    /// n'est pas du YAML, et le dire en premier répondrait à côté de la question posée.
    /// </remarks>
    private string? YamlError() => ComposeSecrets.Extract(_content!).YamlError;

    private async void Unlock_Click(object sender, RoutedEventArgs e) => await UnlockAsync();

    private async void Password_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await UnlockAsync();
    }

    private async Task UnlockAsync()
    {
        var password = TxtPassword.Password;
        if (string.IsNullOrEmpty(password)) return;

        var sync = !_syncOnly && ChkSync.IsChecked == true;

        // L'attente doit se voir pour ce qu'elle est : une synchro ajoute un aller-retour reseau,
        // et un ecran qui annonce « lecture » pendant qu'il telecharge fait croire a un blocage.
        ShowBusy(Loc.T(sync ? "Inject_State_SyncingWorking" : "Inject_State_Working"));

        if (_syncOnly)
        {
            InjectionReport synced;
            try { synced = await SecretInjectionService.SyncAsync(password, _cancellation.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) when (Timeout()) { ShowTimeout(); return; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Synchronisation du coffre");
                ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), ex.GetType().Name));
                return;
            }
            finally { TxtPassword.Clear(); }

            if (!synced.Ok)
            {
                // Un déverrouillage refusé se corrige sur place : on reste sur la saisie plutôt que
                // de renvoyer vers un écran d'échec qu'il faudrait fermer pour réessayer.
                if (synced.Failures.Contains(Loc.T("Inject_Error_UnlockRefused")))
                    ShowUnlock(Loc.T("Inject_Error_UnlockRefused"));
                else
                    ShowFailure(synced);
                return;
            }

            ShowSynced();
            return;
        }

        (InjectionSession? session, InjectionReport? failure) opened;
        try
        {
            opened = await SecretInjectionService.OpenAsync(
                _inventory?.MarkersText ?? _content!, Path.GetDirectoryName(_filePath)!, _mode, password, sync,
                _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Timeout()) { ShowTimeout(); return; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Injection de secrets");
            ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), ex.GetType().Name));
            return;
        }
        finally { TxtPassword.Clear(); }

        // Fenêtre fermée pendant l'ouverture : la session revenue tient une clé que plus rien ne
        // refermerait — le gestionnaire de Closed est déjà passé, et _session était encore nul.
        if (_closed) { opened.session?.Close(); return; }

        if (opened.failure is { } refused)
        {
            // Même règle qu'en synchro : un déverrouillage refusé se corrige sur place.
            if (refused.Failures.Contains(Loc.T("Inject_Error_UnlockRefused")))
                ShowUnlock(Loc.T("Inject_Error_UnlockRefused"));
            else
                ShowFailure(refused);
            return;
        }

        _session = opened.session!;

        if (Proposable(_session).Count > 0) { ShowCreate(_session); return; }

        // Rien à proposer, mais pas faute de manque : l'organisation n'a aucune collection où
        // ranger un item neuf. L'écran ambre doit le dire, sinon le manque paraît oublié plutôt
        // qu'impossible à combler d'ici.
        if (_session.Writer is { CanCreateItems: false } && _session.Creatable.Any(r => r.IsNew))
            _session.Note(Loc.T("Inject_Create_NoCollection"));

        await FinishAsync();
    }

    /// <summary>Rend puis montre — ou, pour un inventaire, rend puis envoie à GitHub.</summary>
    private async Task FinishAsync()
    {
        if (_inventory != null) { await SendToGitHubAsync(_inventory); return; }

        Finish();
    }

    /// <summary>
    /// Rend, puis montre — après la création, ou directement quand il n'y avait rien à créer.
    /// </summary>
    /// <remarks>
    /// La session est fermée ici, quoi qu'il arrive : la clé de session ne survit pas au rendu.
    /// </remarks>
    private void Finish()
    {
        var session = _session!;
        InjectionReport report;

        try
        {
            report = SecretInjectionService.Render(session);
        }
        catch (Exception ex) when (_mode is SecretMode.Files or SecretMode.Both && ex is IOException or UnauthorizedAccessException)
        {
            // La CLI a repondu, c'est le disque qui a resiste : annoncer « la CLI a refuse la
            // demande » enverrait chercher le probleme du mauvais cote.
            //
            // Restreint au mode FICHIERS : en presse-papier et en synchro, rien n'est jamais
            // ecrit, et une IOException venue du tuyau de bw.exe se serait vu repondre « le
            // dossier des secrets n'a pas pu etre ecrit » -- la meme mauvaise direction, inversee.
            LogService.Warn(ex, "Ecriture des fichiers de secrets");
            ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_FileLocked"), ex.GetType().Name));
            return;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Injection de secrets");
            ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), ex.GetType().Name));
            return;
        }
        finally
        {
            session.Close();
            _session = null;
        }

        if (!report.Ok) { ShowFailure(report); return; }

        // L'armement appartient au déroulement et non à l'affichage : les écrans ne doivent que
        // montrer. C'est aussi ce qui permet à l'outil de capture de les rendre sans écrire dans le
        // presse-papier de la machine.
        // Le presse-papier n'est rempli TOUT DE SUITE que s'il est la seule sortie : c'est le cas
        // nominal a zero clic, et rien ne le retarde. Des qu'il accompagne des fichiers, il attend
        // un clic — sinon le decompte court pendant qu'on copie les fichiers sur le NAS, et
        // quatre-vingt-dix secondes plus tard le rendu est efface sans que rien ait ete colle.
        if (report is { Render: { } rendered, Files: null })
        {
            try
            {
                ClipboardGuard.Arm(rendered.Text, AppSettingsService.Current.ClipboardClearSeconds);
                _armedOnce = true;
            }
            catch (Exception ex)
            {
                // Le presse-papier peut être tenu par une autre application — gestionnaire de
                // presse-papier, session RDP, machine virtuelle. Sans ce catch, l'exception quittait
                // un gestionnaire async void et ressortait en « Erreur inattendue » à l'échelle de
                // l'application, alors que cette fenêtre sait très bien le dire elle-même.
                LogService.Warn(ex, "Copie du rendu dans le presse-papier");

                // Les fichiers déjà écrits restent acquis : un presse-papier occupé ne peut pas les
                // effacer du compte-rendu. On dégrade en manque plutôt qu'en échec total.
                if (report.Files is null)
                {
                    ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_ClipboardBusy"), ex.GetType().Name));
                    return;
                }

                report = InjectionReport.Produced(null, report.Files,
                    [.. report.Missing, Loc.T("Inject_Error_ClipboardBusy")]);
            }
        }

        ShowResult(report);
    }

    /// <summary>Ce que le formulaire peut réellement proposer : sans collection, aucun item neuf.</summary>
    private static IReadOnlyList<SecretItemRequest> Proposable(InjectionSession session) =>
        session.Writer is null
            ? []
            : session.Creatable.Where(r => !r.IsNew || session.Writer.CanCreateItems).ToList();

    /// <summary>
    /// Un champ de saisie par champ manquant, groupés par item. Construit en code : la fenêtre ne vit
    /// que le temps d'une injection, une bascule de langue en cours de saisie n'a pas à la retraduire.
    /// </summary>
    private void ShowCreate(InjectionSession session)
    {
        _inputs.Clear();
        CreateRows.Children.Clear();

        var writer = session.Writer!;
        var requests = Proposable(session);

        TxtCreateIntro.Text = writer.Organisation is { } org
            ? Loc.F("Inject_Create_IntroOrg", org)
            : Loc.T("Inject_Create_IntroVault");

        foreach (var request in requests)
        {
            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
            var badge = new TextBlock
            {
                Text = Loc.T(request.IsNew ? "Inject_Create_NewItem" : "Inject_Create_ExistingItem"),
                FontSize = 11, Foreground = (Brush)FindResource("Brush.TextHint"),
            };
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
            header.Children.Add(new TextBlock { Text = request.ItemName, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Brush.Text") });
            CreateRows.Children.Add(header);

            foreach (var field in request.Fields)
                CreateRows.Children.Add(Row(request.ItemName, field));
        }

        var needsCollection = requests.Any(r => r.IsNew) && writer.Organisation is not null;
        RowCollection.Visibility = Vis(needsCollection);
        TxtCreateNote.Visibility = Visibility.Collapsed;

        if (needsCollection)
        {
            var configured = AppSettingsService.Current.VaultCollection;
            var (selected, configuredMissing) = SecretCreationPlan.DefaultCollection(writer.Collections, configured);

            CmbCollection.ItemsSource = writer.Collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            CmbCollection.SelectedItem = selected;

            if (configuredMissing)
            {
                TxtCreateNote.Text = Loc.F("Inject_Create_CollectionMissing", configured);
                TxtCreateNote.Visibility = Visibility.Visible;
            }
        }

        if (session.Creatable.Any(r => r.IsNew) && !writer.CanCreateItems)
        {
            TxtCreateNote.Text = Loc.T("Inject_Create_NoCollection");
            TxtCreateNote.Visibility = Visibility.Visible;
        }

        Show(PanelCreate);
        Buttons(unlock: false, create: true);
        RefreshCreateButton();
        _inputs.FirstOrDefault().Hidden?.Focus();
    }

    /// <summary>Une ligne : le nom du champ au-dessus, la saisie masquée, et 👁 pour la voir.</summary>
    private UIElement Row(string item, string field)
    {
        var grid = new Grid { Margin = new Thickness(12, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Le nom du champ a sa propre ligne, sur toute la largeur : une colonne fixe le coupait
        // (« restic-server-vps-lite-pa… »), alors que c'est précisément ce qu'on doit lire pour
        // savoir quoi saisir. Une TextBox en lecture seule plutôt qu'un TextBlock : il se
        // sélectionne et se copie — pour aller chercher la valeur ailleurs sous ce nom exact.
        // Hors tabulation : Tab et Entrée vont de saisie en saisie, pas de libellé en libellé.
        var label = new TextBox
        {
            Text = field, IsReadOnly = true, IsTabStop = false, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 3),
            Background = System.Windows.Media.Brushes.Transparent,
        };
        label.SetResourceReference(ForegroundProperty, "Brush.TextLabel");

        // Mêmes brosses que TxtPassword : sans elles, la PasswordBox garde l'habillage clair
        // d'Aero2 en thème sombre. SetResourceReference plutôt qu'une brosse lue une fois, pour
        // qu'une bascule de thème en cours de saisie suive — la sémantique de DynamicResource.
        var hidden = new PasswordBox { Padding = new Thickness(6, 5, 6, 5), BorderThickness = new Thickness(1) };
        hidden.SetResourceReference(BackgroundProperty, "Brush.SurfaceCard");
        hidden.SetResourceReference(ForegroundProperty, "Brush.Text");
        hidden.SetResourceReference(PasswordBox.CaretBrushProperty, "Brush.Text");
        hidden.SetResourceReference(BorderBrushProperty, "Brush.BorderStrong");
        var shown = new TextBox { Padding = new Thickness(6, 5, 6, 5), Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(hidden, $"{item} {field}");
        AutomationProperties.SetName(shown, $"{item} {field}");

        hidden.PasswordChanged += (_, _) => RefreshCreateButton();
        shown.TextChanged += (_, _) => RefreshCreateButton();
        hidden.KeyDown += Input_KeyDown;
        shown.KeyDown += Input_KeyDown;

        var eye = new ToggleButton
        {
            Content = "👁", Width = 30, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
            ToolTip = Loc.T("Inject_Create_Show"),
            BorderThickness = new Thickness(1),
            Template = (ControlTemplate)FindResource("EyeToggleTemplate"),
            // Hors de la tabulation : Entrée et Tab passent de saisie en saisie, et s'arrêter sur
            // 👁 puis frapper Entrée ou Espace dévoilait la valeur. La souris l'atteint toujours.
            IsTabStop = false,
        };
        eye.SetResourceReference(BackgroundProperty, "Brush.SurfaceCard");
        eye.SetResourceReference(ForegroundProperty, "Brush.Text");
        eye.SetResourceReference(BorderBrushProperty, "Brush.BorderStrong");
        AutomationProperties.SetName(eye, Loc.T("Inject_Create_Show"));
        eye.Click += (_, _) =>
        {
            var reveal = eye.IsChecked == true;
            if (reveal) shown.Text = hidden.Password; else hidden.Password = shown.Text;
            shown.Visibility = Vis(reveal);
            hidden.Visibility = Vis(!reveal);
        };

        Grid.SetColumnSpan(label, 2);
        Grid.SetRow(hidden, 1);
        Grid.SetRow(shown, 1);
        Grid.SetRow(eye, 1);
        Grid.SetColumn(eye, 1);
        grid.Children.Add(label);
        grid.Children.Add(hidden);
        grid.Children.Add(shown);
        grid.Children.Add(eye);

        _inputs.Add((item, field, hidden, shown));
        return grid;
    }

    /// <summary>La valeur de la ligne, qu'elle soit affichée ou masquée.</summary>
    private static string Value((string Item, string Field, PasswordBox Hidden, TextBox Shown) input) =>
        input.Shown.Visibility == Visibility.Visible ? input.Shown.Text : input.Hidden.Password;

    private void RefreshCreateButton() => BtnCreate.IsEnabled = _inputs.Any(i => Value(i).Length > 0);

    /// <summary>Entrée passe à la saisie suivante, comme dans un formulaire, puis au bouton Créer.</summary>
    /// <remarks>
    /// Jamais <c>MoveFocus(Next)</c> : l'élément suivant dans l'ordre de tabulation était le 👁 de la
    /// ligne, et une seconde Entrée le basculait — la valeur apparaissait en clair. On vise donc la
    /// saisie suivante elle-même, sous la forme qu'elle montre.
    /// </remarks>
    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var index = _inputs.FindIndex(i => ReferenceEquals(i.Hidden, sender) || ReferenceEquals(i.Shown, sender));
        if (index < 0) return;

        if (index + 1 < _inputs.Count)
        {
            var next = _inputs[index + 1];
            if (next.Shown.Visibility == Visibility.Visible) next.Shown.Focus(); else next.Hidden.Focus();
        }
        else if (BtnCreate.IsEnabled) BtnCreate.Focus();
    }

    /// <summary>Passer, c'est aussi oublier ce qui a été tapé : rien ne reste dans les contrôles.</summary>
    private async void SkipCreate_Click(object sender, RoutedEventArgs e)
    {
        ClearInputs();
        await FinishAsync();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var session = _session!;
        var creations = SecretCreationPlan.WithValues(Proposable(session), (item, field) =>
            _inputs.Where(i => i.Item == item && i.Field == field).Select(Value).FirstOrDefault());
        var collectionId = (CmbCollection.SelectedItem as SecretCollection)?.Id;

        ClearInputs();
        ShowBusy(Loc.T("Inject_State_Creating"));

        try
        {
            await SecretInjectionService.CreateAsync(session, creations, collectionId, _cancellation.Token)
                .ConfigureAwait(true);
        }
        // Aucune clé de session ne survit à une injection — y compris celle-ci, annulée ou en
        // échec. Les trois branches qui n'atteignent pas Finish() (qui la referme déjà) doivent
        // donc la refermer elles-mêmes ; sinon le SecretWriter, et la clé qu'il tient, vivrait
        // jusqu'à la fermeture de la fenêtre.
        catch (OperationCanceledException) when (Timeout()) { AbandonSession(); ShowTimeout(); return; }
        catch (OperationCanceledException) { AbandonSession(); return; }
        catch (Exception ex)
        {
            // La relecture a échoué : ce qui a été écrit l'est, mais on ne peut pas le prouver.
            LogService.Warn(ex, "Création de secrets dans le coffre");
            AbandonSession();
            ShowFailure(InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), ex.GetType().Name));
            return;
        }

        // Fenêtre fermée pendant l'écriture : ne rien rendre — ni fichiers écrits derrière elle, ni
        // presse-papier armé pour personne.
        if (_closed) { AbandonSession(); return; }

        await FinishAsync();
    }

    /// <summary>
    /// Referme la session hors du chemin de <see cref="Finish"/> — annulation ou échec de
    /// <see cref="Create_Click"/> — pour que la clé de session ne lui survive pas.
    /// </summary>
    private void AbandonSession()
    {
        _session?.Close();
        _session = null;
    }

    /// <summary>Les valeurs saisies ne restent pas dans les contrôles une fois parties.</summary>
    private void ClearInputs()
    {
        foreach (var input in _inputs) { input.Hidden.Clear(); input.Shown.Clear(); }
        _inputs.Clear();
        CreateRows.Children.Clear();
    }

    /// <summary>
    /// L'annulation vient-elle du délai maximal plutôt que de l'utilisateur ?
    /// </summary>
    /// <remarks>
    /// Les deux lèvent la même exception, et les confondre laissait la fenêtre sur sa barre de
    /// progression indéfiniment quand Vaultwarden ne répondait pas — sans un mot expliquant
    /// pourquoi. La fermeture par l'utilisateur, elle, n'a rien à afficher.
    /// </remarks>
    private bool Timeout() => !_cancellation.IsCancellationRequested;

    private void ShowTimeout() =>
        ShowFailure(InjectionReport.Fail(Loc.F("Inject_Error_Timeout", BitwardenCli.TimeoutSeconds)));

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyCloseLabel();

    private void CloseLabel(string key) { _closeKey = key; ApplyCloseLabel(); }

    private void ApplyCloseLabel() => BtnClose.Content = Loc.T(_closeKey);

    // ───────────── L'inventaire GitHub ─────────────

    /// <summary>Compare l'inventaire à GitHub, sans toucher au coffre, puis montre ce que l'envoi fera.</summary>
    private async Task CheckGitHubAsync(GitHubInventory inventory)
    {
        ShowBusy(Loc.T("GitHub_State_Checking"));

        (GitHubCheck? Check, InjectionReport? Failure) compared;
        try
        {
            compared = await GitHubSyncService.CheckAsync(inventory, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Timeout()) { ShowGitHubTimeout(); return; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Vérification de l'inventaire sur GitHub");
            ShowFailure(InjectionReport.Fail(Loc.T("GitHub_Error_CliFailed"), ex.GetType().Name));
            return;
        }

        if (compared.Check is not { } check)
        {
            ShowFailure(compared.Failure ?? InjectionReport.Fail(Loc.T("GitHub_Error_CliFailed")));
            return;
        }

        ShowGitHub(inventory, check);
    }

    /// <summary>
    /// Rend l'inventaire, referme le coffre, puis envoie.
    /// </summary>
    /// <remarks>
    /// La session est fermée <b>avant</b> les appels à <c>gh</c> : une fois les valeurs rendues, la
    /// clé du coffre ne sert plus à rien, et elle n'a pas à survivre à un aller-retour réseau.
    /// </remarks>
    private async Task SendToGitHubAsync(GitHubInventory inventory)
    {
        var session = _session ?? throw new InvalidOperationException("No open vault session to send from.");

        IReadOnlyList<GitHubValue>? values;
        IReadOnlyList<string> missing;
        try { (values, missing) = inventory.Render(session.Lookup); }
        finally
        {
            session.Close();
            _session = null;
        }

        // Synchro ratée, écriture refusée : ce que la session savait déjà manquer.
        var notes = session.Missing.ToList();

        if (values == null) { ShowFailure(InjectionReport.Failed([.. missing, .. notes])); return; }

        ShowBusy(Loc.T("GitHub_State_Sending"));

        GitHubSendOutcome outcome;
        try
        {
            outcome = await GitHubSyncService.SendAsync(inventory, values, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Timeout()) { ShowGitHubTimeout(); return; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Envoi de l'inventaire vers GitHub");
            ShowFailure(InjectionReport.Fail(Loc.T("GitHub_Error_CliFailed"), ex.GetType().Name));
            return;
        }

        var refused = outcome.Refused.Select(name => Loc.F("GitHub_Error_SetRefused", name)).ToList();

        if (outcome.Sent.Count == 0) { ShowFailure(InjectionReport.Failed([.. refused, .. notes])); return; }

        ShowResult(InjectionReport.Sent(outcome, [.. notes, .. refused]));
    }

    private void ShowGitHubTimeout() =>
        ShowFailure(InjectionReport.Fail(Loc.F("GitHub_Error_Timeout", GitHubCli.TimeoutSeconds)));

    /// <summary>
    /// Ce que l'envoi fera : créer les absents, écraser les présents — avec leur âge —, et laisser ce
    /// qui n'est que sur GitHub. Des noms et des dates, jamais une valeur.
    /// </summary>
    private void ShowGitHub(GitHubInventory inventory, GitHubCheck check)
    {
        var now = DateTimeOffset.Now;

        TxtGitHubTitle.Text = Loc.F(
            inventory.Kind == GitHubTargetKind.Secrets ? "GitHub_Check_TitleSecrets" : "GitHub_Check_TitleVariables",
            inventory.Entries.Count);
        TxtGitHubTarget.Text = GitHubSyncService.TargetLabel(inventory);

        ListGitHubMissing.ItemsSource = check.Missing;
        BlocGitHubMissing.Visibility = Vis(check.Missing.Count > 0);

        ListGitHubPresent.ItemsSource = check.Present
            .Select(p => GitHubInventory.AgeInDays(p.UpdatedAt, now) is { } days
                ? Loc.F("GitHub_Check_Age", p.Name, days)
                : p.Name)
            .ToList();
        BlocGitHubPresent.Visibility = Vis(check.Present.Count > 0);

        ListGitHubExtra.ItemsSource = check.Extra;
        BlocGitHubExtra.Visibility = Vis(check.Extra.Count > 0);

        Show(PanelGitHub);
        Buttons(unlock: false, proceed: true);
        BtnContinue.IsEnabled = true;
        CloseLabel("Common_Cancel");
    }

    // ───────────── Les états ─────────────

    private void ShowBusy(string label)
    {
        TxtBusy.Text = label;
        Show(PanelBusy);
        Buttons(unlock: false);
    }

    /// <summary>
    /// Que faut-il produire ? Posé <b>avant</b> le mot de passe, et seulement pour un fichier qui
    /// porte les deux formats.
    /// </summary>
    /// <remarks>
    /// Les deux cases sont cochées : ne rien décocher redonne exactement le comportement d'avant,
    /// donc l'écran ne coûte qu'un clic à qui veut tout. Les décocher toutes les deux ne produirait
    /// rien : le bouton s'éteint plutôt que d'ouvrir le coffre pour rien.
    /// </remarks>
    private void ShowChoice()
    {
        Show(PanelChoice);
        Buttons(unlock: false, proceed: true);
        CloseLabel("Common_Cancel");
        Choice_Changed(null, null!);
    }

    /// <summary>
    /// Le choix se retient : qui décoche une fois ne veut pas le redécocher à chaque injection.
    /// </summary>
    /// <remarks>
    /// Écrit tout de suite plutôt qu'à la fermeture : cette fenêtre n'a pas de bouton
    /// <i>Sauvegarder</i>, et son cas nominal est de se fermer toute seule au décompte.
    /// </remarks>
    private void Sync_Changed(object sender, RoutedEventArgs e)
    {
        // Update, et non Load/Save : c'est le load-modify-save sous le verrou global des configs.
        AppSettingsService.Update(s => s.SyncVaultBeforeInject = ChkSync.IsChecked == true);
    }

    private void Choice_Changed(object? sender, RoutedEventArgs e)
    {
        var any = ChkFiles.IsChecked == true || ChkClipboard.IsChecked == true;

        BtnContinue.IsEnabled = any;
        TxtChoiceHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Le choix devient le mode, et le fil reprend au déverrouillage.</summary>
    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var files = ChkFiles.IsChecked == true;
        var clipboard = ChkClipboard.IsChecked == true;

        // Un inventaire passe par ici après sa vérification : rien à choisir, on déverrouille.
        if (_mode == SecretMode.GitHub) { ShowUnlock(error: null); return; }

        if (!files && !clipboard) return;

        _mode = files && clipboard ? SecretMode.Both
              : files ? SecretMode.Files
              : SecretMode.Clipboard;

        ShowUnlock(error: null);
    }

    private void ShowUnlock(string? error)
    {
        TxtUnlockError.Text = error ?? "";
        TxtUnlockError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

        ChkSync.IsChecked = AppSettingsService.Current.SyncVaultBeforeInject;

        Show(PanelUnlock);
        Buttons(unlock: true);
        TxtPassword.Focus();
    }

    /// <summary>
    /// L'unique compte-rendu : le rendu, les fichiers, les manques, les périmés — ce qui existe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trois écrans vivaient ici, et un seul s'affichait. Conséquence : en mode « les deux », un
    /// succès complet montrait les fichiers et <b>ne disait jamais</b> que le rendu était dans le
    /// presse-papier — la moitié de l'information disparaissait sans que rien ne le signale.
    /// </para>
    /// <para>
    /// <b>Le titre bascule vert/ambre sur <see cref="InjectionReport.Complete"/></b>, et c'est ce
    /// qui distingue les deux états. La panne d'origine n'était pas qu'un rendu soit partiel, c'est
    /// qu'il ait eu l'air complet.
    /// </para>
    /// </remarks>
    private void ShowResult(InjectionReport report)
    {
        // Pose ici et non chez l'appelant : RefreshClipboard et les commandes lisent _report, et
        // l'outil de capture appelle cette methode directement.
        _report = report;

        var written = report.Files?.Written ?? [];
        var stale = report.Files?.Stale ?? [];
        _writtenFolder = report.Files?.Folder;

        // Des noms et des nombres, jamais une valeur.
        LogService.Info($"Injection {(report.Complete ? "terminée" : "incomplète")} : {Path.GetFileName(_filePath)}, {report.Missing.Count} manque(s), {written.Count} fichier(s), {stale.Count} périmé(s)");

        // Des ternaires et non un switch : le garde des clés lit les arguments de Loc.T, et ne
        // verrait pas les clés d'une expression switch.
        TxtResultTitle.Text = report.GitHub != null
            ? Loc.T(report.Complete ? "GitHub_Result_Title" : "GitHub_Partial_Title")
            : Loc.T(report.Complete ? "Inject_Result_Title" : "Inject_Partial_Title");
        TxtResultTitle.SetResourceReference(ForegroundProperty,
            report.Complete ? "Brush.Text" : "Brush.WarnTextStrong");

        TxtResultSummary.Text = Summary(report.Render, written.Count, report.GitHub);

        TxtGitHubSentTarget.Text = report.GitHub?.Target ?? "";
        ListGitHubSent.ItemsSource = report.GitHub?.Sent ?? [];
        BlocGitHubSent.Visibility = Vis(report.GitHub is { Sent.Count: > 0 });


        ListMissing.ItemsSource = report.Missing;
        BlocMissing.Visibility = Vis(report.Missing.Count > 0);

        TxtFilesFolder.Text = report.Files?.Folder ?? "";
        ListWritten.ItemsSource = written;
        BlocWritten.Visibility = Vis(written.Count > 0);

        ListStale.ItemsSource = stale;
        BlocStale.Visibility = Vis(stale.Count > 0);

        Show(PanelResult);
        Buttons(unlock: false);
        CloseLabel("Common_Close");
        RefreshClipboard();
    }

    /// <summary>
    /// L'état du presse-papier et les commandes qui vont avec.
    /// </summary>
    /// <remarks>
    /// Quatre états, et deux d'entre eux montrent un presse-papier vide sans appeler les mêmes
    /// gestes : <b>en attente</b> d'un clic, <b>rempli</b> avec décompte, <b>tenu</b> décompte
    /// arrêté, <b>effacé</b>. D'où <c>_armedOnce</c>, sans lequel « pas encore » et « plus »
    /// seraient indiscernables.
    /// </remarks>
    private void RefreshClipboard()
    {
        if (_report?.Render is null) { BlocClipboard.Visibility = Visibility.Collapsed; return; }

        BlocClipboard.Visibility = Visibility.Visible;

        var armed = ClipboardGuard.IsArmed;
        var paused = ClipboardGuard.IsPaused;
        var counting = armed && !paused;

        TxtClipboardState.Text = Loc.T(
            !_armedOnce ? "Inject_Clipboard_Pending"
            : paused ? "Inject_Clipboard_Held"
            : armed ? "Inject_Clipboard_Filled"
            : "Inject_Clipboard_Cleared");

        TxtCountdown.Text = counting ? Loc.F("Inject_Countdown", ClipboardGuard.SecondsLeft) : "";
        TxtCountdown.Visibility = Vis(counting);

        BtnArm.Visibility = Vis(!_armedOnce);
        BtnPause.Visibility = Vis(counting);
        BtnClear.Visibility = Vis(armed);
    }

    /// <summary>Remplit le presse-papier, quand l'utilisateur en a fini avec les fichiers.</summary>
    private void Arm_Click(object sender, RoutedEventArgs e)
    {
        if (_report?.Render is not { } rendered || _armedOnce) return;

        try
        {
            ClipboardGuard.Arm(rendered.Text, AppSettingsService.Current.ClipboardClearSeconds);
            _armedOnce = true;
        }
        catch (Exception ex)
        {
            // Le presse-papier peut être tenu par une autre application. Les fichiers écrits
            // restent acquis : on le dit sans rien défaire.
            LogService.Warn(ex, "Copie du rendu dans le presse-papier");
            TxtClipboardState.Text = Loc.T("Inject_Error_ClipboardBusy");
            BtnArm.Visibility = Visibility.Collapsed;
            return;
        }

        RefreshClipboard();
    }

    /// <summary>Arrête le décompte — mais le filet de sortie efface toujours.</summary>
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        ClipboardGuard.Pause();
        RefreshClipboard();
    }

    /// <summary>
    /// Supprime les fichiers qu'on vient d'écrire, sur un clic.
    /// </summary>
    /// <remarks>
    /// Même garde que les périmés : uniquement les noms qu'on a écrits, jamais un balayage du
    /// dossier — il peut contenir autre chose.
    /// </remarks>
    private void DeleteWritten_Click(object sender, RoutedEventArgs e)
    {
        if (_report?.Files is not { } files || files.Written.Count == 0) return;
        if (Path.GetDirectoryName(_filePath) is not { } folder) return;

        var deleted = SecretFileWriter.Delete(folder, files.Written);
        LogService.Info($"Fichiers de secret supprimés à la demande : {deleted.Count}");

        var left = files.Written.Except(deleted, StringComparer.OrdinalIgnoreCase).ToList();

        ListWritten.ItemsSource = left;
        BlocWritten.Visibility = Vis(left.Count > 0);
        _report = InjectionReport.Produced(_report.Render, files with { Written = left }, _report.Missing);
    }

    /// <summary>
    /// Ce qui a été produit, en fragments joints — un par sortie réelle.
    /// </summary>
    /// <remarks>
    /// Le nombre d'items lus a disparu du résumé. Il valait quand une seule source était possible ;
    /// avec deux, il faudrait l'union des items des deux, sinon il surestimerait. Ce sur quoi
    /// l'utilisateur agit, ce sont les deux comptes de sorties.
    /// </remarks>
    private static string Summary(SecretRenderResult? render, int written, GitHubSendOutcome? github)
    {
        var parts = new List<string>();

        if (render is not null) parts.Add(Loc.F("Inject_Result_Markers", render.MarkerCount));
        if (written > 0) parts.Add(Loc.F("Inject_Result_Files", written));
        if (github != null)
            parts.Add(Loc.F(github.Kind == GitHubTargetKind.Secrets ? "GitHub_Result_Secrets" : "GitHub_Result_Variables",
                github.Sent.Count));

        return string.Join(", ", parts);
    }

    private static Visibility Vis(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Supprime les fichiers dont la clé a disparu du coffre — sur un clic, jamais autrement.
    /// </summary>
    /// <remarks>
    /// La liste vient des annotations <c>x-bw</c> dont la clé manque, jamais d'un balayage du
    /// dossier : celui-ci peut contenir autre chose. Ce qui n'a pas pu être supprimé reste affiché,
    /// pour que le compte-rendu ne prétende rien.
    /// </remarks>
    private void DeleteStale_Click(object sender, RoutedEventArgs e)
    {
        if (_report?.Files is not { } files || files.Stale.Count == 0) return;

        var folder = Path.GetDirectoryName(_filePath);
        if (folder is null) return;

        var deleted = SecretFileWriter.Delete(folder, files.Stale);
        LogService.Info($"Fichiers de secret périmés supprimés : {deleted.Count}");

        var left = files.Stale.Except(deleted, StringComparer.OrdinalIgnoreCase).ToList();

        ListStale.ItemsSource = left;
        BlocStale.Visibility = left.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _report = InjectionReport.Produced(_report.Render, files with { Stale = left }, _report.Missing);
    }

    /// <summary>Le cache a été rafraîchi. Rien n'a été lu, rien n'a été produit.</summary>
    private void ShowSynced()
    {
        LogService.Info("Synchronisation du cache Bitwarden depuis les Options");

        TxtResultTitle.Text = Loc.T("Inject_Sync_Done");
        TxtResultTitle.SetResourceReference(ForegroundProperty, "Brush.Text");
        TxtResultSummary.Text = "";

        BlocClipboard.Visibility = Visibility.Collapsed;
        BlocMissing.Visibility = Visibility.Collapsed;
        BlocWritten.Visibility = Visibility.Collapsed;
        BlocStale.Visibility = Visibility.Collapsed;
        BlocGitHubSent.Visibility = Visibility.Collapsed;

        Show(PanelResult);
        Buttons(unlock: false);
        CloseLabel("Common_Close");
    }

    private void ShowFailure(InjectionReport report)
    {
        ListFailures.ItemsSource = report.Failures;

        // La consolation doit parler de ce qui n'a pas eu lieu. En mode fichiers, invoquer le
        // presse-papier décrivait une opération qui n'était de toute façon pas prévue.
        TxtFailedHint.Text = _mode switch
        {
            SecretMode.Files or SecretMode.Both => Loc.T("Inject_Failed_Hint_Files"),
            SecretMode.GitHub => Loc.T("Inject_Failed_Hint_GitHub"),
            _ => Loc.T("Inject_Failed_Hint"),
        };

        TxtDiagnostic.Text = report.Diagnostic ?? "";
        TxtDiagnostic.Visibility = report.Diagnostic is null ? Visibility.Collapsed : Visibility.Visible;

        if (report.Diagnostic is not null)
            LogService.Info($"Injection refusée ({Path.GetFileName(_filePath)}) : {report.Diagnostic}");

        Show(PanelFailed);
        Buttons(unlock: false);
        CloseLabel("Common_Close");
    }

    private void Show(UIElement panel)
    {
        foreach (var candidate in new UIElement[] { PanelBusy, PanelChoice, PanelGitHub, PanelCreate, PanelUnlock, PanelResult, PanelFailed })
            candidate.Visibility = candidate == panel ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Le pied ne porte plus que les boutons d'<b>étape</b> — les commandes vivent à côté de ce sur
    /// quoi elles agissent.
    /// </summary>
    private void Buttons(bool unlock, bool proceed = false, bool create = false)
    {
        BtnUnlock.Visibility = unlock ? Visibility.Visible : Visibility.Collapsed;
        BtnContinue.Visibility = proceed ? Visibility.Visible : Visibility.Collapsed;
        BtnCreate.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        BtnSkipCreate.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────── Le décompte ─────────────

    /// <summary>
    /// Le verrou a bougé : on rafraîchit l'affichage, et la fenêtre disparaît quand il se désarme.
    /// </summary>
    /// <remarks>
    /// La fermeture automatique n'a lieu que sur l'écran de succès du presse-papier : un échec doit
    /// rester lisible, et le mode fichiers n'arme jamais le verrou — rien n'y passe par le
    /// presse-papier.
    /// </remarks>
    private void OnGuardChanged(object? sender, EventArgs e)
    {
        // En mode synchro, rien n'est jamais armé : sans cette garde, un événement venu d'une
        // autre injection fermerait l'écran de confirmation sous les yeux.
        if (_syncOnly) return;

        if (PanelResult.Visibility != Visibility.Visible) return;

        RefreshClipboard();

        // Fermeture automatique quand le presse-papier a été rempli PUIS vidé : le travail est
        // fini, zéro clic dans le cas nominal. Jamais AVANT l'armement — le rendu attend un
        // geste ; jamais sur une PAUSE — l'utilisateur vient de demander du temps ; jamais sur un
        // compte-rendu incomplet — il demande une décision, et le fermer y répondrait à sa place.
        if (_report is { Complete: true } && _armedOnce && !ClipboardGuard.IsArmed
            && AppSettingsService.Current.ClipboardClearSeconds > 0) Close();
    }

    // ───────────── Les boutons ─────────────

    private void Clear_Click(object sender, RoutedEventArgs e) => ClipboardGuard.ClearNow();

    /// <summary>Ouvre le dossier produit dans l'Explorateur.</summary>
    /// <remarks>
    /// Les fichiers restent à déposer sur le NAS : les montrer là où ils sont est la moitié du
    /// chemin que DockPad peut faire.
    /// </remarks>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_writtenFolder is null) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_writtenFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { LogService.Warn(ex, "Ouverture du dossier des secrets"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
