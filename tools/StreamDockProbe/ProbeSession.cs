using System.Diagnostics;
using System.Threading.Channels;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace StreamDockProbe;

internal sealed class ProbeSession(StreamDockSurface surface, ProbeImages images, ProbeReport report)
{
    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("1. Repérage des 15 touches et de la colonne latérale.");
        await ShowNumbersAsync(ct);
        await AskAsync(step: 1, ct, occurrence: 0);
        await AskAsync(step: 1, ct, occurrence: 1);

        Console.WriteLine("2. Image bruitée sur L1 C1.");
        Console.WriteLine($"JPEG : {images.NoiseJpegBytes} octets, {images.NoiseChunks} blocs de données HID.");
        await surface.SetKeyImageAsync(new SurfaceKey(0, 0), images.Noise, ct);
        await surface.CommitAsync(ct);
        await AskAsync(step: 2, ct);

        Console.WriteLine("3. Image rouge sur L1 C2, sans Commit : regarde : rien ne doit changer.");
        await surface.SetKeyImageAsync(new SurfaceKey(0, 1), images.Red, ct);
        await WaitAsync(step: 3, "Attente sans Commit", TimeSpan.FromSeconds(3), ct);
        await AskAsync(step: 3, ct, occurrence: 0);
        await surface.CommitAsync(ct);
        await AskAsync(step: 3, ct, occurrence: 1);

        Console.WriteLine("4. Appuie puis relâche chaque touche pendant 30 s.");
        await ListenAsync(step: 4, TimeSpan.FromSeconds(30), animate: false, ct);
        await AskAsync(step: 4, ct);

        Console.WriteLine("5. Pendant 60 s, seul le maintien en vie automatique du pilote envoie des commandes.");
        await WaitAsync(step: 5, "Maintien en vie", TimeSpan.FromSeconds(60), ct);
        await AskAsync(step: 5, ct, occurrence: 0);
        Console.WriteLine("5. Effacement de L1 C1, puis Commit.");
        await surface.SendCommandAsync(StreamDockProtocol.Clear(StreamDockModel.KeyId(new SurfaceKey(0, 0))), ct);
        await surface.CommitAsync(ct);
        await AskAsync(step: 5, ct, occurrence: 1);
        Console.WriteLine("5. Effacement de toutes les touches et des écrans, puis Commit.");
        await surface.ClearAsync(ct);
        await surface.CommitAsync(ct);
        await AskAsync(step: 5, ct, occurrence: 2);
        Console.WriteLine("5. Mise en veille.");
        await surface.SleepAsync(ct);
        await AskAsync(step: 5, ct, occurrence: 3);

        Console.WriteLine("6. Réveil et retour aux numéros. Teste l'animation des touches pendant 20 s.");
        await surface.SendCommandAsync(StreamDockProtocol.Wake(), ct);
        await ShowNumbersAsync(ct);
        await ListenAsync(step: 6, TimeSpan.FromSeconds(20), animate: true, ct);
        Console.WriteLine(report.DescribeLatency());
        await AskAsync(step: 6, ct);
        report.IsComplete = true;
        report.Outcome = report.IsSuccessful ? "Toutes les vérifications sont validées" : "Vérifications non validées ou sans réponse";
    }

    private async Task ShowNumbersAsync(CancellationToken ct)
    {
        foreach (var (key, image) in images.Normal)
        {
            await surface.SetKeyImageAsync(key, image, ct);
        }
        await surface.SetSideStripImageAsync(images.SideStrip, ct);
        await surface.CommitAsync(ct);
    }

    private async Task AskAsync(int step, CancellationToken ct, int occurrence = 0)
    {
        var check = report.Checks.Where(item => item.Step == step).ElementAt(occurrence);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Console.Write($"{step}. {check.Question} [O/N] ");
            if (report.Automatic)
            {
                Console.WriteLine("? (--auto)");
                return;
            }

            // Console.ReadLine n'est pas annulable : seule son attente est interrompue.
            var answer = await Task.Run(() => Console.ReadLine(), CancellationToken.None).WaitAsync(ct);
            if (answer == null)
            {
                Console.WriteLine("? (entrée fermée)");
                return;
            }
            answer = answer.Trim().ToUpperInvariant();
            if (answer is "O" or "N")
            {
                check.Answer = answer;
                return;
            }
            Console.WriteLine("Réponds O ou N, puis Entrée.");
        }
    }

    private async Task WaitAsync(int step, string label, TimeSpan duration, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        try
        {
            await Task.Delay(duration, ct);
        }
        finally
        {
            report.Durations.Add((step, label, elapsed.Elapsed));
        }
    }

    private async Task ListenAsync(int step, TimeSpan duration, bool animate, CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<ProbeInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var elapsed = Stopwatch.StartNew();

        // Le rappel HID ne fait que mettre en file ; les images et leurs Commits restent séquentiels.
        void Receive(KeyEvent keyEvent) => events.Writer.TryWrite(new ProbeInput(keyEvent, Stopwatch.GetTimestamp(), DateTimeOffset.Now));

        surface.KeyChanged += Receive;
        listening.CancelAfter(duration);
        try
        {
            while (elapsed.Elapsed < duration)
            {
                var input = await events.Reader.ReadAsync(listening.Token);
                ct.ThrowIfCancellationRequested();
                if (elapsed.Elapsed >= duration)
                    break;
                var key = input.Event.Key;
                if (animate)
                {
                    var image = input.Event.State == KeyState.Pressed ? images.Pressed[key] : images.Normal[key];
                    await surface.SetKeyImageAsync(key, image, ct);
                    await surface.CommitAsync(ct);
                    if (input.Event.State == KeyState.Pressed)
                        report.PressCommitMilliseconds.Add(Stopwatch.GetElapsedTime(input.ReceivedAt).TotalMilliseconds);
                }
                else
                {
                    var state = input.Event.State == KeyState.Pressed ? "appui" : "relâchement";
                    Console.WriteLine($"{input.LocalTime:HH:mm:ss.fff} {state} L{key.Row + 1} C{key.Column + 1}");
                    if (input.Event.State == KeyState.Pressed)
                        report.PressCount++;
                    else
                        report.ReleaseCount++;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false && listening.IsCancellationRequested)
        {
        }
        finally
        {
            surface.KeyChanged -= Receive;
            events.Writer.TryComplete();
            report.Durations.Add((step, animate ? "Animation" : "Écoute", elapsed.Elapsed));
        }
        ct.ThrowIfCancellationRequested();
    }

    private sealed record ProbeInput(KeyEvent Event, long ReceivedAt, DateTimeOffset LocalTime);
}
