using System.Security.Cryptography;
using System.Text.Json;

namespace PcDashboard.Core;

public enum UpdateStatus { Unknown, Available, NoneReported, Unsupported, Failed }
public enum ActionKind { Update }
public enum ActionStatus { Planned, Rejected, Running, Succeeded, SucceededUnverified, Failed, Cancelled, RestartRequired, Interrupted }
public enum ActionProgressStage { Queued, Downloading, Installing, PostInstall, Verifying, Completed }
public sealed record UpdateEvidence(UpdateStatus Status, string Source, string? PackageId = null,
    string? AvailableVersion = null, DateTimeOffset? CheckedAt = null, string? Note = null, string? Pin = null);
public sealed record InstalledApp(string Id, string Name, string? Version, string? Publisher,
    string Source, string NativeId, string Scope, string Architecture, DateTimeOffset ObservedAt,
    UpdateEvidence? Update = null);
public sealed record SourceHealth(string Source, DateTimeOffset AttemptedAt, bool Success, string? Error = null);
public sealed record InventoryResult(string Source, IReadOnlyList<InstalledApp> Apps, SourceHealth Health);
public sealed record UpdateResult(IReadOnlyDictionary<string, UpdateEvidence> Evidence, SourceHealth Health);
public sealed record Snapshot(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<SourceHealth> Health)
{
    public static Snapshot Empty => new([], []);
}

public sealed record PackageAgreementInfo(string Label, string? Text, string? Url);
public sealed record UpdateResolution(string AppId, string AppName, string PackageId, string Source,
    string InstalledVersion, string TargetVersion, string Scope, string InstallerType,
    string ElevationRequirement, IReadOnlyList<PackageAgreementInfo> Agreements);
public sealed record UpdatePlan(Guid PlanId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    UpdateResolution Resolution, string Digest);
public sealed record UpdateApproval(Guid PlanId, string Digest, DateTimeOffset ApprovedAt,
    bool AgreementsAccepted, string ApprovalToken);
public sealed record ActionProgress(ActionProgressStage Stage, string Message,
    ulong? BytesDownloaded = null, ulong? BytesRequired = null, double? DownloadPercent = null,
    double? InstallPercent = null, bool CanCancel = true);
public sealed record ActionResult(ActionStatus Status, string Subject, string Message,
    int? InstallerErrorCode = null, bool RestartRequired = false, DateTimeOffset? CompletedAt = null);
public sealed record ActionRecord(Guid Id, ActionKind Kind, string Subject, ActionStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt = null, string? Summary = null,
    int? ErrorCode = null, string? DetailsJson = null);

public static class PlanDigests
{
    public static string Compute<T>(T value)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    public static bool Equals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException) { return false; }
    }
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
public interface IUpdateResolver
{
    Task<UpdateResolution> ResolveAsync(InstalledApp app, CancellationToken cancellationToken);
}
public interface IUpdateExecutor
{
    Task<ActionResult> ExecuteAsync(UpdatePlan plan, IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken);
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
public interface IActionStore
{
    Task StartActionAsync(ActionRecord record, CancellationToken ct = default);
    Task CompleteActionAsync(Guid id, ActionResult result, CancellationToken ct = default);
    Task<IReadOnlyList<ActionRecord>> RecentActionsAsync(CancellationToken ct = default);
    Task MarkInterruptedAsync(CancellationToken ct = default);
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
