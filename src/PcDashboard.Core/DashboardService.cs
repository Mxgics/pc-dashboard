namespace PcDashboard.Core;

public sealed class DashboardService(IDashboardStore store, IEnumerable<IInventoryProvider> providers, IUpdateProvider updates)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? active;
    public Snapshot Snapshot { get; private set; } = Snapshot.Empty;
    public bool IsRefreshing { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    public async Task LoadAsync() { Snapshot = await store.LoadAsync(); Changed?.Invoke(); }
    public void Cancel() => active?.Cancel();
    public async Task RefreshAsync(bool inventory = true, bool forceUpdates = false, CancellationToken ct = default, bool checkUpdates = true)
    {
        if (!await gate.WaitAsync(0, ct)) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        active = cancellation; IsRefreshing = true; Error = null; Changed?.Invoke();
        try
        {
            var token = cancellation.Token;
            var apps = Snapshot.Apps.ToList(); var health = Snapshot.Health.ToDictionary(h => h.Source);
            if (inventory)
            {
                foreach (var provider in providers)
                {
                    InventoryResult result;
                    try { result = await provider.CollectAsync(token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result = new(provider.Source, [], new(provider.Source, DateTimeOffset.UtcNow, false, ex.Message)); }
                    health[result.Source] = result.Health;
                    if (!result.Health.Success) continue; // A failed source must not erase its cached inventory.
                    var prior = apps.Where(a => a.Source == result.Source).ToDictionary(a => a.Id);
                    apps.RemoveAll(a => a.Source == result.Source);
                    apps.AddRange(result.Apps.Select(a => prior.TryGetValue(a.Id, out var old) && old.Version == a.Version
                        ? a with { Update = old.Update } : a));
                }
            }
            var now = DateTimeOffset.UtcNow;
            var lastAttempt = health.GetValueOrDefault("WinGet")?.AttemptedAt;
            var due = apps.Any(a => a.Update?.CheckedAt is not { } date || now - date >= TimeSpan.FromHours(24));
            // Back off failures for an hour; manual refresh always retries.
            if (forceUpdates || (checkUpdates && due && (lastAttempt is null || now - lastAttempt >= TimeSpan.FromHours(1))))
            {
                UpdateResult result;
                try { result = await updates.CheckAsync(apps, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result = new(new Dictionary<string, UpdateEvidence>(), new("WinGet", now, false, ex.Message)); }
                health["WinGet"] = result.Health;
                apps = apps.Select(a => result.Evidence.TryGetValue(a.Id, out var evidence)
                    ? a with { Update = evidence }
                    : a.Update is null && !result.Health.Success
                        ? a with { Update = new(UpdateStatus.Failed, "WinGet", Note: result.Health.Error) } : a).ToList();
            }
            token.ThrowIfCancellationRequested();
            var next = new Snapshot(apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray(), health.Values.ToArray());
            await store.SaveAsync(next, token); Snapshot = next;
        }
        catch (OperationCanceledException) { Error = "Refresh cancelled. The previous snapshot was retained."; }
        catch (Exception ex) { Error = $"Refresh failed; the previous snapshot was retained. {ex.Message}"; }
        finally { active = null; IsRefreshing = false; gate.Release(); Changed?.Invoke(); }
    }
}
