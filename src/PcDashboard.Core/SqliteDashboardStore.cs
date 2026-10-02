using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PcDashboard.Core;

public sealed class SqliteDashboardStore : IDashboardStore, IActionStore, ISetupProfileStore, IBackupStore
{
    public const int SchemaVersion = 3;
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
            CREATE TABLE IF NOT EXISTS setup_profiles (
                id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE);
            CREATE TABLE IF NOT EXISTS setup_profile_items (
                id TEXT PRIMARY KEY,
                profile_id TEXT NOT NULL REFERENCES setup_profiles(id) ON DELETE CASCADE,
                package_id TEXT NOT NULL, source TEXT NOT NULL, scope TEXT NOT NULL,
                UNIQUE(profile_id, package_id, source, scope));
            CREATE INDEX IF NOT EXISTS ix_setup_profile_items_profile
                ON setup_profile_items(profile_id);
            PRAGMA user_version=3;
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

    public async Task<IReadOnlyList<SetupProfile>> SetupProfilesAsync(
        CancellationToken ct = default)
    {
        using var db = Open();
        var result = new List<SetupProfile>();
        using var profiles = db.CreateCommand();
        profiles.CommandText = "SELECT id,name FROM setup_profiles ORDER BY name,id";
        using var reader = await profiles.ExecuteReaderAsync(ct);
        var headers = new List<(Guid Id, string Name)>();
        while (await reader.ReadAsync(ct))
            headers.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        await reader.DisposeAsync();

        foreach (var header in headers)
        {
            using var items = db.CreateCommand();
            items.CommandText = """
                SELECT id,package_id,source,scope FROM setup_profile_items
                WHERE profile_id=$profile ORDER BY rowid
                """;
            items.Parameters.AddWithValue("$profile", header.Id.ToString("D"));
            using var itemReader = await items.ExecuteReaderAsync(ct);
            var profileItems = new List<SetupProfileItem>();
            while (await itemReader.ReadAsync(ct))
                profileItems.Add(new(Guid.Parse(itemReader.GetString(0)),
                    itemReader.GetString(1), itemReader.GetString(2), itemReader.GetString(3)));
            result.Add(new(header.Id, header.Name, profileItems));
        }
        return result;
    }

