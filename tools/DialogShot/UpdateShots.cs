using DockPad;
using DockPad.Services.Updates;
using System.Threading;
using System.Threading.Tasks;

namespace DialogShot;
internal static class UpdateShots
{
    /// <summary>
    /// preparing : version téléchargée puis préparation commencée, l'état qui suit le clic sur
    /// « Mettre à jour et redémarrer ».
    /// </summary>
    public static UpdatesDialog Create(bool preparing = false)
    {
        var service = new UpdateService(new Fixture());
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            service.CheckAsync().GetAwaiter().GetResult();
            if (preparing)
            {
                service.DownloadAsync().GetAwaiter().GetResult();
                service.BeginPreparation();
            }
        }
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
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            progress(100);
            return Task.CompletedTask;
        }
        public void Apply(UpdateRelease release) => throw new NotSupportedException();
    }
}
