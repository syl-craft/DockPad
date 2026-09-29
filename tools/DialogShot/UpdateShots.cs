using DockPad;
using DockPad.Services.Updates;
using System.Threading;
using System.Threading.Tasks;

namespace DialogShot;
internal static class UpdateShots
{
    public static UpdatesDialog Create()
    {
        var service = new UpdateService(new Fixture());
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(null); service.CheckAsync().GetAwaiter().GetResult(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        return new UpdatesDialog(service);
    }
    private sealed class Fixture : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => DockPad.Services.AppInfo.VersionText.TrimStart('v');
        public string ContentDirectory => System.IO.Path.GetTempPath();
        public Task<UpdateRelease?> CheckAsync()
        {
            var current = Version.Parse(CurrentVersion);
            var next = new Version(current.Major, current.Minor, current.Build + 1).ToString();
            return Task.FromResult<UpdateRelease?>(new(next,
                "Aperçu — données de démonstration\n\n• Mise à jour depuis l’application.\n• Conservation des raccourcis et des liens entrants.\n• Gestion des applications bloquantes.\n\nVos paramètres sont conservés lors du redémarrage.", new object()));
        }
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) => throw new NotSupportedException();
        public void Apply(UpdateRelease release) => throw new NotSupportedException();
    }
}
