using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
    private string? _writtenFolder;
    private InjectionReport? _report;
    private readonly bool _syncOnly;

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
            ClipboardGuard.Changed -= OnGuardChanged;
            Loc.LanguageChanged -= OnLanguageChanged;
            _cancellation.Cancel();
            _cancellation.Dispose();
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

        ShowBusy(Loc.T("Inject_State_Working"));

        InjectionReport report;
        try
        {
            report = _syncOnly
                ? await SecretInjectionService.SyncAsync(password, _cancellation.Token).ConfigureAwait(true)
                : await SecretInjectionService.InjectAsync(
                    _content!, Path.GetDirectoryName(_filePath)!, _mode, password,
                    _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Timeout()) { ShowTimeout(); return; }
        catch (OperationCanceledException) { return; }
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
            // La zone de saisie ne garde pas le mot de passe une fois l'appel parti.
            TxtPassword.Clear();
        }

        if (!report.Ok)
        {
            // Un déverrouillage refusé se corrige sur place : on reste sur la saisie plutôt que de
            // renvoyer vers un écran d'échec qu'il faudrait fermer pour réessayer.
            if (report.Failures.Contains(Loc.T("Inject_Error_UnlockRefused")))
                ShowUnlock(Loc.T("Inject_Error_UnlockRefused"));
            else
                ShowFailure(report);
            return;
        }

        if (report.DidSync) { ShowSynced(); return; }

        // L'armement appartient au déroulement et non à l'affichage : les écrans ne doivent que
        // montrer. C'est aussi ce qui permet à l'outil de capture de les rendre sans écrire dans le
        // presse-papier de la machine.
        if (report.Render is { } rendered)
        {
            try
            {
                ClipboardGuard.Arm(rendered.Text, AppSettingsService.Current.ClipboardClearSeconds);
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

        _report = report;
        ShowOutcome(report);
    }

    /// <summary>
    /// Trois sorties possibles, et <b>le trou décide en premier</b>.
    /// </summary>
    /// <remarks>
    /// Un rendu incomplet ne prend jamais l'écran vert, même quand tous les fichiers ont été
    /// écrits : la panne d'origine n'était pas qu'un fichier soit partiel, c'est qu'il ait eu l'air
    /// complet.
    /// </remarks>
    private void ShowOutcome(InjectionReport report)
    {
        if (!report.Complete) { ShowIncomplete(report); return; }
        if (report.Files is { } files) { ShowFiles(files); return; }

        ShowSuccess(report.Render!);
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

    // ───────────── Les états ─────────────

    private void ShowBusy(string label)
    {
        TxtBusy.Text = label;
        Show(PanelBusy);
        Buttons(clear: false, unlock: false, folder: false);
    }

    private void ShowUnlock(string? error)
    {
        TxtUnlockError.Text = error ?? "";
        TxtUnlockError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

        Show(PanelUnlock);
        Buttons(clear: false, unlock: true, folder: false);
        TxtPassword.Focus();
    }

    private void ShowSuccess(SecretRenderResult result)
    {
        // Des noms et des nombres, jamais une valeur : ni le contenu du fichier, ni le rendu.
        LogService.Info($"Injection : {Path.GetFileName(_filePath)}, {result.MarkerCount} marqueur(s), {result.ItemCount} item(s)");

        TxtSummary.Text = Loc.F("Inject_Success", result.MarkerCount, result.ItemCount);
        Show(PanelSuccess);
        Buttons(clear: ClipboardGuard.IsArmed, unlock: false, folder: false);
        CloseLabel("Common_Close");
        OnGuardChanged(null, EventArgs.Empty);
    }

    private void ShowFiles(SecretFilesOutcome files)
    {
        LogService.Info($"Injection fichiers : {Path.GetFileName(_filePath)}, {files.Written.Count} fichier(s)");

        _writtenFolder = files.Folder;
        TxtFilesSummary.Text = Loc.F("Inject_Files_Success", files.Written.Count, files.ItemCount);
        TxtFilesFolder.Text = files.Folder;
        ListWritten.ItemsSource = files.Written;

        Show(PanelFiles);
        Buttons(clear: false, unlock: false, folder: true);
        CloseLabel("Common_Close");
    }

    /// <summary>
    /// Produit, mais avec des trous : ce qui manque, ce qui a été écrit, ce qui est périmé.
    /// </summary>
    /// <remarks>
    /// Les trois listes appellent trois réactions différentes — créer la clé absente, vérifier ce
    /// qui est à jour, décider du sort des périmés. Les fondre en une seule obligerait le lecteur à
    /// les redémêler lui-même.
    /// </remarks>
    private void ShowIncomplete(InjectionReport report)
    {
        var written = report.Files?.Written ?? [];
        var stale = report.Files?.Stale ?? [];
        _writtenFolder = report.Files?.Folder;

        // Des noms et des nombres, jamais une valeur.
        LogService.Info($"Injection incomplète : {Path.GetFileName(_filePath)}, {report.Missing.Count} manque(s), {written.Count} fichier(s), {stale.Count} périmé(s)");

        TxtPartialSummary.Text = report.Render is { } rendered
            ? Loc.F("Inject_Partial_Summary", rendered.MarkerCount, written.Count)
            : Loc.F("Inject_Partial_SummaryFiles", written.Count);

        ListMissing.ItemsSource = report.Missing;

        LblPartialWritten.Visibility = written.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ListPartialWritten.ItemsSource = written;

        ListStale.ItemsSource = stale;
        BlocStale.Visibility = stale.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Show(PanelIncomplete);
        Buttons(clear: ClipboardGuard.IsArmed, unlock: false, folder: written.Count > 0);
        CloseLabel("Common_Close");
        OnGuardChanged(null, EventArgs.Empty);
    }

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

        TxtSummary.Text = Loc.T("Inject_Sync_Done");
        TxtCountdown.Visibility = Visibility.Collapsed;

        Show(PanelSuccess);
        Buttons(clear: false, unlock: false, folder: false);
        CloseLabel("Common_Close");
    }

    private void ShowFailure(InjectionReport report)
    {
        ListFailures.ItemsSource = report.Failures;

        // La consolation doit parler de ce qui n'a pas eu lieu. En mode fichiers, invoquer le
        // presse-papier décrivait une opération qui n'était de toute façon pas prévue.
        TxtFailedHint.Text = _mode is SecretMode.Files or SecretMode.Both
            ? Loc.T("Inject_Failed_Hint_Files")
            : Loc.T("Inject_Failed_Hint");

        TxtDiagnostic.Text = report.Diagnostic ?? "";
        TxtDiagnostic.Visibility = report.Diagnostic is null ? Visibility.Collapsed : Visibility.Visible;

        if (report.Diagnostic is not null)
            LogService.Info($"Injection refusée ({Path.GetFileName(_filePath)}) : {report.Diagnostic}");

        Show(PanelFailed);
        Buttons(clear: false, unlock: false, folder: false);
        CloseLabel("Common_Close");
    }

    private void Show(UIElement panel)
    {
        foreach (var candidate in new UIElement[] { PanelBusy, PanelUnlock, PanelSuccess, PanelFiles, PanelIncomplete, PanelFailed })
            candidate.Visibility = candidate == panel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Buttons(bool clear, bool unlock, bool folder)
    {
        BtnClear.Visibility = clear ? Visibility.Visible : Visibility.Collapsed;
        BtnUnlock.Visibility = unlock ? Visibility.Visible : Visibility.Collapsed;
        BtnOpenFolder.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
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

        // L'écran incomplet ne se referme JAMAIS tout seul : c'est le seul qui demande une
        // décision — supprimer les fichiers périmés, ou non. Le voir disparaître au bout du
        // décompte reviendrait à répondre à sa place.
        if (PanelIncomplete.Visibility == Visibility.Visible)
        {
            var armed = ClipboardGuard.IsArmed;
            TxtPartialCountdown.Text = armed ? Loc.F("Inject_Countdown", ClipboardGuard.SecondsLeft) : "";
            TxtPartialCountdown.Visibility = armed ? Visibility.Visible : Visibility.Collapsed;
            BtnClear.Visibility = armed ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (PanelSuccess.Visibility != Visibility.Visible) return;

        if (ClipboardGuard.IsArmed)
        {
            TxtCountdown.Text = Loc.F("Inject_Countdown", ClipboardGuard.SecondsLeft);
            TxtCountdown.Visibility = Visibility.Visible;
            return;
        }

        // Désarmé alors que le succès est affiché : le décompte est allé au bout, ou l'utilisateur
        // a demandé l'effacement. Il n'y a plus rien à surveiller.
        if (AppSettingsService.Current.ClipboardClearSeconds > 0) Close();
        else TxtCountdown.Visibility = Visibility.Collapsed;
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
