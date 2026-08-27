using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit la base locale de GitHub Copilot CLI.
/// </summary>
/// <remarks>
/// <para>
/// Source : <c>~/.copilot/session-store.db</c>, table <c>assistant_usage_events</c> — une ligne par
/// appel facturé, avec <c>model</c>, <c>input_tokens</c>, <c>output_tokens</c>,
/// <c>cache_read_tokens</c>, <c>cache_write_tokens</c> et <c>created_at</c>.
/// </para>
/// <para>
/// C'est la seule source du projet qui soit une base de données, d'où la dépendance
/// <c>Microsoft.Data.Sqlite</c> — et les binaires natifs SQLite qu'elle embarque dans la
/// publication. Ouverture en lecture seule : Copilot garde sa base ouverte.
/// </para>
/// <para>
/// Une ligne par appel signifie que les appels d'un sous-agent comptent aussi, ce qui est correct :
/// ce sont des requêtes distinctes, pas une copie du tour parent.
/// </para>
/// </remarks>
public static class CopilotUsageReader
{
    /// <summary>Variable d'environnement qui déplace le dossier de Copilot, comme le fait sa CLI.</summary>
    public const string HomeVariable = "COPILOT_HOME";

    private const string Table = "assistant_usage_events";

    /// <summary>
    /// Chemin de la base. <b>Seule</b> fonction qui sait où chercher : le scan, la détection et les
    /// tests l'appellent tous.
    /// </summary>
    public static string DatabasePath(string home)
    {
        var root = Environment.GetEnvironmentVariable(HomeVariable);
        var copilot = string.IsNullOrWhiteSpace(root) ? Path.Combine(home, ".copilot") : root.Trim().Trim('"');
        return Path.Combine(copilot, "session-store.db");
    }

    /// <summary>Toutes les consommations postérieures à <paramref name="since"/> (heure locale).</summary>
    public static List<UsageAggregator.UsageEntry> Read(string home, DateTime since)
    {
        var entries = new List<UsageAggregator.UsageEntry>();
        var path = DatabasePath(home);
        if (!File.Exists(path)) return entries;

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT id, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, created_at " +
                $"FROM {Table} WHERE created_at >= $cutoff";
            // `created_at` est du texte : la comparaison est lexicographique, donc seulement un
            // pré-filtre grossier. On recule d'un jour entier pour qu'aucune ligne de la fenêtre ne
            // passe à la trappe à cause d'un décalage horaire écrit dans la valeur ; les dates sont
            // ensuite filtrées pour de vrai après analyse.
            command.Parameters.AddWithValue("$cutoff",
                since.AddDays(-1).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = ReadRow(reader, path);
                if (entry is null || entry.Timestamp < since) continue;
                entries.Add(entry);
            }
        }
        catch (SqliteException ex)
        {
            // Base verrouillée, table absente (schéma changé), fichier illisible : on n'affiche rien
            // pour ce fournisseur, ce qui est mieux qu'un total faux.
            LogService.Warn(ex, "Lecture de la base Copilot");
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture de la base Copilot");
        }

        return entries;
    }

    /// <remarks>
    /// <c>input_tokens</c> est le prompt entier : les lectures et écritures de cache en sont un
    /// sous-ensemble. Les soustraire évite de compter le même prompt trois fois — le piège relevé
    /// dans l'implémentation de référence.
    /// </remarks>
    /// <summary>
    /// Horodatage d'une ligne, en heure locale. <c>null</c> si la valeur n'est pas une date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>AssumeUniversal</c> est la clé.</b> La colonne est du TEXT, et une date SQLite sans
    /// marqueur de fuseau est de l'UTC — c'est ce que rend <c>CURRENT_TIMESTAMP</c>. Sans ce
    /// drapeau, .NET interprète une chaîne nue comme de l'<b>heure locale</b> : sur un poste à
    /// UTC+2, chaque ligne était datée de deux heures trop tard, et celles écrites en fin de
    /// journée UTC basculaient au lendemain. Un décalage qui ne se voit qu'aux bornes — d'où un
    /// test qui ne tombait qu'entre minuit et deux heures du matin.
    /// </para>
    /// <para>
    /// Un décalage explicite dans la chaîne (<c>Z</c>, <c>+01:00</c>) reste prioritaire : le
    /// drapeau ne s'applique qu'à défaut d'information.
    /// </para>
    /// </remarks>
    public static DateTime? ParseTimestamp(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var utc)
            ? utc.LocalDateTime
            : null;

    private static UsageAggregator.UsageEntry? ReadRow(SqliteDataReader row, string database)
    {
        var raw = row.IsDBNull(6) ? null : row.GetString(6);
        if (ParseTimestamp(raw) is not { } timestamp) return null;

        var input = Number(row, 2);
        var cacheRead = Number(row, 4);
        var cacheWrite = Number(row, 5);

        return new UsageAggregator.UsageEntry(
            // L'identifiant de ligne n'est unique que dans une base, et COPILOT_HOME peut en
            // désigner plusieurs : sans le chemin dans la clé, la ligne 1 de chaque base se
            // confondrait avec l'autre.
            Key: $"copilot|{database}|{row.GetInt64(0)}",
            Timestamp: timestamp,
            Model: row.IsDBNull(1) ? "" : row.GetString(1),
            Input: Math.Max(0, input - cacheRead - cacheWrite),
            Output: Number(row, 3),
            CacheWrite: cacheWrite,
            CacheRead: cacheRead);
    }

    private static long Number(SqliteDataReader row, int index) =>
        row.IsDBNull(index) ? 0 : row.GetInt64(index);
}
