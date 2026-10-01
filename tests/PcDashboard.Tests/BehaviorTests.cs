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
        Check(UpdateExecutionResults.NativeTerminal("Example", true, false, "Ok", null, now).Status
            == ActionStatus.Succeeded, "Native success maps explicitly");
        var restart = UpdateExecutionResults.NativeTerminal("Example", true, true, "Ok", 3010, now);
        Check(restart.Status == ActionStatus.RestartRequired && restart.RestartRequired,
            "Successful restart-required result stays distinct");
        var installerFailure = UpdateExecutionResults.NativeTerminal("Example", false, true,
            "InstallError", 1603, now);
        Check(installerFailure.Status == ActionStatus.Failed && !installerFailure.RestartRequired
            && installerFailure.InstallerErrorCode == 1603,
            "Failed install is not mislabeled as completed restart-required");
        Check(UpdateExecutionResults.Cancellation("Example", false, now).Status
            == ActionStatus.Cancelled, "Pre-install cancellation is terminal cancelled");
        Check(UpdateExecutionResults.Cancellation("Example", true, now).Status
            == ActionStatus.Interrupted, "Post-install interruption reports uncertain state");
        var evidenced = app with { Update = new UpdateEvidence(UpdateStatus.Available, "winget", "Vendor.Example", "2.0", now) };
        var resolution = new UpdateResolution("a", "Example", "Vendor.Example", "winget", "1.0", "2.0",
            "User", "Exe", "ElevationProhibited", []);
        var resolver = new MutableResolver(resolution);
        var executor = new RecordingExecutor();
        var verifier = new RecordingVerifier();
        var actionStore = new RecordingActionStore();
        var clock = new ManualTimeProvider(now);
        var updateService = new UpdateService(resolver, executor, verifier, actionStore,
            new OperationCoordinator(), clock);
        var plan = await updateService.PrepareAsync(evidenced);
        var approved = new UpdateApproval(plan.PlanId, plan.Digest, now, true, "user-approved");
        var action = await updateService.ExecuteApprovedAsync(plan, approved, evidenced);
        Check(action.Status == ActionStatus.Succeeded && executor.Calls == 1 && resolver.Calls == 2
            && verifier.Calls == 1, "Explicit update is resolved again and freshly verified");
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
        var unavailableVerifier = new RecordingVerifier
        {
            Result = new(false, false, "Fresh verification was unavailable.")
        };
        var unverifiedService = new UpdateService(resolver, new RecordingExecutor(),
            unavailableVerifier, new RecordingActionStore(), new OperationCoordinator(), clock);
        var unverifiedPlan = await unverifiedService.PrepareAsync(evidenced);
        var unverified = await unverifiedService.ExecuteApprovedAsync(unverifiedPlan,
            new(unverifiedPlan.PlanId, unverifiedPlan.Digest, now, true, "user-approved"), evidenced);
        Check(unverified.Status == ActionStatus.SucceededUnverified && unavailableVerifier.Calls == 1,
            "Native success remains unverified when fresh inventory is unavailable");
        var failedExecutor = new RecordingExecutor
        {
            Result = new(ActionStatus.Failed, "Example", "Installer failed", 1603,
                CompletedAt: now)
        };
        var failureVerifier = new RecordingVerifier();
        var failureService = new UpdateService(resolver, failedExecutor, failureVerifier,
            new RecordingActionStore(), new OperationCoordinator(), clock);
        var failurePlan = await failureService.PrepareAsync(evidenced);
        var failed = await failureService.ExecuteApprovedAsync(failurePlan,
            new(failurePlan.PlanId, failurePlan.Digest, now, true, "user-approved"), evidenced);
        Check(failed.Status == ActionStatus.Failed && failed.InstallerErrorCode == 1603
            && failureVerifier.Calls == 1, "Installer failure is preserved after mandatory refresh");
        var blockingExecutor = new BlockingExecutor();
        var serializedUpdates = new UpdateService(resolver, blockingExecutor, new RecordingVerifier(),
            new RecordingActionStore(), new OperationCoordinator(), clock);
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

        var setupItem = new SetupProfileItem(Guid.NewGuid(), "Vendor.Tool", "winget", "User");
        var setupResolved = new SetupPlanItem(setupItem, "Tool", null, "2.0",
            SetupDisposition.Install, "Exe", "ElevationProhibited",
            [new("Terms", "Review these terms", "https://example.invalid/setup")]);
        var setupProvider = new RecordingSetupProvider(setupResolved);
        var setupProfiles = new RecordingProfileStore();
        var setupActions = new RecordingActionStore();
        var setupService = new SetupService(setupProvider, setupProfiles, setupActions,
            new OperationCoordinator(), clock);
        var setupProfile = new SetupProfile(Guid.NewGuid(), "Developer tools", [setupItem]);
        await setupService.SaveProfileAsync(setupProfile);
        Check((await setupService.ProfilesAsync()).Single() == setupProfile,
            "Setup profile CRUD preserves exact package, source, and scope");
        var setupPlan = await setupService.PrepareAsync(setupProfile);
        Check(setupPlan.Items.Single().Disposition == SetupDisposition.Install,
            "Setup preview classifies a missing package as Install");
        var currentItem = new SetupProfileItem(Guid.NewGuid(), "Vendor.Current", "winget", "User");
        var updateItem = new SetupProfileItem(Guid.NewGuid(), "Vendor.Update", "winget", "Machine");
        var blockedItem = new SetupProfileItem(Guid.NewGuid(), "Vendor.Blocked", "winget", "User");
        var classificationProvider = new RecordingSetupProvider(setupResolved);
        classificationProvider.Resolutions[currentItem.Id] = new(currentItem, "Current", "1.0", "1.0",
            SetupDisposition.Current, "Exe", "ElevationProhibited", []);
        classificationProvider.Resolutions[updateItem.Id] = new(updateItem, "Update", "1.0", "2.0",
            SetupDisposition.Update, "Msi", "ElevationRequired", []);
        classificationProvider.Resolutions[blockedItem.Id] = new(blockedItem, "Blocked", null, null,
            SetupDisposition.Blocked, "Unavailable", "Unknown", [], "Source unavailable");
        var classificationService = new SetupService(classificationProvider, setupProfiles,
            new RecordingActionStore(), new OperationCoordinator(), clock);
        var classificationPlan = await classificationService.PrepareAsync(new(Guid.NewGuid(),
            "Classification", [setupItem, currentItem, updateItem, blockedItem]));
        Check(classificationPlan.Items.Select(item => item.Disposition).SequenceEqual(
                [SetupDisposition.Install, SetupDisposition.Current,
                    SetupDisposition.Update, SetupDisposition.Blocked]),
            "Setup preview preserves Current, Install, Update, and Blocked classifications");
        var setupWithoutAgreement = await setupService.ExecuteApprovedAsync(setupPlan,
            new(setupPlan.PlanId, setupPlan.Digest, now, false, "approved"));
        Check(setupWithoutAgreement.Status == ActionStatus.Rejected && setupProvider.InstallCalls == 0,
            "Setup agreements require separate acceptance");
        var setupResult = await setupService.ExecuteApprovedAsync(setupPlan,
            new(setupPlan.PlanId, setupPlan.Digest, now, true, "approved"));
        Check(setupResult.Status == ActionStatus.Succeeded && setupProvider.ResolveCalls == 2
            && setupProvider.InstallCalls == 1,
            "Approved setup revalidates immediately before sequential execution");
        setupProvider.Current = setupResolved with { TargetVersion = "2.1" };
        var driftedSetup = await setupService.ExecuteApprovedAsync(setupPlan,
            new(setupPlan.PlanId, setupPlan.Digest, now, true, "approved"));
        Check(driftedSetup.Status == ActionStatus.Rejected && setupProvider.InstallCalls == 1,
            "Setup stops before execution when package evidence drifts");
        setupProvider.Current = setupResolved;
        var secondItem = new SetupProfileItem(Guid.NewGuid(), "Vendor.Second", "winget", "Machine");
        var restartProvider = new RecordingSetupProvider(setupResolved)
        {
            Result = new(ActionStatus.RestartRequired, "Tool", "Restart required",
                RestartRequired: true, CompletedAt: now)
        };
        restartProvider.Resolutions[secondItem.Id] = new(secondItem, "Second", null, "1.0",
            SetupDisposition.Install, "Msi", "ElevationRequired", []);
        var restartService = new SetupService(restartProvider, setupProfiles,
            new RecordingActionStore(), new OperationCoordinator(), clock);
        var restartPlan = await restartService.PrepareAsync(
            new(Guid.NewGuid(), "Restart profile", [setupItem, secondItem]));
        var restartResult = await restartService.ExecuteApprovedAsync(restartPlan,
            new(restartPlan.PlanId, restartPlan.Digest, now, true, "approved"));
        Check(restartResult.Status == ActionStatus.RestartRequired
            && restartProvider.InstallCalls == 1,
            "Setup stops at the first restart-required result");

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
                    "Setup profiles upgrade the database to schema 3");
            }
            var storedProfile = new SetupProfile(Guid.NewGuid(), "Workstation",
                [new(Guid.NewGuid(), "Vendor.Editor", "winget", "Machine")]);
            await store.SaveSetupProfileAsync(storedProfile);
            var roundTrippedProfile = (await store.SetupProfilesAsync()).Single();
            Check(roundTrippedProfile.Id == storedProfile.Id
                && roundTrippedProfile.Name == storedProfile.Name
                && roundTrippedProfile.Items.SequenceEqual(storedProfile.Items),
                "SQLite profile CRUD round-trips ordered exact package identity");
            await store.DeleteSetupProfileAsync(storedProfile.Id);
            Check((await store.SetupProfilesAsync()).Count == 0,
                "Deleting a setup profile cascades to its package items");
            try
            {
                await store.SaveSetupProfileAsync(new(Guid.NewGuid(), "Unsafe scope",
                    [new(Guid.NewGuid(), "Vendor.Editor", "winget", "Any")]));
                throw new Exception("Accepted an inexact scope");
            }
            catch (ArgumentException)
            {
                Check(true, "Setup profile persistence rejects an inexact package scope");
            }

            var cleanupDirectory = Path.Combine(directory, "cleanup-root");
            var cleanupNested = Path.Combine(cleanupDirectory, "nested");
            Directory.CreateDirectory(cleanupNested);
            var stablePath = Path.Combine(cleanupNested, "stable.tmp");
            var lockedPath = Path.Combine(cleanupDirectory, "locked.tmp");
            var driftPath = Path.Combine(cleanupDirectory, "drift.tmp");
            var youngPath = Path.Combine(cleanupDirectory, "young.tmp");
            File.WriteAllText(stablePath, "stable");
            File.WriteAllText(lockedPath, "locked");
            File.WriteAllText(driftPath, "drift");
            File.WriteAllText(youngPath, "young");
            foreach (var oldPath in new[] { stablePath, lockedPath, driftPath })
                File.SetLastWriteTimeUtc(oldPath, now.UtcDateTime.AddDays(-8));
            var cleanupActions = new RecordingActionStore();
            var cleanup = new CleanupService(cleanupActions, new OperationCoordinator(), clock,
                cleanupDirectory);
            var scan = await cleanup.ScanAsync();
            Check(scan.Files.Count == 3 && !scan.ScanIncomplete
                && scan.Files.All(file => !file.IsReparsePoint)
                && scan.Files.All(file => !file.Path.EndsWith("young.tmp")),
                "Cleanup scan includes only old regular files under the exact root");
            var emptySelection = cleanup.Select(scan, []);
            var emptyResult = await cleanup.ExecuteApprovedAsync(emptySelection,
                new(emptySelection.PlanId, emptySelection.Digest, now, "approved"));
            Check(emptyResult.Status == ActionStatus.Rejected && scan.Files.Count == 3,
                "Cleanup selects nothing by default and rejects an empty approval");
            var selectedCleanup = cleanup.Select(scan, scan.Files.Select(file => file.Path));
            File.AppendAllText(driftPath, " changed");
            File.SetLastWriteTimeUtc(driftPath, now.UtcDateTime.AddDays(-8));
            var stableBytes = new FileInfo(stablePath).Length;
            CleanupResult partialCleanup;
            using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                partialCleanup = await cleanup.ExecuteApprovedAsync(selectedCleanup,
                    new(selectedCleanup.PlanId, selectedCleanup.Digest, now, "approved"));
            }
            Check(partialCleanup.Status == ActionStatus.Failed && partialCleanup.Deleted == 1
                && partialCleanup.Skipped == 1 && partialCleanup.Failed == 1
                && partialCleanup.BytesDeleted == stableBytes && !File.Exists(stablePath)
                && !Directory.Exists(cleanupNested),
                "Cleanup revalidates drift, continues isolated failures, reports exact partial results, and removes resulting empty directories");
            var tamperedCleanup = selectedCleanup with
            {
                Files = [new(Path.Combine(directory, "outside.tmp"), 1,
                    now.AddDays(-8), false)]
            };
            var tamperedCleanupResult = await cleanup.ExecuteApprovedAsync(tamperedCleanup,
                new(tamperedCleanup.PlanId, tamperedCleanup.Digest, now, "approved"));
            Check(tamperedCleanupResult.Status == ActionStatus.Rejected,
                "Cleanup rejects a tampered path outside the frozen root");
            var cancelPath = Path.Combine(cleanupDirectory, "cancel.tmp");
            File.WriteAllText(cancelPath, "cancel");
            File.SetLastWriteTimeUtc(cancelPath, now.UtcDateTime.AddDays(-8));
            var cancelScan = await cleanup.ScanAsync();
            var cancelPlan = cleanup.Select(cancelScan,
                cancelScan.Files.Where(file => file.Path == Path.GetFullPath(cancelPath))
                    .Select(file => file.Path));
            using var cleanupCancellation = new CancellationTokenSource();
            cleanupCancellation.Cancel();
            var cancelledCleanup = await cleanup.ExecuteApprovedAsync(cancelPlan,
                new(cancelPlan.PlanId, cancelPlan.Digest, now, "approved"),
                ct: cleanupCancellation.Token);
            Check(cancelledCleanup.Status == ActionStatus.Cancelled && File.Exists(cancelPath),
                "Cleanup cancellation before the action gate deletes nothing");
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
        public ActionResult? Result { get; set; }
        public Task<ActionResult> ExecuteAsync(UpdatePlan plan, IProgress<ActionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result ?? new ActionResult(ActionStatus.Succeeded,
                plan.Resolution.AppName, "Executed", CompletedAt: DateTimeOffset.UtcNow));
        }
    }
    private sealed class RecordingVerifier : IUpdateVerifier
    {
        public int Calls { get; private set; }
        public UpdateVerification Result { get; set; }
            = new(true, true, "Fresh inventory confirmed the target version.");
        public Task<UpdateVerification> RefreshAndVerifyAsync(UpdatePlan plan,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
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
    private sealed class RecordingSetupProvider(SetupPlanItem current) : IPackageSetupProvider
    {
        public SetupPlanItem Current { get; set; } = current;
        public Dictionary<Guid, SetupPlanItem> Resolutions { get; } = [];
        public ActionResult Result { get; set; } = new(ActionStatus.Succeeded, "Setup",
            "Installed", CompletedAt: DateTimeOffset.UtcNow);
        public int ResolveCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PackageSearchResult>>(
                [new(Current.Item.PackageId, Current.Name, Current.Item.Source)]);
        public Task<SetupPlanItem> ResolveAsync(SetupProfileItem item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            return Task.FromResult(Resolutions.GetValueOrDefault(item.Id,
                Current with { Item = item }));
        }
        public Task<ActionResult> InstallAsync(SetupPlanItem item, bool acceptAgreements,
            IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallCalls++;
            return Task.FromResult(Result with { Subject = item.Name });
        }
    }
    private sealed class RecordingProfileStore : ISetupProfileStore
    {
        private readonly Dictionary<Guid, SetupProfile> profiles = [];
        public Task<IReadOnlyList<SetupProfile>> SetupProfilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SetupProfile>>(profiles.Values.ToArray());
        public Task SaveSetupProfileAsync(SetupProfile profile, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }
        public Task DeleteSetupProfileAsync(Guid id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); profiles.Remove(id);
            return Task.CompletedTask;
        }
    }
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void SetUtcNow(DateTimeOffset value) => current = value;
    }
}
