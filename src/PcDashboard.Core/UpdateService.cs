namespace PcDashboard.Core;

/// <summary>Validates an explicitly approved update against the latest evidence before execution.</summary>
public sealed class UpdateService(IUpdateExecutor executor)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<UpdateActionResult> ExecuteApprovedAsync(UpdateRequest request, InstalledApp app, CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
            return new(UpdateActionStatus.Rejected, request.AppId, request.PackageId, "Another update is already running.");
        try
        {
            var rejection = Validate(request, app);
            if (rejection is not null) return new(UpdateActionStatus.Rejected, request.AppId, request.PackageId, rejection);
            return await executor.ExecuteAsync(request, app, cancellationToken);
        }
        catch (OperationCanceledException) { return new(UpdateActionStatus.Cancelled, request.AppId, request.PackageId, "Update cancelled before completion.", CompletedAt: DateTimeOffset.UtcNow); }
        catch (Exception ex) { return new(UpdateActionStatus.Failed, request.AppId, request.PackageId, ex.Message, CompletedAt: DateTimeOffset.UtcNow); }
        finally { gate.Release(); }
    }

    private static string? Validate(UpdateRequest request, InstalledApp app)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.ApprovalToken)) return "Explicit approval is required.";
        if (request.AppId != app.Id) return "The approved application no longer matches the selected application.";
        if (app.Update is not { Status: UpdateStatus.Available } evidence) return "A source reported update evidence is required.";
        if (!string.Equals(request.PackageId, evidence.PackageId, StringComparison.OrdinalIgnoreCase)) return "The package identity no longer matches update evidence.";
        if (!string.Equals(request.Source, evidence.Source, StringComparison.OrdinalIgnoreCase)) return "The package source no longer matches update evidence.";
        if (!string.Equals(request.TargetVersion, evidence.AvailableVersion, StringComparison.Ordinal)) return "The target version no longer matches update evidence.";
        if (!string.Equals(request.ExpectedInstalledVersion, app.Version, StringComparison.Ordinal)) return "The installed version changed; refresh before updating.";
        if (request.ApprovedAt < now.Subtract(TimeSpan.FromMinutes(5))) return "Approval expired; review the update again.";
        if (request.ApprovedAt > now.AddMinutes(5)) return "Approval timestamp is invalid.";
        return null;
    }
}
