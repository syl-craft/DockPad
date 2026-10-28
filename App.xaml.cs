using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace DockPad;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }

    public static new async void Exit()
    {
        if (IsExiting) return;
        IsExiting = true;
        await System.Threading.Tasks.Task.WhenAll(Services.UrlPipeService.StopServerAsync(),
            InjectPipe.StopServerAsync(), ShowPipe.StopServerAsync(), TerminalPipe.StopServerAsync(),
            Services.McpPipeService.StopServerAsync());
        Current.Shutdown();
    }

    private WinForms.NotifyIcon _trayIcon = null!;
    private QuickAccessWindow _mainWindow = null!;
    private Mutex? _mutex;
    private StreamDockService? _streamDock;
    private StaDockRenderer? _streamDockRenderer;
    private Task _streamDockStopping = Task.CompletedTask;

    /// <summary>
    /// Session verrouillée : le dock reste éteint jusqu'au déverrouillage.
    /// </summary>
    private bool _isSessionLocked;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Avant tout acces aux chemins : AppPaths ne resout sa racine qu'une fois, et le
        // journal ecrit deja dedans.
        Services.AppPaths.Initialize("DockPad");

        Services.LogService.Init();

        // Avant toute fenêtre : une vue construite avant ce point figerait ses libellés dans la
        // langue par défaut. ApplyWpfLanguage fait suivre FrameworkElement.Language, que WPF lit
        // pour les StringFormat de liaison au lieu de CurrentCulture — voir sa remarque.
        Services.Localization.Loc.SetCulture(
            Services.Localization.Loc.Parse(Services.SettingsService.LoadLanguage()));
        ApplyWpfLanguage();

        // Même raison que la langue : une fenêtre construite avant ce point aurait résolu la
        // palette au chargement. Les références sont en DynamicResource, donc un remplacement
        // ultérieur serait bien vu — mais le démarrage clignoterait en clair avant de basculer.
        Services.ThemeService.ApplyFromSettings();

        // « Automatique » doit suivre Windows en cours de route, pas seulement au démarrage.
        // Le désabonnement a lieu dans OnExit : App masque l'événement Exit par une méthode
        // statique du même nom.
        Services.ThemeService.StartFollowingSystem();

        // Filets de sécurité : une exception non gérée ne doit pas tuer l'app résidente
        // (systray + hotkey). Tracée dans %APPDATA%\DockPad\logs\ + dialog d'erreur.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Services.LogService.Error(args.Exception, "Exception UI non gérée");
            try { AppDialog.Error($"Erreur inattendue :\n{args.Exception.Message}", "DockPad"); }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Services.LogService.Error(ex, "Exception non gérée (AppDomain)");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.LogService.Error(args.Exception, "Exception de tâche non observée");
            args.SetObserved();
        };

        // Mode relais MCP : serveur stdio lancé par Claude Code / Claude Desktop.
        // Aucune UI, pas de mutex (coexiste avec l'instance principale et d'autres relais).
        if (e.Args.Contains("--mcp"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Mcp.McpRelay.RunAsync()
                .ContinueWith(_ => Dispatcher.BeginInvoke(() => Shutdown()));
            return;
        }

        string? url = Services.StartupRelay.Arg(e.Args, "--url");
        string? injectPath = Services.StartupRelay.Arg(e.Args, "--inject-secrets");

        _mutex = new Mutex(initiallyOwned: true, Services.StartupRelay.SingleInstanceMutex, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;

            // ARRIVER ICI VEUT DIRE QUE LE RELAIS A ÉCHOUÉ. Il a été tenté avant WPF, dans
            // Program.Main : s'il avait abouti, le processus serait déjà sorti sans rien
            // construire. Le retenter ferait payer deux fois le délai d'attente au seul cas qui
            // en souffre — une instance qui tient le mutex sans répondre à ses tubes.
            //
            // Reste la course : le mutex a pu être pris entre la question de Program.Main et
            // celle-ci. On sert alors le repli sans avoir tenté le tube, ce qui reste correct —
            // l'utilisateur a sa fenêtre — simplement moins direct.

            if (injectPath is not null)
            {
                // Repli : l'instance principale est injoignable, on rend ici. Elle n'a pas de
                // systray, donc elle mourrait à la fermeture de la fenêtre — et OnExit viderait le
                // presse-papier avant que l'utilisateur ait pu coller. D'où la sortie différée.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = Secrets.SecretInjection.Handle(injectPath);
                window.Closed += (_, _) => ShutdownWhenClipboardIsSafe();
                return;
            }

            if (url is not null)
            {
                // Fallback : instance principale injoignable → popup locale. On reste en
                // shutdown explicite : si aucune popup n'a été créée (lancement direct via
                // une règle), on quitte immédiatement ; sinon on quitte à sa fermeture.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var picker = Services.UrlRouterService.Handle(url);
                if (picker is not null)
                    picker.Closed += (_, _) => ShutdownWhenFavoriteIsWritten(picker);
                else
                    Shutdown();
                return;
            }

            // Second lancement SANS argument : l'utilisateur redemande l'application. On sortait
            // en silence, ce qui est invisible tant que la premiere instance a une fenetre — mais
            // une instance demarree par un clic sur un lien n'en a AUCUNE. Double-cliquer
            // l'executable ne faisait alors plus rien du tout, et rien ne l'expliquait : ni
            // fenetre, ni message, ni ligne au journal.
            Services.LogService.Warn(new InvalidOperationException("show pipe unreachable"), "Une instance tient le mutex sans ecouter le tube d'affichage");

            Current.Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mainWindow = new QuickAccessWindow();
        MainWindow = _mainWindow;
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex) { Services.LogService.Warn(ex, "Stream Dock : surveillance de la veille indisponible."); }
        ApplyStreamDockSettings();

        if (url is null && injectPath is null)
        {
            _mainWindow.Show();
        }
        else
        {
            // Lancé par un clic sur une URL alors que DockPad ne tournait pas :
            // démarrage en arrière-plan. EnsureHandle déclenche OnSourceInitialized
            // (hook WndProc + hotkey global) sans afficher la fenêtre.
            new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();
        }

        _trayIcon = CreateTrayIcon();

        if (Services.Updates.AppInstallation.Root is not null)
        {
            if (Services.SettingsService.LoadAutoStart()) Services.SettingsService.SaveAutoStart(true);
            Services.BrowserRegistrationService.RefreshExisting();
            Secrets.SecretMenu.RefreshExisting();
        }
        Services.Updates.UpdateService.Current.StartPolling();

        _updateRelays = new(action => Dispatcher.BeginInvoke(action), args =>
        {
            if (!Services.Updates.AppInstallation.DeferDuringUpdate(args))
                throw new System.IO.IOException("Update handoff is not active");
        });
        Services.UrlPipeService.StartServer(u =>
            _updateRelays.Accept(["--url", u], () => Services.UrlRouterService.Handle(u)));

        InjectPipe.StartServer(path =>
            _updateRelays.Accept(["--inject-secrets", path], () => Secrets.SecretInjection.Handle(path)));

        // La demande d'affichage passe par le meme geste que le raccourci global : un seul point,
        // et le bandeau se rafraichit comme il doit.
        ShowPipe.StartServer(_ => Dispatcher.BeginInvoke(() => _mainWindow.BringToFront()));

        // Un prédéfini terminal du clic droit, relayé par le processus court : lancé d'ici, déjà chaud.
        // Jamais depuis une instance élevée : le tube ferait lancer n'importe quelle commande en admin.
        if (!Services.TerminalLaunch.IsElevated())
            TerminalPipe.StartServer(payload =>
            {
                if (Services.TerminalRequest.Decode(payload) is not { } request) return;
                _updateRelays.Accept(Services.TerminalRequest.ToArgs(request),
                    () => System.Threading.Tasks.Task.Run(() => Services.TerminalLaunch.Launch(request)));
            });

        Services.McpDispatcher.Mutated += () => Dispatcher.BeginInvoke(() => _mainWindow.RefreshGrid());
        Services.McpPipeService.StartServer(req => Services.McpDispatcher.Handle(req));

        if (url is not null)
            Dispatcher.BeginInvoke(() => Services.UrlRouterService.Handle(url));

        if (injectPath is not null)
            Dispatcher.BeginInvoke(() => Secrets.SecretInjection.Handle(injectPath));
    }

    /// <summary>Pipe du clic droit « Injecter les secrets… », jumeau de celui des URL.</summary>
    private static readonly Services.LinePipeService InjectPipe = new(Services.StartupRelay.InjectPipeName);
    private Services.Updates.PendingRelays? _updateRelays;
    public void BeginUpdateHandoff() => _updateRelays?.BeginHandoff();
    public void CancelUpdateHandoff() => _updateRelays?.Resume();

    /// <summary>
    /// Pipe du <b>second lancement</b> : « montre-toi ». Troisième jumeau.
    /// </summary>
    /// <remarks>
    /// <b>Un tube de plus plutôt qu'un préfixe dans un tube existant.</b> Les trois flux n'ont rien
    /// à voir, et un protocole partagé se paie au premier ajout — celui où l'on découvre qu'un des
    /// consommateurs doit distinguer un cas de plus.
    /// </remarks>
    private static readonly Services.LinePipeService ShowPipe = new(Services.StartupRelay.ShowPipeName);

    /// <summary>
    /// Tube des prédéfinis terminal du clic droit (<c>--terminal</c>).
    /// </summary>
    private static readonly Services.LinePipeService TerminalPipe = new(Services.TerminalRequest.PipeName);

    /// <summary>
    /// Quitte, mais pas avant que le presse-papier ait été rendu à l'utilisateur.
    /// </summary>
    /// <remarks>
    /// Seule l'instance éphémère du repli en a besoin : l'instance résidente ne meurt pas à la
    /// fermeture d'une fenêtre. Sans ce délai, sa sortie viderait le presse-papier immédiatement,
    /// et l'injection paraîtrait n'avoir rien fait.
    /// </remarks>
    /// <summary>
    /// Sortie différée jusqu'à la fin de l'enregistrement d'un favori, s'il y en a un en vol.
    /// </summary>
    /// <remarks>
    /// Même raison que <see cref="ShutdownWhenClipboardIsSafe"/>, et même forme : seule l'instance
    /// éphémère du repli en a besoin, parce qu'elle meurt à la fermeture de sa popup. Poser un
    /// favori télécharge l'icône du site ; sans ce délai, choisir un navigateur juste après avoir
    /// cliqué l'étoile tuait le processus au milieu de l'écriture, sans un mot.
    /// </remarks>
    private void ShutdownWhenFavoriteIsWritten(BrowserPickerWindow picker)
    {
        if (picker.PendingFavoriteWrite.IsCompleted) { Shutdown(); return; }

        picker.PendingFavoriteWrite.ContinueWith(_ => Dispatcher.BeginInvoke(() => Shutdown()));
    }

    private void ShutdownWhenClipboardIsSafe()
    {
        if (!Secrets.SecretInjection.IsClipboardArmed) { Shutdown(); return; }

        void OnChanged(object? sender, EventArgs e)
        {
            if (Secrets.SecretInjection.IsClipboardArmed) return;
            Secrets.SecretInjection.ClipboardChanged -= OnChanged;
            Dispatcher.BeginInvoke(() => Shutdown());
        }

        Secrets.SecretInjection.ClipboardChanged += OnChanged;
    }

    /// <summary>
    /// Affiche un rendu de la colonne sur le dock connecté sans l'enregistrer.
    /// </summary>
    public void PreviewStreamDockSideStrip(Models.SideStripStyle style)
    {
        Dispatcher.VerifyAccess();
        if (_streamDock != null)
            _streamDock.SideStripStyle = style;
    }

    /// <summary>
    /// Affiche un style de touches sur le dock connecté sans l'enregistrer.
    /// </summary>
    /// <summary>
    /// Faux si le Stream Dock est désactivé ou débranché ; un échec d'écriture remonte.
    /// </summary>
    public Task<bool> ApplyStreamDockBootImageAsync(DockPad.Surfaces.SurfaceImage image)
    {
        Dispatcher.VerifyAccess();
        return _streamDock?.ApplyBootImageAsync(image) ?? Task.FromResult(false);
    }

    public void PreviewStreamDockKeyStyle(Models.DockKeyStyle style)
    {
        Dispatcher.VerifyAccess();
        if (_streamDock != null)
            _streamDock.KeyStyle = style;
    }

    /// <summary>
    /// Applique les réglages du dock sur le thread UI, uniquement dans l’instance propriétaire du mutex.
    /// </summary>
    public void ApplyStreamDockSettings()
    {
        Dispatcher.VerifyAccess();
        if (_mutex == null || _mainWindow == null || IsExiting || _isSessionLocked) return;

        try
        {
            if (Services.SettingsService.LoadStreamDockEnabled() == false)
            {
                _ = StopStreamDockAsync();
                return;
            }

            // Une réactivation attend la libération de la surface et relit ensuite les derniers réglages.
            if (_streamDockStopping.IsCompleted == false)
            {
                _ = ApplyAfterStreamDockStopAsync(_streamDockStopping);
                return;
            }
            if (_streamDock == null)
            {
                var result = SurfaceDriverLoader.Load("StreamDock", SurfaceDriverLoader.PluginsFolder);
                if (result.Driver == null) return;

                _streamDockRenderer = new StaDockRenderer();
                _streamDock = new StreamDockService(result.Driver, new DockSources(_mainWindow),
                    _streamDockRenderer, TimeProvider.System, new SystemLoadMonitor());
                Services.TileEvents.GridChanged += _streamDock.OnGridChanged;
                Services.UsageConfigService.Changed += _streamDock.OnUsageConfigChanged;
                _mainWindow.ShortcutPage.Changed += _streamDock.OnShortcutPageChanged;
                _streamDock.SyncPages = Services.SettingsService.LoadStreamDockSyncPages();
                _streamDock.SideStripStyle = Services.SettingsService.LoadStreamDockSideStrip();
                _streamDock.KeyStyle = Services.SettingsService.LoadStreamDockKeyStyle();
                _streamDock.PrincipalProviderId = _mainWindow.UsageBanner.ViewModel?.SoloProviderId ?? "";
                if (_mainWindow.UsageBanner.ViewModel != null)
                    _mainWindow.UsageBanner.ViewModel.PropertyChanged += OnUsageBannerChanged;
                _streamDock.Start();
                return;
            }

            _streamDock.SyncPages = Services.SettingsService.LoadStreamDockSyncPages();
            _streamDock.SideStripStyle = Services.SettingsService.LoadStreamDockSideStrip();
            _streamDock.KeyStyle = Services.SettingsService.LoadStreamDockKeyStyle();
        }
        catch (Exception ex)
        {
            Services.LogService.Warn(ex, "Stream Dock : application des réglages échouée.");
            _ = StopStreamDockAsync(applySettingsAfterStop: false);
        }
    }

    private void OnUsageBannerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Services.Usage.UsageViewModel.SoloProviderId)
            && sender is Services.Usage.UsageViewModel viewModel && _streamDock != null)
            _streamDock.PrincipalProviderId = viewModel.SoloProviderId;
    }

    private Task StopStreamDockAsync(bool applySettingsAfterStop = true)
    {
        var service = _streamDock;
        var renderer = _streamDockRenderer;
        _streamDock = null;
        _streamDockRenderer = null;
        if (service != null)
        {
            Services.TileEvents.GridChanged -= service.OnGridChanged;
            Services.UsageConfigService.Changed -= service.OnUsageConfigChanged;
            _mainWindow.ShortcutPage.Changed -= service.OnShortcutPageChanged;
            if (_mainWindow.UsageBanner.ViewModel != null)
                _mainWindow.UsageBanner.ViewModel.PropertyChanged -= OnUsageBannerChanged;
        }

        if (service != null || renderer != null)
        {
            // Dispose du renderer peut attendre son thread STA : tout l’arrêt reste hors du thread UI.
            _streamDockStopping = Task.Run(async () =>
            {
                try
                {
                    if (service != null) await service.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { Services.LogService.Warn(ex, "Stream Dock : arrêt du service échoué."); }
                finally
                {
                    try { renderer?.Dispose(); }
                    catch (Exception ex) { Services.LogService.Warn(ex, "Stream Dock : arrêt du rendu échoué."); }
                }
            });
            if (applySettingsAfterStop && IsExiting == false) _ = ApplyAfterStreamDockStopAsync(_streamDockStopping);
        }
        return WaitForStreamDockStopAsync(_streamDockStopping);
    }

    private static async Task WaitForStreamDockStopAsync(Task stopping)
    {
        try { await stopping.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch (TimeoutException ex) { Services.LogService.Warn(ex, "Stream Dock : délai d’arrêt d’une seconde atteint."); }
    }

    private async Task ApplyAfterStreamDockStopAsync(Task stopping)
    {
        try { await stopping.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException ex)
        {
            Services.LogService.Warn(ex, "Stream Dock : délai d’arrêt de deux secondes atteint, réactivation abandonnée.");
            return;
        }
        if (IsExiting == false && Dispatcher.HasShutdownStarted == false)
            ApplyStreamDockSettings();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            TurnOffStreamDock("veille", showBootScreen: false);
        else if (e.Mode == PowerModes.Resume)
            TurnOnStreamDock(restart: true);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Dispatcher.Invoke(() => _isSessionLocked = true);
            TurnOffStreamDock("verrouillage", showBootScreen: true);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Dispatcher.Invoke(() => _isSessionLocked = false);
            TurnOnStreamDock(restart: false);
        }
    }

    /// <summary>
    /// Arrête le dock, éteint ou sur son image de démarrage, et attend la fin de l'arrêt : Windows n'attend pas le dock
    /// avant de se mettre en veille.
    /// </summary>
    private void TurnOffStreamDock(string cause, bool showBootScreen)
    {
        try
        {
            var service = Dispatcher.Invoke(() => IsExiting ? null : _streamDock);
            if (service == null) return;
            if (showBootScreen)
            {
                try { service.ShowBootScreenAsync().Wait(TimeSpan.FromSeconds(1)); }
                catch (Exception ex) { Services.LogService.Warn(ex, "Stream Dock : image de démarrage non affichée."); }
            }
            var stopping = Dispatcher.Invoke(() =>
                _streamDock == service ? StopStreamDockAsync(applySettingsAfterStop: false) : Task.CompletedTask);
            stopping.GetAwaiter().GetResult();
        }
        catch (Exception ex) { Services.LogService.Warn(ex, $"Stream Dock : extinction ({cause}) impossible."); }
    }

    /// <summary>
    /// Rallume le dock ; restart rouvre une connexion encore active, que la veille a pu rendre inutilisable.
    /// </summary>
    private void TurnOnStreamDock(bool restart)
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (IsExiting) return;
                if (restart && _streamDock != null) _ = StopStreamDockAsync();
                else ApplyStreamDockSettings();
            });
        }
        catch (Exception ex) { Services.LogService.Warn(ex, "Stream Dock : rallumage impossible."); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        StopStreamDockAsync().GetAwaiter().GetResult();
        Services.Updates.UpdateService.Stop();
        // Filet de sortie : un rendu encore dans le presse-papier en est retiré, à condition qu'il
        // s'y trouve toujours — l'utilisateur a pu copier autre chose entre-temps.
        Secrets.SecretInjection.ClearClipboardNow();

        // SystemEvents garde une référence statique sur l'abonné.
        Services.ThemeService.StopFollowingSystem();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Services.LogService.Shutdown();
        base.OnExit(e);
    }

    public async System.Threading.Tasks.Task<bool> PrepareForUpdateAsync(Window updateDialog)
    {
        var open = Windows.Cast<Window>().Where(w => w != _mainWindow && w != updateDialog).ToArray();
        if (open.Length > 0)
        {
            AppDialog.Info(Loc.T("Update_CloseDialogs"), Loc.T("Update_Title"), updateDialog);
            return false;
        }
        await Services.Updates.PendingWrites.CompleteAsync();
        Secrets.SecretInjection.ClearClipboardNow();
        return true;
    }

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        var icon = Icon.ExtractAssociatedIcon(
            Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName)
            ?? SystemIcons.Application;

        var menu = new WinForms.ContextMenuStrip();
        var closeItem = new WinForms.ToolStripMenuItem("Fermer");
        closeItem.Click += (_, _) => Exit();
        menu.Items.Add(closeItem);

        var tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "DockPad",
            Visible = true,
            ContextMenuStrip = menu,
        };

        tray.MouseClick += (_, args) =>
        {
            if (args.Button != WinForms.MouseButtons.Left) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        };

        return tray;
    }

    /// <summary>
    /// Fait suivre à WPF la langue courante, en plus des cultures posées par
    /// <see cref="Loc.SetCulture"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF ignore <c>CurrentCulture</c> pour les <c>StringFormat</c> de liaison : il lit
    /// <c>FrameworkElement.Language</c>. Sans ça, un nombre ou une date formaté par une liaison sort
    /// en <c>en-US</c> quelle que soit la culture du thread.
    /// </para>
    /// <para>
    /// <b>Pourquoi un gestionnaire de classe et non un <c>OverrideMetadata</c>.</b>
    /// <c>OverrideMetadata</c> ne s'appelle qu'une fois par propriété et par type : il figerait la
    /// langue du démarrage, et une fenêtre ouverte après une bascule hériterait de l'ancienne. Poser
    /// la langue au <c>Loaded</c> de chaque fenêtre couvre les deux cas, y compris les fenêtres
    /// ajoutées au projet plus tard, sans rien à brancher dans leur code.
    /// </para>
    /// <para>
    /// <c>Language</c> étant une propriété héritée, la poser sur la fenêtre suffit à couvrir tout
    /// son contenu.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <c>public</c> et non <c>private</c> : les outils de capture n'appellent jamais
    /// <c>OnStartup</c> — par conception, il monte mutex, systray et fenêtres — et doivent pourtant
    /// poser la langue à WPF, sans quoi tout <c>StringFormat</c> de liaison rendrait en <c>en-US</c>
    /// quelle que soit la langue demandée pour la capture. Même raison que
    /// <c>QuickAccessWindow.UsageBannerPanel</c>.
    /// </remarks>
    public static void ApplyWpfLanguage()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window window) return;

                window.Language = CurrentXmlLanguage();
                // Même point d'accroche pour la barre de titre : WPF ne la peint pas, elle
                // appartient au gestionnaire de fenêtres et resterait claire sur un contenu sombre.
                // Loaded arrive après SourceInitialized, le HWND existe donc.
                Services.ThemeService.ApplyTitleBar(window);
            }));

        // Les fenêtres déjà ouvertes au moment de la bascule : le gestionnaire ci-dessus ne les
        // reverra pas, leur Loaded est passé.
        Loc.LanguageChanged += (_, _) =>
        {
            var language = CurrentXmlLanguage();
            foreach (Window window in Current.Windows) window.Language = language;
        };
    }

    private static XmlLanguage CurrentXmlLanguage() =>
        XmlLanguage.GetLanguage(Loc.Current.IetfLanguageTag);
}
