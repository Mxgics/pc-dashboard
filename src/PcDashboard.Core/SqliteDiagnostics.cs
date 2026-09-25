using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace PcDashboard.Core;

/// <summary>Private diagnostics in the existing database, with no file fallback.</summary>
public sealed class SqliteDiagnostics
{
    public const int MaxEntries = 256;
    public const int MaxMessageLength = 4096;
    private readonly string connectionString;
    public SqliteDiagnostics(string databasePath)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 1 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS diagnostics (id INTEGER PRIMARY KEY, recorded_at TEXT NOT NULL, message TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS legacy_diagnostics (digest TEXT PRIMARY KEY, content TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public bool TryWrite(string message)
    {
        try
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO diagnostics(recorded_at,message) VALUES ($time,$message);
                DELETE FROM diagnostics WHERE id NOT IN (SELECT id FROM diagnostics ORDER BY id DESC LIMIT 256);
                """;
            cmd.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$message", message.Length > MaxMessageLength ? message[..MaxMessageLength] : message);
            cmd.ExecuteNonQuery();
            transaction.Commit();
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException) { return false; }
    }
    // Only explicitly selected legacy files. Preserve full content before removing the source.
    public void ImportLegacyFile(string path)
    {
        if (!File.Exists(path)) return;
        var content = File.ReadAllText(path);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        using (var db = Open())
        using (var transaction = db.BeginTransaction())
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO legacy_diagnostics(digest,content) VALUES ($digest,$content)";
            cmd.Parameters.AddWithValue("$digest", digest);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.ExecuteNonQuery();
            transaction.Commit();
        }
        File.Delete(path);
    }
}
