using Mue.Wpf;
using DockPad.Services.UpdateHost;
using DockPad;
using DockPad.Services;
using Mue;
using DockPad.Models;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;
using System.Threading;
using System.Threading.Tasks;

namespace DialogShot;
internal static class UpdateShots
{
    public static UpdatesDialog Create(UpdateState state = UpdateState.Available)
    {
        var service = CreateService(state);
        var window = new UpdatesDialog(service, DockPadUpdates.DialogHost);
        window.Closed += (_, _) => service.Dispose();
        return window;
    }

    private static UpdateService CreateService(UpdateState state)
    {
        var schedule = new MemoryUpdateSchedule();
        var service = new UpdateService(new Fixture(state), new UpdateServiceOptions { Schedule = schedule });
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            if (state is not (UpdateState.Unavailable or UpdateState.Checking)) service.CheckAsync().GetAwaiter().GetResult();
            if (state is UpdateState.Ready or UpdateState.Preparing or UpdateState.Installing)
                service.DownloadAsync().GetAwaiter().GetResult();
            // Les états transitoires sont figés pour la capture, sans tâche ni installateur actif.
            typeof(UpdateService).GetProperty(nameof(UpdateService.State))?.SetValue(service, state);
            if (state == UpdateState.Downloading)
                typeof(UpdateService).GetProperty(nameof(UpdateService.Progress))?.SetValue(service, 62);
            schedule.RecordCheck(DateTimeOffset.UtcNow.AddMinutes(-12));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        return service;
    }

    public static QuickAccessWindow Indicator(UpdateState state = UpdateState.Available)
    {
        GridCheck.WriteFixture();
        var service = CreateService(state);
        var usage = new UsageViewModel(new UsageService([]), () => new UsageConfig { Enabled = false });
        var window = new QuickAccessWindow(usage, new FixtureLoad(), updates: service);
        window.Closed += (_, _) => service.Dispose();
        return window;
    }

    private sealed class FixtureLoad : ISystemLoadMonitor
    {
        public SystemLoad Sample() => new(null, null);
        public void Reset() { }
    }

    public static UpdateBlockersDialog Blockers(bool incomplete = false, bool unavailable = false)
    {
        var started = DateTime.Today.AddHours(9).AddMinutes(12).ToFileTimeUtc();
        UpdateBlocker[] blockers = incomplete ? [
            new() { Pid = 43244, Name = "DockPad", IsRelay = true, ParentName = "Claude Code", Started = started, Selected = true },
            new() { Pid = 5004, Name = "DockPad", IsRelay = true, ParentName = "Claude Desktop", Started = started, Selected = true },
            new() { Pid = 17164, Name = "Windows Terminal", Started = started },
        ] : [
            new() { Pid = 31200, Name = "DockPad", IsRelay = true, ParentName = "Claude Code", Started = started, Selected = true },
            new() { Pid = 43244, Name = "DockPad", IsRelay = true, ParentName = "Claude Code", Started = started, Selected = true },
            new() { Pid = 5004, Name = "DockPad", IsRelay = true, ParentName = "Claude Desktop", Started = started, Selected = true },
            new() { Pid = 17164, Name = "Windows Terminal", Started = started },
        ];
        if (unavailable) blockers = [new() { Pid = 43244, Name = "DockPad", Started = started, Unavailable = true }];
        return new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory(System.IO.Path.GetTempPath()), new(blockers),
            host: new DockPadUpdateDialogHost(), shutdownBehavior: UpdateShutdownBehavior.Refuse, refresh: () => Task.FromResult(new UpdateBlockerScan(blockers)),
            close: (_, _) => Task.FromResult(UpdateBlockerCloseResult.StillRunning), incomplete: incomplete);
    }

    private sealed class Fixture(UpdateState state) : IUpdateBackend
    {
        public bool IsInstalled => state != UpdateState.Unavailable;
        public string CurrentVersion => "1.31.1";
        public string ContentDirectory => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockpad-update-fixture");
        public Task<UpdateRelease?> CheckAsync(CancellationToken token) => Task.FromResult<UpdateRelease?>(state == UpdateState.Idle ? null : new("1.32.0",
            """
            ## [1.32.0] — 2026-11-01
            ### Usage IA
            La barre verticale à droite de la grille accompagne les **quotas IA**.

            - Quatre styles : **Colonnes**, **Héros**, **Anneaux** et **Réservoir**.
              - Choisissez la position dans **Paramètres → Usage IA**.
            - La charge du PC affiche le processeur et la mémoire.
            ### Mises à jour
            - Les relais `--mcp` sont regroupés par session.
            - Les paramètres sont conservés lors du redémarrage.
            """, new object(), 7_700_000));
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            progress(100);
            return Task.CompletedTask;
        }
        public void Apply(UpdateRelease release) => throw new NotSupportedException("Screenshot fixture");
    }
}
