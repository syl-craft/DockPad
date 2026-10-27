using System.Diagnostics;
using System.IO;
using System.Text;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace StreamDockProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage : StreamDockProbe [--auto]");
            Console.WriteLine("--auto : réponses ?, sans saisie ; les durées de 3, 30, 60 et 20 s sont conservées.");
            return 0;
        }
        if (args.Where(arg => string.Equals(arg, "--auto", StringComparison.OrdinalIgnoreCase) == false).Any())
        {
            Console.WriteLine("Option inconnue. Usage : StreamDockProbe [--auto]");
            return 1;
        }

        var report = new ProbeReport(args.Contains("--auto", StringComparer.OrdinalIgnoreCase));
        using var stopping = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            stopping.Cancel();
        }
        Console.CancelKeyPress += Cancel;
        var exitCode = 1;
        try
        {
            Console.WriteLine("0. Vérification du logiciel Stream Dock.");
            if (IsVendorSoftwareRunning())
            {
                Console.WriteLine("Ferme d'abord le logiciel Stream Dock");
                report.Outcome = "Le logiciel Stream Dock est ouvert";
                exitCode = 2;
            }
            else
            {
                var images = ProbeImages.Create(stopping.Token);
                report.NoiseJpegBytes = images.NoiseJpegBytes;
                report.NoiseChunks = images.NoiseChunks;
                exitCode = RunAsync(images, report, stopping.Token).GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Console.WriteLine("Essai interrompu. Fermeture du dock terminée.");
            report.Outcome = "Essai interrompu par Ctrl+C";
        }
        catch (SurfaceBusyException ex)
        {
            Console.WriteLine("Le dock est occupé. Ferme d'abord le logiciel Stream Dock");
            Console.Error.WriteLine(ex);
            report.Outcome = "Dock occupé";
            report.Failure = ex.ToString();
            exitCode = 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("L'essai a échoué. Consulte le diagnostic ci-dessous.");
            Console.Error.WriteLine(ex);
            report.Outcome = "Échec de l'essai";
            report.Failure = ex.ToString();
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }

        try
        {
            report.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Impossible d'écrire le résumé dans le dossier courant.");
            Console.Error.WriteLine(ex);
            return exitCode == 0 ? 1 : exitCode;
        }
        return exitCode;
    }

    private static bool IsVendorSoftwareRunning()
    {
        var processes = Process.GetProcessesByName("StreamDock");
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }

    private static async Task<int> RunAsync(ProbeImages images, ProbeReport report, CancellationToken ct)
    {
        Console.WriteLine("0. Recherche du dock.");
        var driver = new StreamDockDriver();
        var surfaces = await Task.Run(driver.FindSurfaces, ct);
        var info = surfaces.FirstOrDefault();
        if (info == null)
        {
            Console.WriteLine("Aucun Stream Dock trouvé.");
            report.Outcome = "Aucun dock trouvé";
            return 3;
        }
        report.Surface = info;
        using var disconnectedStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var opened = await driver.OpenAsync(info, ct);
        var surface = opened as StreamDockSurface ?? throw new InvalidOperationException("The Stream Dock driver returned an unexpected surface type.");
        Console.WriteLine($"0. Modèle : {surface.Description.Model}");
        Console.WriteLine($"0. Chemin : {info.DevicePath}");
        Console.WriteLine($"0. Numéro de série : {info.SerialNumber ?? "non disponible"}");
        if (surfaces.Count > 1)
            Console.WriteLine($"0. {surfaces.Count} docks trouvés ; le premier est utilisé.");

        var disconnection = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Disconnected(Exception cause)
        {
            disconnection.TrySetResult(cause);
            disconnectedStop.Cancel();
        }
        surface.Disconnected += Disconnected;
        try
        {
            await new ProbeSession(surface, images, report).RunAsync(disconnectedStop.Token);
            return report.IsSuccessful ? 0 : 1;
        }
        catch (OperationCanceledException) when (disconnection.Task.IsCompletedSuccessfully)
        {
            throw new IOException("Stream Dock disconnected during the probe.", disconnection.Task.Result);
        }
        finally
        {
            surface.Disconnected -= Disconnected;
        }
    }
}