    public async Task SaveSetupProfileAsync(SetupProfile profile,
        CancellationToken ct = default)
    {
        ValidateSetupProfile(profile);
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using (var save = db.CreateCommand())
        {
            save.Transaction = transaction;
            save.CommandText = """
                INSERT INTO setup_profiles(id,name) VALUES($id,$name)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name;
                DELETE FROM setup_profile_items WHERE profile_id=$id;
                """;
            save.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
            save.Parameters.AddWithValue("$name", profile.Name.Trim());
            await save.ExecuteNonQueryAsync(ct);
        }
        foreach (var item in profile.Items)
        {
            using var saveItem = db.CreateCommand();
            saveItem.Transaction = transaction;
            saveItem.CommandText = """
                INSERT INTO setup_profile_items(id,profile_id,package_id,source,scope)
                VALUES($id,$profile,$package,$source,$scope)
                """;
            saveItem.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            saveItem.Parameters.AddWithValue("$profile", profile.Id.ToString("D"));
            saveItem.Parameters.AddWithValue("$package", item.PackageId);
            saveItem.Parameters.AddWithValue("$source", item.Source);
            saveItem.Parameters.AddWithValue("$scope", item.Scope);
            await saveItem.ExecuteNonQueryAsync(ct);
        }
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    public async Task DeleteSetupProfileAsync(Guid id, CancellationToken ct = default)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM setup_profiles WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BackupPayload> ExportBackupAsync(CancellationToken ct = default)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        var categories = new List<string>();
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM categories ORDER BY name";
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) categories.Add(reader.GetString(0));
        }
        var aliases = new List<BackupAlias>();
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT original,category FROM category_aliases ORDER BY original";
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) aliases.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        var assignments = new List<BackupAssignment>();
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT app_id,category FROM assignments ORDER BY app_id";
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) assignments.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        var profiles = new List<SetupProfile>();
        using (var headers = db.CreateCommand())
        {
            headers.Transaction = transaction;
            headers.CommandText = "SELECT id,name FROM setup_profiles ORDER BY name,id";
            using var reader = await headers.ExecuteReaderAsync(ct);
            var profileHeaders = new List<(Guid Id, string Name)>();
            while (await reader.ReadAsync(ct))
                profileHeaders.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
            await reader.DisposeAsync();
            foreach (var header in profileHeaders)
            {
                using var items = db.CreateCommand();
                items.Transaction = transaction;
                items.CommandText = "SELECT id,package_id,source,scope FROM setup_profile_items WHERE profile_id=$profile ORDER BY rowid";
                items.Parameters.AddWithValue("$profile", header.Id.ToString("D"));
                using var itemReader = await items.ExecuteReaderAsync(ct);
                var profileItems = new List<SetupProfileItem>();
                while (await itemReader.ReadAsync(ct))
                    profileItems.Add(new(Guid.Parse(itemReader.GetString(0)), itemReader.GetString(1),
                        itemReader.GetString(2), itemReader.GetString(3)));
                profiles.Add(new(header.Id, header.Name, profileItems));
            }
        }
        transaction.Commit();
        return new(BackupService.ArchiveVersion, DateTimeOffset.UtcNow,
            categories, aliases, assignments, profiles);
    }

    public async Task RestoreBackupAsync(BackupPayload payload, CancellationToken ct = default)
    {
        BackupService.ValidatePayload(payload);
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using (var clear = db.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM setup_profile_items; DELETE FROM setup_profiles; DELETE FROM assignments; DELETE FROM category_aliases; DELETE FROM categories;";
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach (var category in payload.Categories)
        {
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO categories(name) VALUES($name)";
            insert.Parameters.AddWithValue("$name", category);
            await insert.ExecuteNonQueryAsync(ct);
        }
        foreach (var alias in payload.Aliases)
        {
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO category_aliases(original,category) VALUES($original,$category)";
            insert.Parameters.AddWithValue("$original", alias.Original);
            insert.Parameters.AddWithValue("$category", alias.Category);
            await insert.ExecuteNonQueryAsync(ct);
        }
        foreach (var assignment in payload.Assignments)
        {
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO assignments(app_id,category) VALUES($app,$category)";
            insert.Parameters.AddWithValue("$app", assignment.AppId);
            insert.Parameters.AddWithValue("$category", assignment.Category);
            await insert.ExecuteNonQueryAsync(ct);
        }
        foreach (var profile in payload.Profiles)
        {
            using (var insert = db.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO setup_profiles(id,name) VALUES($id,$name)";
                insert.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
                insert.Parameters.AddWithValue("$name", profile.Name.Trim());
                await insert.ExecuteNonQueryAsync(ct);
            }
            foreach (var item in profile.Items)
            {
                using var insertItem = db.CreateCommand();
                insertItem.Transaction = transaction;
                insertItem.CommandText = "INSERT INTO setup_profile_items(id,profile_id,package_id,source,scope) VALUES($id,$profile,$package,$source,$scope)";
                insertItem.Parameters.AddWithValue("$id", item.Id.ToString("D"));
                insertItem.Parameters.AddWithValue("$profile", profile.Id.ToString("D"));
                insertItem.Parameters.AddWithValue("$package", item.PackageId);
                insertItem.Parameters.AddWithValue("$source", item.Source);
                insertItem.Parameters.AddWithValue("$scope", item.Scope);
                await insertItem.ExecuteNonQueryAsync(ct);
            }
        }
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    internal static void ValidateSetupProfile(SetupProfile profile)
    {
        var name = profile.Name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use a profile name of 1–80 characters without control characters.");
        if (profile.Items.Count > 500)
            throw new ArgumentException("A setup profile can contain at most 500 packages.");
        if (profile.Items.Select(item => item.Id).Distinct().Count() != profile.Items.Count)
            throw new ArgumentException("Setup item identifiers must be unique.");
        if (profile.Items.Select(item => (item.PackageId.ToUpperInvariant(),
                item.Source.ToUpperInvariant(), item.Scope)).Distinct().Count() != profile.Items.Count)
            throw new ArgumentException("A profile cannot contain the same package, source, and scope twice.");
        foreach (var item in profile.Items)
        {
            if (item.PackageId.Length is < 1 or > 200 || item.PackageId.Any(char.IsControl)
                || item.Source.Length is < 1 or > 200 || item.Source.Any(char.IsControl)
                || item.Scope is not ("User" or "Machine"))
                throw new ArgumentException("A setup item has an invalid package, source, or exact scope.");
        }
    }

    private static string Bound(string value, int max)
        => value.Length <= max ? value : throw new ArgumentException($"Value exceeds {max} characters.");
}
