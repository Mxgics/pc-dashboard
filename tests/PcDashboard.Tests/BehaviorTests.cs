using PcDashboard.Core;
using Microsoft.Data.Sqlite;

internal static class BehaviorTests
{
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {++count}: {name}"); }
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new InstalledApp("a", "Example", "1.0", "Publisher", "registry", "{product}", "User", "x64", now);
        var candidate = new PackageCandidate("Vendor.Example", "winget", "1.0", "2.0", true, ["{product}"], [], "User");
        UpdateEvidence Match(InstalledApp value, params PackageCandidate[] candidates) => UpdateMatching.Match(value, candidates, now);
        Check(Match(app, candidate).Status == UpdateStatus.Available, "Exact identity and consistent installed evidence report source update");
        Check(Match(app with { Version = "1" }, candidate).Status == UpdateStatus.Unknown, "Mismatched version strings never establish an update");
        Check(Match(app with { Version = null }, candidate).Status == UpdateStatus.Unknown, "Missing installed version stays unknown");
        Check(Match(app, candidate with { InstalledVersion = "Unknown" }).Status == UpdateStatus.Unknown, "WinGet Unknown version stays unknown");
        Check(Match(app, candidate, candidate with { Id = "Other" }).Status == UpdateStatus.Unknown, "Ambiguous identity stays unknown");
        Check(Match(app, candidate with { ProductCodes = ["different"] }).Status == UpdateStatus.Unsupported, "Same name is insufficient for matching");
        Check(Match(app, candidate with { Scope = "Machine" }).Status == UpdateStatus.Unsupported, "Different install scopes are not conflated");
        Check(Match(app with { Scope = "Machine" }, candidate with { Scope = "System" }).Status == UpdateStatus.Available, "WinGet System scope maps to Windows machine scope");
        Check(Match(app, candidate with { UpdateAvailable = false }).Status == UpdateStatus.NoneReported, "Source false is none reported, not inferred latest");
        Check(Match(app, candidate).Pin == "Not exposed", "Unavailable pin metadata is explicit");
        Check(Match(app with { Source = "Windows packages", NativeId = "family" }, candidate with { FamilyNames = ["family"] }).Status == UpdateStatus.Available, "Packaged apps correlate on family identity");
        var evidenced = app with { Update = new UpdateEvidence(UpdateStatus.Available, "winget", "Vendor.Example", "2.0", now) };
        var executor = new RecordingExecutor();
        var updateService = new UpdateService(executor);
        var approved = new UpdateRequest("a", "Vendor.Example", "winget", "1.0", "2.0", now, "user-approved");
        var action = await updateService.ExecuteApprovedAsync(approved, evidenced);
        Check(action.Status == UpdateActionStatus.Succeeded && executor.Calls == 1, "Explicit update approval executes when evidence is exact");
        var stale = await updateService.ExecuteApprovedAsync(approved with { TargetVersion = "3.0" }, evidenced);
        Check(stale.Status == UpdateActionStatus.Rejected && executor.Calls == 1, "Stale target version is rejected before execution");
        var unapproved = await updateService.ExecuteApprovedAsync(approved with { ApprovalToken = "" }, evidenced);
        Check(unapproved.Status == UpdateActionStatus.Rejected && executor.Calls == 1, "Missing approval token is rejected before execution");
        var expired = await updateService.ExecuteApprovedAsync(approved with { ApprovedAt = now.AddMinutes(-6) }, evidenced);
        Check(expired.Status == UpdateActionStatus.Rejected && executor.Calls == 1, "Expired approval is rejected before execution");
        var changedInstall = await updateService.ExecuteApprovedAsync(approved, evidenced with { Version = "1.1" });
        Check(changedInstall.Status == UpdateActionStatus.Rejected && executor.Calls == 1, "Changed installed version requires fresh approval");
        var blockingExecutor = new BlockingExecutor();
        var serializedUpdates = new UpdateService(blockingExecutor);
        var firstUpdate = serializedUpdates.ExecuteApprovedAsync(approved, evidenced);
        await blockingExecutor.Started.Task;
        var overlap = await serializedUpdates.ExecuteApprovedAsync(approved, evidenced);
        Check(overlap.Status == UpdateActionStatus.Rejected && blockingExecutor.Calls == 1, "Overlapping update action is rejected");
        blockingExecutor.Complete.SetResult();
        Check((await firstUpdate).Status == UpdateActionStatus.Succeeded, "Single update action completes after overlap rejection");
        var directory = Path.Combine(Path.GetTempPath(), "PcDashboard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SqliteDashboardStore(Path.Combine(directory, "test.db"));
            var diagnostics = new SqliteDiagnostics(Path.Combine(directory, "test.db"));
            Check(diagnostics.TryWrite("Synthetic private diagnostic"), "Diagnostics persist in SQLite");
            for (var i = 0; i < 260; i++) diagnostics.TryWrite(new string('x', 5000));
            using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")}"))
            {
                db.Open();
                using var query = db.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM diagnostics";
                Check(Convert.ToInt32(query.ExecuteScalar()) == SqliteDiagnostics.MaxEntries, "Diagnostic retention is bounded");
                query.CommandText = "SELECT MAX(length(message)) FROM diagnostics";
                Check(Convert.ToInt32(query.ExecuteScalar()) == SqliteDiagnostics.MaxMessageLength, "Diagnostic message size is bounded");
            }
            var legacy = Path.Combine(directory, "legacy.log");
            File.WriteAllText(legacy, "Synthetic legacy evidence");
            diagnostics.ImportLegacyFile(legacy);
            Check(!File.Exists(legacy), "Legacy file removed after database import");
            File.WriteAllText(legacy, "Synthetic legacy evidence");
            diagnostics.ImportLegacyFile(legacy);
            using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")}"))
            {
                db.Open();
                using var query = db.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM legacy_diagnostics WHERE content='Synthetic legacy evidence'";
                Check(Convert.ToInt32(query.ExecuteScalar()) == 1, "Legacy import preserves content and deduplicates retries");
                using var transaction = db.BeginTransaction();
                Check(!diagnostics.TryWrite("Blocked diagnostic"), "Locked database reports diagnostic failure without fallback");
                File.WriteAllText(legacy, "Preserve on failed import");
                try { diagnostics.ImportLegacyFile(legacy); throw new Exception("Import unexpectedly succeeded"); }
                catch (SqliteException) { Check(File.ReadAllText(legacy) == "Preserve on failed import", "Failed legacy import preserves original file"); }
                transaction.Rollback();
            }
            File.Delete(legacy);
            Check(Directory.GetFiles(directory).All(p => Path.GetFileName(p).StartsWith("test.db", StringComparison.Ordinal)), "Diagnostics create no log or probe files");
            await store.AddCategoryAsync("Work"); await store.AssignAsync(app.Id, "Work");
            await store.RenameCategoryAsync("Work", "Projects");
            Check((await store.AssignmentsAsync())[app.Id] == "Projects", "Category rename cascades to assignments");
            await store.RenameCategoryAsync("Coding", "Development");
            var reopened = new SqliteDashboardStore(Path.Combine(directory, "test.db"));
            Check((await reopened.CategoryAliasesAsync())["Coding"] == "Development" && !(await reopened.CategoriesAsync()).Contains("Coding"), "Built-in rename persists and redirects suggestions after restart");
            var oldEvidence = new UpdateEvidence(UpdateStatus.Available, "winget", "Vendor.Example", "2.0", now);
            var previous = new Snapshot([app with { Update = oldEvidence }], []);
            await store.SaveAsync(previous);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await store.SaveAsync(Snapshot.Empty, cancelled.Token); } catch (OperationCanceledException) { }
            Check((await store.LoadAsync()).Apps.Count == 1, "Cancelled transaction retains last snapshot");
            var failure = new FakeInventory("registry", _ => throw new IOException("source offline"));
            var updater = new FakeUpdates((_, _) => throw new IOException("update source offline"));
            var service = new DashboardService(store, [failure], updater); await service.LoadAsync(); await service.RefreshAsync(forceUpdates: true);
            Check(service.Snapshot.Apps.Single().Update == oldEvidence && service.Snapshot.Health.All(h => !h.Success), "Provider failures retain previous apps and update evidence with failed health");
            Check((await store.AssignmentsAsync())[app.Id] == "Projects", "Snapshot replacement does not erase assignments");
            var replacement = new FakeInventory("registry", _ => Task.FromResult(new InventoryResult("registry", [app, app with { Id = "b", Version = "2.0" }], new("registry", now, true))));
            service = new DashboardService(store, [replacement], updater); await service.LoadAsync(); await service.RefreshAsync(forceUpdates: true);
            Check(service.Snapshot.Apps.Count == 2, "Duplicate display names and side-by-side versions retain distinct identities");
            Check(service.Snapshot.Apps.Single(a => a.Id == "b").Update?.Status == UpdateStatus.Failed, "New app exposes failed update detection");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var blocking = new FakeInventory("registry", async ct => { Interlocked.Increment(ref calls); entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new Exception(); });
            service = new DashboardService(store, [blocking], updater); await service.LoadAsync();
            var refresh = service.RefreshAsync(); await entered.Task; await service.RefreshAsync();
            Check(calls == 1, "Overlapping refresh is rejected"); service.Cancel(); await refresh;
            Check(!service.IsRefreshing && service.Snapshot.Apps.Count == 2 && service.Error!.Contains("cancelled"), "Cancellation releases gate and retains snapshot");
            var healthy = new FakeInventory("registry", _ => Task.FromResult(new InventoryResult("registry", [], new("registry", now, true))));
            service = new DashboardService(store, [healthy], updater); await service.LoadAsync(); await service.RefreshAsync();
            Check(service.Snapshot.Apps.Count == 0, "Successful empty inventory removes obsolete entries");
            Check((await store.AssignmentsAsync()).ContainsKey(app.Id), "Removed-app overrides remain available if the identity returns");
            Check(Categories.Suggest(app with { Name = "Visual Studio Code" }) == "Coding", "Deterministic bundled category suggestion");
            Check(Categories.Suggest(app with { Name = "Battle.net" }) == "Gaming", "Battle.net does not collide with .NET category rule");
            Check(Categories.Suggest(app with { Name = "Logitech Options" }) != "Coding", "Git category rule does not match inside unrelated names");
            try { await store.AddCategoryAsync(" "); throw new Exception("accepted empty category"); } catch (ArgumentException) { Check(true, "Empty category is rejected"); }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
        Console.WriteLine($"All {count} behavioral checks passed.");
    }
    private sealed class FakeInventory(string source, Func<CancellationToken, Task<InventoryResult>> run) : IInventoryProvider
    { public string Source => source; public Task<InventoryResult> CollectAsync(CancellationToken ct) => run(ct); }
    private sealed class FakeUpdates(Func<IReadOnlyList<InstalledApp>, CancellationToken, Task<UpdateResult>> run) : IUpdateProvider
    { public Task<UpdateResult> CheckAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct) => run(apps, ct); }
    private sealed class RecordingExecutor : IUpdateExecutor
    {
        public int Calls { get; private set; }
        public Task<UpdateActionResult> ExecuteAsync(UpdateRequest request, InstalledApp app, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new UpdateActionResult(UpdateActionStatus.Succeeded, request.AppId, request.PackageId, "Executed", CompletedAt: DateTimeOffset.UtcNow));
        }
    }
    private sealed class BlockingExecutor : IUpdateExecutor
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<UpdateActionResult> ExecuteAsync(UpdateRequest request, InstalledApp app, CancellationToken cancellationToken)
        {
            Calls++;
            Started.SetResult();
            await Complete.Task.WaitAsync(cancellationToken);
            return new(UpdateActionStatus.Succeeded, request.AppId, request.PackageId, "Executed", CompletedAt: DateTimeOffset.UtcNow);
        }
    }
}
