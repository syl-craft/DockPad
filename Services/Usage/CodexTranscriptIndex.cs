using System.IO;
using System.Threading;

namespace DockPad.Services.Usage;

/// <summary>Conserve le contexte du tour et les relevés déjà lus, séparément pour chaque rollout.</summary>
internal sealed class CodexTranscriptIndex
{
    private sealed class Transcript
    {
        internal long Length;
        internal DateTime LastWrite;
        internal long Position;
        internal int Index;
        internal string Model = "";
        internal CodexQuotaSnapshot? Quota;
        internal readonly List<UsageAggregator.UsageEntry> Entries = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Transcript> _files = new(StringComparer.OrdinalIgnoreCase);
    internal int FilesRead { get; private set; }

    internal CodexUsageReader.Snapshot ReadSnapshot(string home, DateTime since, DateTime quotaSince,
        CancellationToken token = default)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            var oldestUseful = since < quotaSince ? since : quotaSince;
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<UsageAggregator.UsageEntry>();
            CodexQuotaSnapshot? quota = null;
            foreach (var root in CodexUsageReader.ScanRoots(home).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try
                {
                    files = Directory.GetFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { LogService.Warn(ex, $"Énumération des sessions Codex ({root})"); continue; }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists || info.LastWriteTime < oldestUseful) continue;
                        retained.Add(file);
                        _files.TryGetValue(file, out var state);
                        if (state == null || info.Length < state.Length
                            || (info.Length == state.Length && info.LastWriteTime != state.LastWrite))
                            _files[file] = state = new Transcript();

                        if (info.Length != state.Length || info.LastWriteTime != state.LastWrite)
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            FilesRead++;
                            state.Position = JsonlIncrementalReader.Read(file, state.Position, info.Length, line =>
                            {
                                state.Index++;
                                if (line.Contains("turn_context", StringComparison.Ordinal))
                                {
                                    if (CodexUsageReader.TurnModel(line) is { } model) state.Model = model;
                                    return;
                                }
                                if (!line.Contains("token_count", StringComparison.Ordinal)) return;
                                var candidate = CodexQuotaReader.ParseLine(line);
                                if (candidate != null && (state.Quota == null || candidate.ObservedAt >= state.Quota.ObservedAt))
                                    state.Quota = candidate;
                                var entry = CodexUsageReader.ParseLine(line, name, state.Index, state.Model);
                                if (entry != null) state.Entries.Add(entry);
                            }, token);
                            state.Length = info.Length;
                            state.LastWrite = info.LastWriteTime;
                        }

                        foreach (var entry in state.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            if (entry.Timestamp >= since) entries.Add(entry);
                        }
                        // Le quota suit la borne d'ouverture des fichiers de la lecture complète ; son
                        // horodatage propre départage les relevés, même s'ils sont écrits dans le désordre.
                        if (state.Quota is { } latest && (quota == null || latest.ObservedAt >= quota.ObservedAt))
                            quota = latest;
                    }
                    catch (OperationCanceledException) { _files.Remove(file); throw; }
                    catch (Exception ex)
                    {
                        _files.Remove(file);
                        LogService.Warn(ex, $"Lecture de la session Codex {Path.GetFileName(file)}");
                    }
                }
            }
            foreach (var file in _files.Keys.Where(file => !retained.Contains(file)).ToArray()) _files.Remove(file);
            token.ThrowIfCancellationRequested();
            return new CodexUsageReader.Snapshot(entries, quota);
        }
    }
}
