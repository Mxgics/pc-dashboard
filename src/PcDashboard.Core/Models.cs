namespace PcDashboard.Core;

public enum UpdateStatus { Unknown, Available, NoneReported, Unsupported, Failed }
public enum UpdateActionStatus { Planned, Rejected, Running, Succeeded, Failed, Cancelled, RestartRequired }
public sealed record UpdateEvidence(UpdateStatus Status, string Source, string? PackageId = null,
    string? AvailableVersion = null, DateTimeOffset? CheckedAt = null, string? Note = null, string? Pin = null);
public sealed record InstalledApp(string Id, string Name, string? Version, string? Publisher,
    string Source, string NativeId, string Scope, string Architecture, DateTimeOffset ObservedAt,
    UpdateEvidence? Update = null);
public sealed record SourceHealth(string Source, DateTimeOffset AttemptedAt, bool Success, string? Error = null);
public sealed record InventoryResult(string Source, IReadOnlyList<InstalledApp> Apps, SourceHealth Health);
public sealed record UpdateResult(IReadOnlyDictionary<string, UpdateEvidence> Evidence, SourceHealth Health);
public sealed record UpdateRequest(string AppId, string PackageId, string Source, string ExpectedInstalledVersion,
    string TargetVersion, DateTimeOffset ApprovedAt, string ApprovalToken);
public sealed record UpdateActionResult(UpdateActionStatus Status, string AppId, string PackageId,
    string? Message = null, int? InstallerErrorCode = null, bool RestartRequired = false,
    DateTimeOffset? CompletedAt = null);
public sealed record Snapshot(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<SourceHealth> Health)
{
    public static Snapshot Empty => new([], []);
}
public interface IInventoryProvider
{
    string Source { get; }
    Task<InventoryResult> CollectAsync(CancellationToken cancellationToken);
}
public interface IUpdateProvider
{
    Task<UpdateResult> CheckAsync(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken);
}
public interface IUpdateExecutor
{
    Task<UpdateActionResult> ExecuteAsync(UpdateRequest request, InstalledApp app, CancellationToken cancellationToken);
}
public interface IDashboardStore
{
    Task<Snapshot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(Snapshot snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<string>> CategoriesAsync();
    Task<IReadOnlyDictionary<string, string>> AssignmentsAsync();
    Task<IReadOnlyDictionary<string, string>> CategoryAliasesAsync();
    Task AddCategoryAsync(string name);
    Task RenameCategoryAsync(string oldName, string newName);
    Task AssignAsync(string appId, string category);
}
public static class Categories
{
    public static readonly string[] Defaults = ["Coding", "Gaming", "Productivity", "Utilities", "Uncategorized"];
    public static string Suggest(InstalledApp app)
    {
        var name = app.Name.ToLowerInvariant();
        if (new[] { "steam", "epic games", "xbox", "gog", "battle.net" }.Any(name.Contains)) return "Gaming";
        if (new[] { "visual studio", "node.js", "python", "docker", "postgres", "sql server", "jetbrains" }.Any(name.Contains)
            || System.Text.RegularExpressions.Regex.IsMatch(name, @"(^|\s)(git|\.net)(\s|$)")) return "Coding";
        if (new[] { "office", "word", "excel", "onenote", "notion", "teams" }.Any(name.Contains)) return "Productivity";
        if (new[] { "7-zip", "powertoys", "winrar", "everything" }.Any(name.Contains)) return "Utilities";
        return "Uncategorized";
    }
    public static string Validate(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl))
            throw new ArgumentException("Use a category name of 1–40 characters without control characters.");
        return name;
    }
}
