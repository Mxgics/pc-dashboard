using System.Text.Json;

namespace PcDashboard.Core;

/// <summary>Freezes reviewed package evidence and revalidates it before invoking an executor.</summary>
public sealed class UpdateService(IUpdateResolver resolver, IUpdateExecutor executor,
    IActionStore actions, OperationCoordinator operations, TimeProvider timeProvider)
{
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(5);

    public async Task<UpdatePlan> PrepareAsync(InstalledApp app, CancellationToken ct = default)
    {
        var resolution = await resolver.ResolveAsync(app, ct);
        ValidateEvidence(app, resolution);
        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var expires = now + ApprovalLifetime;
        return new(id, now, expires, resolution, Digest(id, now, expires, resolution));
    }

    public async Task<ActionResult> ExecuteApprovedAsync(UpdatePlan plan, UpdateApproval approval,
        InstalledApp app, IProgress<ActionProgress>? progress = null, CancellationToken ct = default)
    {
        var rejection = ValidateApproval(plan, approval, app, timeProvider.GetUtcNow());
        if (rejection is not null) return Rejected(plan, rejection, timeProvider.GetUtcNow());

        IAsyncDisposable? lease;
        try { lease = await operations.TryBeginAsync(ActionKind.Update, ct); }
        catch (OperationCanceledException)
        {
            return new(ActionStatus.Cancelled, plan.Resolution.AppName,
                "Update cancelled before installation.", CompletedAt: timeProvider.GetUtcNow());
        }
        if (lease is null)
            return Rejected(plan, "Another action or refresh is already running.", timeProvider.GetUtcNow());

        var actionId = Guid.NewGuid();
        var started = false;
        ActionResult result;
        await using (lease)
        {
            try
            {
                await actions.StartActionAsync(new(actionId, ActionKind.Update, plan.Resolution.AppName,
                    ActionStatus.Running, timeProvider.GetUtcNow(), DetailsJson: JsonSerializer.Serialize(new
                    {
                        plan.Resolution.PackageId,
                        plan.Resolution.Source,
                        plan.Resolution.InstalledVersion,
                        plan.Resolution.TargetVersion,
                        plan.Resolution.Scope,
                        plan.Resolution.InstallerType,
                        plan.Resolution.ElevationRequirement
                    })), ct);
                started = true;

                var live = await resolver.ResolveAsync(app, ct);
                if (!PlanDigests.Equals(PlanDigests.Compute(plan.Resolution), PlanDigests.Compute(live)))
                    result = Rejected(plan, "Package evidence changed after approval; review the update again.", timeProvider.GetUtcNow());
                else
                    result = await executor.ExecuteAsync(plan, progress, ct);
            }
            catch (OperationCanceledException)
            {
                result = new(ActionStatus.Cancelled, plan.Resolution.AppName,
                    "Update cancelled before installation.", CompletedAt: timeProvider.GetUtcNow());
            }
            catch (Exception ex)
            {
                result = new(ActionStatus.Failed, plan.Resolution.AppName, ex.Message,
                    CompletedAt: timeProvider.GetUtcNow());
            }
        }

        progress?.Report(new(ActionProgressStage.Completed, result.Message, CanCancel: false));
        if (started) await actions.CompleteActionAsync(actionId, result, CancellationToken.None);
        return result;
    }

    private static string? ValidateApproval(UpdatePlan plan, UpdateApproval approval,
        InstalledApp app, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(approval.ApprovalToken)) return "Explicit approval is required.";
        if (approval.PlanId != plan.PlanId || !PlanDigests.Equals(approval.Digest, plan.Digest))
            return "The approval does not match this update plan.";
        if (!PlanDigests.Equals(plan.Digest, Digest(plan.PlanId, plan.CreatedAt, plan.ExpiresAt, plan.Resolution)))
            return "The update plan was altered.";
        if (plan.ExpiresAt - plan.CreatedAt != ApprovalLifetime || plan.CreatedAt > now
            || approval.ApprovedAt < plan.CreatedAt || approval.ApprovedAt > plan.ExpiresAt
            || approval.ApprovedAt > now || now > plan.ExpiresAt)
            return "Approval expired; review the update again.";
        if (plan.Resolution.Agreements.Count > 0 && !approval.AgreementsAccepted)
            return "Required package agreements were not accepted.";
        try { ValidateEvidence(app, plan.Resolution); }
        catch (InvalidOperationException ex) { return ex.Message; }
        return null;
    }

    private static void ValidateEvidence(InstalledApp app, UpdateResolution resolution)
    {
        if (app.Update is not { Status: UpdateStatus.Available } evidence)
            throw new InvalidOperationException("Fresh source-reported update evidence is required.");
        if (app.Id != resolution.AppId
            || !string.Equals(app.Version, resolution.InstalledVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The installed application changed; refresh before updating.");
        if (!string.Equals(evidence.PackageId, resolution.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.Source, resolution.Source, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.AvailableVersion, resolution.TargetVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The resolved package no longer matches update evidence.");
    }

    private static string Digest(Guid id, DateTimeOffset created, DateTimeOffset expires,
        UpdateResolution resolution) => PlanDigests.Compute(new { id, created, expires, resolution });

    private static ActionResult Rejected(UpdatePlan plan, string message, DateTimeOffset completedAt)
        => new(ActionStatus.Rejected, plan.Resolution.AppName, message, CompletedAt: completedAt);
}
