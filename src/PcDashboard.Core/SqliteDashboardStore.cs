using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PcDashboard.Core;

public sealed class SqliteDashboardStore : IDashboardStore
{
    private readonly string connectionString;
    public SqliteDashboardStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var db = Open();
        using (var version = db.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar()) > 1) throw new InvalidOperationException("This database was created by a newer PC Dashboard. Reopen that version; do not reset this data.");
        }
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS categories (name TEXT PRIMARY KEY COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS assignments (app_id TEXT PRIMARY KEY, category TEXT NOT NULL REFERENCES categories(name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS category_aliases (original TEXT PRIMARY KEY, category TEXT NOT NULL REFERENCES categories(name) ON UPDATE CASCADE);
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery();
        foreach (var category in Categories.Defaults)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO categories SELECT $name WHERE NOT EXISTS (SELECT 1 FROM category_aliases WHERE original=$name); INSERT OR IGNORE INTO category_aliases VALUES ($name,$name)";
            insert.Parameters.AddWithValue("$name", category);
            insert.ExecuteNonQuery();
        }
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
}
