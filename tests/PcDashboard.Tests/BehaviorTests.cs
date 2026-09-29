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
        var resolution = new UpdateResolution("a", "Example", "Vendor.Example", "winget", "1.0", "2.0",
            "User", "Exe", "ElevationProhibited", []);
        var resolver = new MutableResolver(resolution);
        var executor = new RecordingExecutor();
        var actionStore = new RecordingActionStore();
        var clock = new ManualTimeProvider(now);
        var updateService = new UpdateService(resolver, executor, actionStore, new OperationCoordinator(), clock);
        var plan = await updateService.PrepareAsync(evidenced);
        var approved = new UpdateApproval(plan.PlanId, plan.Digest, now, true, "user-approved");
        var action = await updateService.ExecuteApprovedAsync(plan, approved, evidenced);
        Check(action.Status == ActionStatus.Succeeded && executor.Calls == 1 && resolver.Calls == 2,
            "Explicit immutable update plan is resolved again before execution");
        Check(actionStore.Records.Single().Status == ActionStatus.Succeeded,
            "Terminal update result is persisted");
        var tampered = await updateService.ExecuteApprovedAsync(plan with
            { Resolution = plan.Resolution with { TargetVersion = "3.0" } }, approved, evidenced);
        Check(tampered.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Altered update plan is rejected before execution");
        var unapproved = await updateService.ExecuteApprovedAsync(plan, approved with { ApprovalToken = "" }, evidenced);
        Check(unapproved.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Missing approval token is rejected before execution");
        clock.SetUtcNow(now.AddMinutes(6));
        var expired = await updateService.ExecuteApprovedAsync(plan, approved, evidenced);
        Check(expired.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Five-minute plan expiry is deterministic");
        clock.SetUtcNow(now);
        var changedInstall = await updateService.ExecuteApprovedAsync(plan, approved, evidenced with { Version = "1.1" });
        Check(changedInstall.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Changed installed version requires fresh approval");
        resolver.Current = resolution with { Agreements = [new("Terms", "Review terms", "https://example.invalid/terms")] };
        var agreementPlan = await updateService.PrepareAsync(evidenced);
        var missingAgreement = await updateService.ExecuteApprovedAsync(agreementPlan,
            new(agreementPlan.PlanId, agreementPlan.Digest, now, false, "user-approved"), evidenced);
        Check(missingAgreement.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Required package agreements need separate acceptance");
        resolver.Current = resolution;
        var driftPlan = await updateService.PrepareAsync(evidenced);
        resolver.Current = resolution with { InstallerType = "Msi" };
        var drift = await updateService.ExecuteApprovedAsync(driftPlan,
            new(driftPlan.PlanId, driftPlan.Digest, now, true, "user-approved"), evidenced);
        Check(drift.Status == ActionStatus.Rejected && executor.Calls == 1,
            "Execution-time resolution drift is rejected");
        resolver.Current = resolution;
        var blockingExecutor = new BlockingExecutor();
        var serializedUpdates = new UpdateService(resolver, blockingExecutor, new RecordingActionStore(),
            new OperationCoordinator(), clock);
        var blockingPlan = await serializedUpdates.PrepareAsync(evidenced);
        var blockingApproval = new UpdateApproval(blockingPlan.PlanId, blockingPlan.Digest, now, true, "user-approved");
        var firstUpdate = serializedUpdates.ExecuteApprovedAsync(blockingPlan, blockingApproval, evidenced);
        await blockingExecutor.Started.Task;
        var overlap = await serializedUpdates.ExecuteApprovedAsync(blockingPlan, blockingApproval, evidenced);
        Check(overlap.Status == ActionStatus.Rejected && blockingExecutor.Calls == 1,
            "Overlapping update action is rejected");
        blockingExecutor.Complete.SetResult();
        Check((await firstUpdate).Status == ActionStatus.Succeeded,
            "Single update action completes after overlap rejection");
        var cancelledBeforeStart = new CancellationTokenSource(); cancelledBeforeStart.Cancel();
        Check((await updateService.ExecuteApprovedAsync(plan, approved, evidenced,
            ct: cancelledBeforeStart.Token)).Status == ActionStatus.Cancelled,
            "Cancellation before the action gate reports cancelled without executing");
        var sharedGate = new OperationCoordinator();
        var refreshLease = await sharedGate.TryBeginRefreshAsync();
        Check(refreshLease is not null && sharedGate.IsRefreshing
            && await sharedGate.TryBeginAsync(ActionKind.Update) is null,
            "A refresh excludes update mutations");
        await refreshLease!.DisposeAsync();
        var mutationLease = await sharedGate.TryBeginAsync(ActionKind.Update);
        Check(mutationLease is not null && sharedGate.ActiveMutation == ActionKind.Update
            && await sharedGate.TryBeginRefreshAsync() is null,
            "An update mutation excludes refreshes");
        await mutationLease!.DisposeAsync();
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
            using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "test.db")}"))
            {
                db.Open(); using var query = db.CreateCommand(); query.CommandText = "PRAGMA user_version";
                Check(Convert.ToInt32(query.ExecuteScalar()) == SqliteDashboardStore.SchemaVersion,
                    "V2 action history upgrades the database to schema 2");
            }
            var unfinishedId = Guid.NewGuid();
            await store.StartActionAsync(new(unfinishedId, ActionKind.Update, "Interrupted example",
                ActionStatus.Running, now));
            var recoveredStore = new SqliteDashboardStore(Path.Combine(directory, "test.db"));
            Check((await recoveredStore.RecentActionsAsync()).Single(r => r.Id == unfinishedId).Status
                == ActionStatus.Interrupted, "Startup marks unfinished actions interrupted");
            for (var i = 0; i < 105; i++)
            {
                var id = Guid.NewGuid();
                await recoveredStore.StartActionAsync(new(id, ActionKind.Update, $"Action {i}",
                    ActionStatus.Running, now.AddSeconds(i + 1)));
                await recoveredStore.CompleteActionAsync(id,
                    new(ActionStatus.Succeeded, $"Action {i}", "Completed", CompletedAt: now.AddSeconds(i + 1)));
            }
            Check((await recoveredStore.RecentActionsAsync()).Count == 100,
                "Action history retention is bounded to 100 records");
            var newerPath = Path.Combine(directory, "newer.db");
            using (var newer = new SqliteConnection($"Data Source={newerPath}"))
            {
                newer.Open(); using var command = newer.CreateCommand();
                command.CommandText = $"PRAGMA user_version={SqliteDashboardStore.SchemaVersion + 1}";
                command.ExecuteNonQuery();
            }
            try { _ = new SqliteDashboardStore(newerPath); throw new Exception("Opened newer schema"); }
            catch (InvalidOperationException) { Check(true, "Newer database schema is rejected without reset"); }
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
            var service = new DashboardService(store, [failure], updater, new OperationCoordinator()); await service.LoadAsync(); await service.RefreshAsync(forceUpdates: true);
            Check(service.Snapshot.Apps.Single().Update == oldEvidence && service.Snapshot.Health.All(h => !h.Success), "Provider failures retain previous apps and update evidence with failed health");
            Check((await store.AssignmentsAsync())[app.Id] == "Projects", "Snapshot replacement does not erase assignments");
            var replacement = new FakeInventory("registry", _ => Task.FromResult(new InventoryResult("registry", [app, app with { Id = "b", Version = "2.0" }], new("registry", now, true))));
            service = new DashboardService(store, [replacement], updater, new OperationCoordinator()); await service.LoadAsync(); await service.RefreshAsync(forceUpdates: true);
            Check(service.Snapshot.Apps.Count == 2, "Duplicate display names and side-by-side versions retain distinct identities");
            Check(service.Snapshot.Apps.Single(a => a.Id == "b").Update?.Status == UpdateStatus.Failed, "New app exposes failed update detection");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var blocking = new FakeInventory("registry", async ct => { Interlocked.Increment(ref calls); entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new Exception(); });
            var sharedOperations = new OperationCoordinator();
            service = new DashboardService(store, [blocking], updater, sharedOperations); await service.LoadAsync();
            var refresh = service.RefreshAsync(); await entered.Task; await service.RefreshAsync();
            Check(calls == 1, "Overlapping refresh is rejected"); service.Cancel(); await refresh;
            Check(!service.IsRefreshing && service.Snapshot.Apps.Count == 2 && service.Error!.Contains("cancelled"), "Cancellation releases gate and retains snapshot");
            var healthy = new FakeInventory("registry", _ => Task.FromResult(new InventoryResult("registry", [], new("registry", now, true))));
            service = new DashboardService(store, [healthy], updater, new OperationCoordinator()); await service.LoadAsync(); await service.RefreshAsync();
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
    private sealed class MutableResolver(UpdateResolution current) : IUpdateResolver
    {
        public int Calls { get; private set; }
        public UpdateResolution Current { get; set; } = current;
        public Task<UpdateResolution> ResolveAsync(InstalledApp app, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(Current);
        }
    }
    private sealed class RecordingExecutor : IUpdateExecutor
    {
        public int Calls { get; private set; }
        public Task<ActionResult> ExecuteAsync(UpdatePlan plan, IProgress<ActionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ActionResult(ActionStatus.Succeeded, plan.Resolution.AppName,
                "Executed", CompletedAt: DateTimeOffset.UtcNow));
        }
    }
    private sealed class BlockingExecutor : IUpdateExecutor
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ActionResult> ExecuteAsync(UpdatePlan plan, IProgress<ActionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            Started.SetResult();
            await Complete.Task.WaitAsync(cancellationToken);
            return new(ActionStatus.Succeeded, plan.Resolution.AppName, "Executed",
                CompletedAt: DateTimeOffset.UtcNow);
        }
    }
    private sealed class RecordingActionStore : IActionStore
    {
        public List<ActionRecord> Records { get; } = [];
        public Task StartActionAsync(ActionRecord record, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Records.Add(record); return Task.CompletedTask;
        }
        public Task CompleteActionAsync(Guid id, ActionResult result, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var index = Records.FindIndex(record => record.Id == id);
            if (index < 0) throw new InvalidOperationException("Missing action record.");
            Records[index] = Records[index] with
            {
                Status = result.Status,
                CompletedAt = result.CompletedAt,
                Summary = result.Message,
                ErrorCode = result.InstallerErrorCode
            };
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ActionRecord>> RecentActionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ActionRecord>>(Records.ToArray());
        public Task MarkInterruptedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void SetUtcNow(DateTimeOffset value) => current = value;
    }
}
