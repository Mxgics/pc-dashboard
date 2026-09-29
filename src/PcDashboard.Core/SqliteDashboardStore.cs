using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PcDashboard.Core;

public sealed class SqliteDashboardStore : IDashboardStore, IActionStore
{
    public const int SchemaVersion = 2;
    private readonly string connectionString;
    public SqliteDashboardStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var version = db.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(version.ExecuteScalar()) > SchemaVersion)
            throw new InvalidOperationException("This database was created by a newer PC Dashboard. Reopen that version; do not reset this data.");
        using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS categories (name TEXT PRIMARY KEY COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS assignments (app_id TEXT PRIMARY KEY, category TEXT NOT NULL REFERENCES categories(name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS category_aliases (original TEXT PRIMARY KEY, category TEXT NOT NULL REFERENCES categories(name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS action_history (
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, subject TEXT NOT NULL, status TEXT NOT NULL,
                started_at TEXT NOT NULL, completed_at TEXT, summary TEXT, error_code INTEGER, details_json TEXT);
            CREATE INDEX IF NOT EXISTS ix_action_history_started ON action_history(started_at DESC);
            PRAGMA user_version=2;
            """;
        cmd.ExecuteNonQuery();
        transaction.Commit();
        foreach (var category in Categories.Defaults)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO categories SELECT $name WHERE NOT EXISTS (SELECT 1 FROM category_aliases WHERE original=$name); INSERT OR IGNORE INTO category_aliases VALUES ($name,$name)";
            insert.Parameters.AddWithValue("$name", category);
            insert.ExecuteNonQuery();
        }
        MarkInterruptedAsync().GetAwaiter().GetResult();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public async Task<Snapshot> LoadAsync(CancellationToken ct = default)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM snapshot WHERE id=1";
        return await cmd.ExecuteScalarAsync(ct) is string json
            ? JsonSerializer.Deserialize<Snapshot>(json) ?? Snapshot.Empty : Snapshot.Empty;
    }
    public async Task SaveAsync(Snapshot snapshot, CancellationToken ct = default)
    {
        using var db = Open(); using var transaction = db.BeginTransaction(); using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO snapshot VALUES (1, $json) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(snapshot));
        await cmd.ExecuteNonQueryAsync(ct);
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
    }
    public async Task<IReadOnlyList<string>> CategoriesAsync()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT name FROM categories ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync(); var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0)); return result;
    }
    public async Task<IReadOnlyDictionary<string, string>> AssignmentsAsync()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT app_id, category FROM assignments";
        using var reader = await cmd.ExecuteReaderAsync(); var result = new Dictionary<string, string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0), reader.GetString(1)); return result;
    }
    public async Task AddCategoryAsync(string name)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO categories VALUES ($name)";
        cmd.Parameters.AddWithValue("$name", Categories.Validate(name)); await cmd.ExecuteNonQueryAsync();
    }
    public async Task RenameCategoryAsync(string oldName, string newName)
    {
        Categories.Validate(oldName);
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE categories SET name=$new WHERE name=$old";
        cmd.Parameters.AddWithValue("$new", Categories.Validate(newName)); cmd.Parameters.AddWithValue("$old", oldName);
        if (await cmd.ExecuteNonQueryAsync() == 0) throw new ArgumentException("Select an existing category.");
    }
    public async Task<IReadOnlyDictionary<string, string>> CategoryAliasesAsync()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT original, category FROM category_aliases";
        using var reader = await cmd.ExecuteReaderAsync(); var result = new Dictionary<string, string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0), reader.GetString(1)); return result;
    }
    public async Task AssignAsync(string appId, string category)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO assignments VALUES ($id,$category) ON CONFLICT(app_id) DO UPDATE SET category=excluded.category";
        cmd.Parameters.AddWithValue("$id", appId); cmd.Parameters.AddWithValue("$category", category); await cmd.ExecuteNonQueryAsync();
    }

    public async Task StartActionAsync(ActionRecord record, CancellationToken ct = default)
    {
        using var db = Open(); using var transaction = db.BeginTransaction(); using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO action_history(id,kind,subject,status,started_at,completed_at,summary,error_code,details_json)
            VALUES($id,$kind,$subject,$status,$started,NULL,$summary,$code,$details);
            DELETE FROM action_history WHERE id NOT IN (
                SELECT id FROM action_history ORDER BY started_at DESC, id DESC LIMIT 100);
            """;
        cmd.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$kind", record.Kind.ToString());
        cmd.Parameters.AddWithValue("$subject", Bound(record.Subject, 500));
        cmd.Parameters.AddWithValue("$status", record.Status.ToString());
        cmd.Parameters.AddWithValue("$started", record.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$summary", record.Summary is null ? DBNull.Value : Bound(record.Summary, 2_000));
        cmd.Parameters.AddWithValue("$code", (object?)record.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$details", record.DetailsJson is null ? DBNull.Value : Bound(record.DetailsJson, 8_000));
        await cmd.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task CompleteActionAsync(Guid id, ActionResult result, CancellationToken ct = default)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE action_history
            SET status=$status,completed_at=$completed,summary=$summary,error_code=$code
            WHERE id=$id
            """;
        cmd.Parameters.AddWithValue("$status", result.Status.ToString());
        cmd.Parameters.AddWithValue("$completed", (result.CompletedAt ?? DateTimeOffset.UtcNow).ToString("O"));
        cmd.Parameters.AddWithValue("$summary", Bound(result.Message, 2_000));
        cmd.Parameters.AddWithValue("$code", (object?)result.InstallerErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("The action record no longer exists.");
    }

    public async Task<IReadOnlyList<ActionRecord>> RecentActionsAsync(CancellationToken ct = default)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,kind,subject,status,started_at,completed_at,summary,error_code,details_json
            FROM action_history ORDER BY started_at DESC, id DESC LIMIT 100
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<ActionRecord>();
        while (await reader.ReadAsync(ct))
            result.Add(new(Guid.Parse(reader.GetString(0)), Enum.Parse<ActionKind>(reader.GetString(1)),
                reader.GetString(2), Enum.Parse<ActionStatus>(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        return result;
    }

    public async Task MarkInterruptedAsync(CancellationToken ct = default)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE action_history
            SET status='Interrupted',completed_at=$now,
                summary='The app closed before this action reported a terminal result.'
            WHERE status IN ('Planned','Running')
            """;
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Bound(string value, int max)
        => value.Length <= max ? value : throw new ArgumentException($"Value exceeds {max} characters.");
}
