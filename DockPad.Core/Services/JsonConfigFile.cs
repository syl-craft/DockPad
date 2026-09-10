using System.IO;
using System.Text.Json;

namespace DockPad.Services;

/// <summary>Lecture JSON avec valeurs par défaut et sauvegarde atomique bloquée après une erreur de lecture.</summary>
public static class JsonConfigFile
{
    private static readonly HashSet<string> FailedReads = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsReadOnly(string path)
    {
        lock (ConfigLock.Gate) return FailedReads.Contains(Path.GetFullPath(path));
    }

    public static T Load<T>(string path, JsonSerializerOptions options, Action<T>? normalize = null)
        where T : class, new()
    {
        path = Path.GetFullPath(path);
        lock (ConfigLock.Gate)
        {
            try
            {
                var value = Read<T>(path, options);
                normalize?.Invoke(value);
                FailedReads.Remove(path);
                return value;
            }
            catch (FileNotFoundException) { FailedReads.Remove(path); return new T(); }
            catch (DirectoryNotFoundException) { FailedReads.Remove(path); return new T(); }
            catch (Exception ex)
            {
                FailedReads.Add(path);
                LogService.Warn(ex, $"Lecture de {Path.GetFileName(path)} : sauvegarde bloquée jusqu'à une lecture réussie");
                return new T();
            }
        }
    }

    public static void Save<T>(string path, T value, JsonSerializerOptions options) where T : class
    {
        path = Path.GetFullPath(path);
        lock (ConfigLock.Gate)
        {
            if (FailedReads.Contains(path))
                throw new IOException($"Sauvegarde refusée : relire ou restaurer la configuration {path} après l'erreur de lecture.");

            // Valide le fichier existant avant son remplacement.
            try { _ = Read<T>(path, options); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            var json = JsonSerializer.Serialize(value, options);
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temp); }
                catch (Exception ex) { LogService.Warn(ex, "Nettoyage du temporaire de configuration"); }
            }
        }
    }

    private static T Read<T>(string path, JsonSerializerOptions options) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), options)
        ?? throw new JsonException($"Configuration null : {path}");
}
