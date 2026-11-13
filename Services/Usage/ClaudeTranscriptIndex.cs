using System.IO;
using System.Threading;

namespace DockPad.Services.Usage;

/// <summary>
/// Garde les consommations par transcript : deux rafraîchissements du même fournisseur partagent le même parcours.
/// </summary>
internal sealed class ClaudeTranscriptIndex
{
    private sealed class Transcript
    {
        internal long Length;
        internal DateTime LastWrite;
        internal long Position;
        internal readonly List<UsageAggregator.UsageEntry> Entries = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Transcript> _files = new(StringComparer.OrdinalIgnoreCase);
    internal int FilesRead { get; private set; }

    internal List<UsageAggregator.UsageEntry> Read(string home, DateTime since, CancellationToken token = default)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<UsageAggregator.UsageEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in ClaudeUsageReader.ScanRoots(home))
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try
                {
                    files = Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { LogService.Warn(ex, $"Énumération des transcripts Claude ({root})"); continue; }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists || info.LastWriteTime < since) continue;
                        retained.Add(file);
                        _files.TryGetValue(file, out var state);
                        if (state == null || info.Length < state.Length
                            || (info.Length == state.Length && info.LastWriteTime != state.LastWrite))
                            _files[file] = state = new Transcript();

                        if (info.Length != state.Length || info.LastWriteTime != state.LastWrite)
                        {
                            FilesRead++;
                            state.Position = JsonlIncrementalReader.Read(file, state.Position, info.Length, line =>
                            {
                                var entry = ClaudeUsageReader.ParseLine(line);
                                if (entry != null && entry.Total != 0) state.Entries.Add(entry);
                            }, token);
                            state.Length = info.Length;
                            state.LastWrite = info.LastWriteTime;
                        }

                        // La date précède la déduplication, comme dans la lecture complète. Garder toutes les
                        // occurrences permet aussi à un autre fichier de gagner si le premier disparaît.
                        foreach (var entry in state.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            if (entry.Timestamp >= since && seen.Add(entry.Key)) entries.Add(entry);
                        }
                    }
                    catch (OperationCanceledException) { _files.Remove(file); throw; }
                    catch (Exception ex)
                    {
                        // Une lecture interrompue ne doit pas laisser un ajout partiel être rejoué au tick suivant.
                        _files.Remove(file);
                        LogService.Warn(ex, $"Lecture du transcript {Path.GetFileName(file)}");
                    }
                }
            }
            foreach (var file in _files.Keys.Where(file => !retained.Contains(file)).ToArray()) _files.Remove(file);
            token.ThrowIfCancellationRequested();
            return entries;
        }
    }
}
