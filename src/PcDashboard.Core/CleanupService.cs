using System.Text.Json;

namespace PcDashboard.Core;

/// <summary>Scans and deletes only revalidated, explicitly selected files under the canonical user TEMP root.</summary>
public sealed class CleanupService
{
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);
    public const int MaxCandidates = 50_000;

    private readonly IActionStore actions;
    private readonly OperationCoordinator operations;
    private readonly TimeProvider timeProvider;
    private readonly string root;
    private readonly string rootPrefix;
    private readonly bool productionRoot;

    public CleanupService(IActionStore actions, OperationCoordinator operations,
        TimeProvider timeProvider, string? rootOverride = null)
    {
        this.actions = actions;
        this.operations = operations;
        this.timeProvider = timeProvider;
        productionRoot = rootOverride is null;
        root = Canonical(rootOverride ?? UserTempPath());
        rootPrefix = root + Path.DirectorySeparatorChar;
    }

    public async Task<CleanupPlan> ScanAsync(CancellationToken ct = default)
    {
        ValidateRoot();
        var lease = await operations.TryBeginRefreshAsync(ct)
            ?? throw new InvalidOperationException("Another refresh or action is already running.");
        await using (lease)
            return await Task.Run(() => ScanCore(ct), ct);
    }

    public CleanupPlan Select(CleanupPlan scan, IEnumerable<string> selectedPaths)
    {
        ValidatePlanRoot(scan);
        var selected = selectedPaths.Select(Canonical)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = scan.Files.Where(file => selected.Contains(file.Path)).ToArray();
        return scan with
        {
            Files = files,
            Digest = Digest(scan.PlanId, scan.CreatedAt, scan.ExpiresAt, scan.Root,
                files, scan.ScanIncomplete)
        };
    }

    public async Task<CleanupResult> ExecuteApprovedAsync(CleanupPlan plan,
        CleanupApproval approval, IProgress<ActionProgress>? progress = null,
        CancellationToken ct = default)
    {
        string? rejection;
        try
        {
            ValidateRoot();
            ValidatePlanRoot(plan);
            rejection = ValidateApproval(plan, approval, timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            rejection = ex.Message;
        }
        if (rejection is not null)
            return new(ActionStatus.Rejected, 0, plan.Files.Count, 0, 0, rejection);
        if (plan.Files.Count == 0)
            return new(ActionStatus.Rejected, 0, 0, 0, 0,
                "Select at least one scanned file before approving cleanup.");

        IAsyncDisposable? lease;
        try { lease = await operations.TryBeginAsync(ActionKind.Cleanup, ct); }
        catch (OperationCanceledException)
        {
            return new(ActionStatus.Cancelled, 0, plan.Files.Count, 0, 0,
                "Cleanup cancelled before the first file.");
        }
        if (lease is null)
            return new(ActionStatus.Rejected, 0, plan.Files.Count, 0, 0,
                "Another refresh or action is already running.");

        var actionId = Guid.NewGuid();
        var started = false;
        var deleted = 0;
        var skipped = 0;
        var failed = 0;
        long bytesDeleted = 0;
        var cancelled = false;
        var candidateParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (lease)
        {
            try
            {
                await actions.StartActionAsync(new(actionId, ActionKind.Cleanup,
                    "Current user temporary files", ActionStatus.Running,
                    timeProvider.GetUtcNow(), DetailsJson: JsonSerializer.Serialize(new
                    {
                        SelectedFiles = plan.Files.Count,
                        SelectedBytes = plan.TotalBytes,
                        plan.ScanIncomplete
                    })), ct);
                started = true;

                for (var index = 0; index < plan.Files.Count; index++)
                {
                    if (ct.IsCancellationRequested) { cancelled = true; break; }
                    var candidate = plan.Files[index];
                    progress?.Report(new(ActionProgressStage.Queued,
                        $"Checking file {index + 1} of {plan.Files.Count}.",
                        CanCancel: true));
                    try
                    {
                        var info = Revalidate(candidate);
                        if (info is null) { skipped++; continue; }
                        var parent = info.DirectoryName;
                        info.Delete();
                        deleted++;
                        bytesDeleted += candidate.Length;
                        if (parent is not null) AddParents(parent, candidateParents);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                        or System.Security.SecurityException or NotSupportedException)
                    {
                        failed++;
                    }
                }
                RemoveEmptyCandidateDirectories(candidateParents);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }

        var status = cancelled ? ActionStatus.Cancelled
            : failed > 0 ? ActionStatus.Failed : ActionStatus.Succeeded;
        var message = $"Deleted {deleted}; skipped {skipped}; failed {failed}; "
            + $"removed {bytesDeleted} bytes."
            + (cancelled ? " Cleanup stopped before the next file." : string.Empty);
        progress?.Report(new(ActionProgressStage.Completed, message, CanCancel: false));
        if (started)
            await actions.CompleteActionAsync(actionId,
                new(status, "Current user temporary files", message,
                    CompletedAt: timeProvider.GetUtcNow()), CancellationToken.None);
        return new(status, deleted, skipped, failed, bytesDeleted, message);
    }

    private CleanupPlan ScanCore(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var files = new List<CleanupCandidate>();
        var pending = new Stack<string>();
        var incomplete = false;
        pending.Push(root);
        while (pending.Count > 0 && files.Count < MaxCandidates)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var canonical = Canonical(entry);
                        if (!IsInsideRoot(canonical)) { incomplete = true; continue; }
                        var attributes = File.GetAttributes(canonical);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(canonical);
                            continue;
                        }
                        var info = new FileInfo(canonical);
                        info.Refresh();
                        if (!info.Exists || now - Utc(info.LastWriteTimeUtc) < MinimumAge) continue;
                        files.Add(new(canonical, info.Length, Utc(info.LastWriteTimeUtc), false));
                        if (files.Count == MaxCandidates) { incomplete = true; break; }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                        or System.Security.SecurityException or NotSupportedException)
                    {
                        incomplete = true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                incomplete = true;
            }
        }
        if (pending.Count > 0) incomplete = true;
        var ordered = files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var id = Guid.NewGuid();
        var expires = now + ApprovalLifetime;
        return new(id, now, expires, root, ordered, incomplete,
            Digest(id, now, expires, root, ordered, incomplete));
    }

    private FileInfo? Revalidate(CleanupCandidate candidate)
    {
        var canonical = Canonical(candidate.Path);
        if (!string.Equals(canonical, candidate.Path, StringComparison.OrdinalIgnoreCase)
            || !IsInsideRoot(canonical) || candidate.IsReparsePoint
            || !ParentChainIsRegular(canonical))
            return null;
        var info = new FileInfo(canonical);
        info.Refresh();
        if (!info.Exists || (info.Attributes & (FileAttributes.Directory
                | FileAttributes.ReparsePoint)) != 0
            || info.Length != candidate.Length
            || Utc(info.LastWriteTimeUtc) != candidate.LastWriteUtc
            || timeProvider.GetUtcNow() - Utc(info.LastWriteTimeUtc) < MinimumAge)
            return null;
        return info;
    }

    private void AddParents(string directory, HashSet<string> parents)
    {
        var current = Canonical(directory);
        while (IsInsideRoot(current))
        {
            parents.Add(current);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null) break;
            current = Canonical(parent);
        }
    }

    private void RemoveEmptyCandidateDirectories(IEnumerable<string> parents)
    {
        foreach (var directory in parents.OrderByDescending(path => path.Length))
        {
            try
            {
                var canonical = Canonical(directory);
                if (!IsInsideRoot(canonical) || !ParentChainIsRegular(canonical)) continue;
                var attributes = File.GetAttributes(canonical);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (!Directory.EnumerateFileSystemEntries(canonical).Any())
                    Directory.Delete(canonical);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.SecurityException or DirectoryNotFoundException) { }
        }
    }

    private void ValidateRoot()
    {
        if (productionRoot
            && !string.Equals(root, Canonical(UserTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current user TEMP location changed; scan again.");
        if (!Directory.Exists(root))
            throw new IOException("The current user TEMP directory is unavailable.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The current user TEMP root is a link or reparse point.");
    }

    private void ValidatePlanRoot(CleanupPlan plan)
    {
        if (!string.Equals(plan.Root, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The cleanup root changed; scan again.");
    }

    private bool IsInsideRoot(string path)
        => path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);

    private bool ParentChainIsRegular(string path)
    {
        var current = Directory.GetParent(path)?.FullName;
        while (current is not null)
        {
            var canonical = Canonical(current);
            if (string.Equals(canonical, root, StringComparison.OrdinalIgnoreCase))
                return Directory.Exists(canonical)
                    && (File.GetAttributes(canonical) & FileAttributes.ReparsePoint) == 0;
            if (!IsInsideRoot(canonical) || !Directory.Exists(canonical)
                || (File.GetAttributes(canonical) & FileAttributes.ReparsePoint) != 0)
                return false;
            current = Directory.GetParent(canonical)?.FullName;
        }
        return false;
    }

    private static string? ValidateApproval(CleanupPlan plan, CleanupApproval approval,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(approval.ApprovalToken))
            return "Explicit approval is required.";
        if (approval.PlanId != plan.PlanId || !PlanDigests.Equals(approval.Digest, plan.Digest))
            return "The approval does not match this cleanup plan.";
        if (!PlanDigests.Equals(plan.Digest, Digest(plan.PlanId, plan.CreatedAt,
                plan.ExpiresAt, plan.Root, plan.Files, plan.ScanIncomplete)))
            return "The cleanup plan was altered.";
        if (plan.ExpiresAt - plan.CreatedAt != ApprovalLifetime || plan.CreatedAt > now
            || approval.ApprovedAt < plan.CreatedAt || approval.ApprovedAt > plan.ExpiresAt
            || approval.ApprovedAt > now || now > plan.ExpiresAt)
            return "Approval expired; scan again.";
        return null;
    }

    private static string Digest(Guid id, DateTimeOffset created, DateTimeOffset expires,
        string root, IReadOnlyList<CleanupCandidate> files, bool incomplete)
        => PlanDigests.Compute(new { id, created, expires, root, files, incomplete });

    private static string UserTempPath()
        => Environment.GetEnvironmentVariable("TEMP")
            ?? throw new InvalidOperationException("The current user TEMP location is unavailable.");

    private static string Canonical(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
