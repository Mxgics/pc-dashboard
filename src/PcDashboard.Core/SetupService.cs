using System.Text.Json;

namespace PcDashboard.Core;

/// <summary>Creates one immutable profile plan and executes its packages sequentially.</summary>
public sealed class SetupService(IPackageSetupProvider packages, ISetupProfileStore profiles,
    IActionStore actions, OperationCoordinator operations, TimeProvider timeProvider)
{
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(5);

    public Task<IReadOnlyList<SetupProfile>> ProfilesAsync(CancellationToken ct = default)
        => profiles.SetupProfilesAsync(ct);

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query,
        CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        if (query.Length > 100) throw new ArgumentException("Search text must be at most 100 characters.");
        return await packages.SearchAsync(query, ct);
    }

    public Task SaveProfileAsync(SetupProfile profile, CancellationToken ct = default)
        => profiles.SaveSetupProfileAsync(profile, ct);

    public Task DeleteProfileAsync(Guid id, CancellationToken ct = default)
        => profiles.DeleteSetupProfileAsync(id, ct);

    public async Task<SetupPlan> PrepareAsync(SetupProfile profile,
        CancellationToken ct = default)
    {
        if (operations.IsBusy)
            throw new InvalidOperationException("Another refresh or action is already running.");
        if (profile.Items.Count == 0)
            throw new InvalidOperationException("Add at least one package before reviewing this profile.");

        var resolved = new List<SetupPlanItem>(profile.Items.Count);
        foreach (var item in profile.Items)
        {
            ct.ThrowIfCancellationRequested();
            resolved.Add(await packages.ResolveAsync(item, ct));
        }

        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var expires = now + ApprovalLifetime;
        return new(id, profile.Id, profile.Name, now, expires, resolved,
            Digest(id, profile.Id, profile.Name, now, expires, resolved));
    }

    public async Task<ActionResult> ExecuteApprovedAsync(SetupPlan plan, SetupApproval approval,
        IProgress<ActionProgress>? progress = null, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var rejection = ValidateApproval(plan, approval, now);
        if (rejection is not null) return Rejected(plan, rejection, now);

        IAsyncDisposable? lease;
        try { lease = await operations.TryBeginAsync(ActionKind.Setup, ct); }
        catch (OperationCanceledException)
        {
            return new(ActionStatus.Cancelled, plan.ProfileName,
                "Setup cancelled before the first package.", CompletedAt: timeProvider.GetUtcNow());
        }
        if (lease is null)
            return Rejected(plan, "Another refresh or action is already running.",
                timeProvider.GetUtcNow());

        var actionId = Guid.NewGuid();
        var started = false;
        ActionResult result = new(ActionStatus.Succeeded, plan.ProfileName,
            "Every planned package is current or completed successfully.",
            CompletedAt: timeProvider.GetUtcNow());
        await using (lease)
        {
            try
            {
                await actions.StartActionAsync(new(actionId, ActionKind.Setup, plan.ProfileName,
                    ActionStatus.Running, timeProvider.GetUtcNow(), DetailsJson: JsonSerializer.Serialize(
                        new { plan.ProfileId, PackageCount = plan.Items.Count })), ct);
                started = true;

                foreach (var approved in plan.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new(ActionProgressStage.Queued,
                        $"Revalidating {approved.Name}.", CanCancel: true));
                    var live = await packages.ResolveAsync(approved.Item, ct);
                    if (!PlanDigests.Equals(PlanDigests.Compute(approved),
                            PlanDigests.Compute(live)))
                    {
                        result = Rejected(plan,
                            $"{approved.Name} changed after approval; review the setup plan again.",
                            timeProvider.GetUtcNow());
                        break;
                    }
                    if (approved.Disposition == SetupDisposition.Current) continue;
                    if (approved.Disposition == SetupDisposition.Blocked)
                    {
                        result = new(ActionStatus.Failed, plan.ProfileName,
                            approved.Reason ?? $"{approved.Name} cannot be installed.",
                            CompletedAt: timeProvider.GetUtcNow());
                        break;
                    }

                    result = await packages.InstallAsync(approved,
                        approval.AgreementsAccepted, progress, ct);
                    if (result.Status != ActionStatus.Succeeded) break;
                    result = new(ActionStatus.Succeeded, plan.ProfileName,
                        $"Completed {approved.Name}; continuing the approved profile.",
                        CompletedAt: timeProvider.GetUtcNow());
                }
            }
            catch (OperationCanceledException)
            {
                result = new(ActionStatus.Cancelled, plan.ProfileName,
                    "Setup stopped before the next package began.",
                    CompletedAt: timeProvider.GetUtcNow());
            }
            catch (Exception ex)
            {
                result = new(ActionStatus.Failed, plan.ProfileName, ex.Message,
                    CompletedAt: timeProvider.GetUtcNow());
            }
        }

        progress?.Report(new(ActionProgressStage.Completed, result.Message, CanCancel: false));
        if (started) await actions.CompleteActionAsync(actionId, result, CancellationToken.None);
        return result;
    }

    private static string? ValidateApproval(SetupPlan plan, SetupApproval approval,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(approval.ApprovalToken))
            return "Explicit approval is required.";
        if (approval.PlanId != plan.PlanId || !PlanDigests.Equals(approval.Digest, plan.Digest))
            return "The approval does not match this setup plan.";
        if (!PlanDigests.Equals(plan.Digest, Digest(plan.PlanId, plan.ProfileId,
                plan.ProfileName, plan.CreatedAt, plan.ExpiresAt, plan.Items)))
            return "The setup plan was altered.";
        if (plan.ExpiresAt - plan.CreatedAt != ApprovalLifetime || plan.CreatedAt > now
            || approval.ApprovedAt < plan.CreatedAt || approval.ApprovedAt > plan.ExpiresAt
            || approval.ApprovedAt > now || now > plan.ExpiresAt)
            return "Approval expired; review the setup plan again.";
        if (plan.Items.SelectMany(item => item.Agreements).Any()
            && !approval.AgreementsAccepted)
            return "Required package agreements were not accepted.";
        return null;
    }

    private static string Digest(Guid id, Guid profileId, string name, DateTimeOffset created,
        DateTimeOffset expires, IReadOnlyList<SetupPlanItem> items)
        => PlanDigests.Compute(new { id, profileId, name, created, expires, items });

    private static ActionResult Rejected(SetupPlan plan, string message,
        DateTimeOffset completedAt)
        => new(ActionStatus.Rejected, plan.ProfileName, message, CompletedAt: completedAt);
}
