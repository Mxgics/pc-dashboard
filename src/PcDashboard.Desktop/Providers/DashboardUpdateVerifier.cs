using PcDashboard.Core;

namespace PcDashboard.Desktop.Providers;

public sealed class DashboardUpdateVerifier(DashboardService dashboard) : IUpdateVerifier
{
    public async Task<UpdateVerification> RefreshAndVerifyAsync(UpdatePlan plan,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await dashboard.RefreshAsync(forceUpdates: true, ct: ct);
        var app = dashboard.Snapshot.Apps.SingleOrDefault(candidate
            => candidate.Id == plan.Resolution.AppId);
        var inventoryHealth = dashboard.Snapshot.Health.SingleOrDefault(health
            => health.Source == app?.Source);
        var updateHealth = dashboard.Snapshot.Health.SingleOrDefault(health
            => health.Source == "WinGet");
        var fresh = dashboard.Error is null
            && inventoryHealth is { Success: true } && inventoryHealth.AttemptedAt >= startedAt
            && updateHealth is { Success: true } && updateHealth.AttemptedAt >= startedAt;
        var observed = app is not null && string.Equals(app.Version,
            plan.Resolution.TargetVersion, StringComparison.Ordinal);

        return fresh && observed
            ? new(true, true, "Fresh inventory confirmed the target version.")
            : new(fresh, false, fresh
                ? "Fresh inventory did not confirm the target version."
                : "Fresh post-install verification was unavailable.");
    }
}
