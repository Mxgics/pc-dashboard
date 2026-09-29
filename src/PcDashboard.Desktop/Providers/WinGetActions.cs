using Microsoft.Management.Deployment;
using PcDashboard.Core;

namespace PcDashboard.Desktop.Providers;

/// <summary>Resolves and executes one exact WinGet upgrade without invoking a shell.</summary>
public sealed class WinGetActions : IUpdateResolver, IUpdateExecutor, IPackageSetupProvider
{
    private readonly PackageManager manager = new();

    public async Task<UpdateResolution> ResolveAsync(InstalledApp app, CancellationToken ct)
    {
        if (app.Update is not { Status: UpdateStatus.Available, PackageId: not null,
            AvailableVersion: not null } evidence)
            throw new InvalidOperationException("Fresh source-reported update evidence is required.");

        var package = await FindExactAsync(evidence.PackageId, evidence.Source, ct);
        var targetId = FindVersionId(package, evidence.AvailableVersion);
        var target = package.GetPackageVersionInfo(targetId);
        var scope = NormalizeScope(app.Scope);
        var options = CreateOptions(targetId, scope, acceptAgreements: false);
        var installer = target.GetApplicableInstaller(options)
            ?? throw new InvalidOperationException("WinGet found no installer for the exact approved scope.");
        var metadata = target.GetCatalogPackageMetadata();

        return new(app.Id, app.Name, package.Id, evidence.Source, app.Version ?? string.Empty,
            target.Version, scope, installer.InstallerType.ToString(),
            installer.ElevationRequirement.ToString(), Agreements(metadata));
    }

    public async Task<ActionResult> ExecuteAsync(UpdatePlan plan, IProgress<ActionProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var package = await FindExactAsync(plan.Resolution.PackageId, plan.Resolution.Source, ct);
        var targetId = FindVersionId(package, plan.Resolution.TargetVersion);
        var target = package.GetPackageVersionInfo(targetId);
        var options = CreateOptions(targetId, plan.Resolution.Scope,
            plan.Resolution.Agreements.Count > 0);
        var installer = target.GetApplicableInstaller(options)
            ?? throw new InvalidOperationException("WinGet found no installer for the exact approved scope.");
        EnsureExecutionStillMatches(plan.Resolution, package, target, installer);

        return await ExecuteNativeAsync(plan.Resolution.AppName, "Update", progress, ct,
            (nativeCt, adapter) => manager.UpgradePackageAsync(package, options)
                .AsTask(nativeCt, adapter));
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query,
        CancellationToken ct)
    {
        var found = new List<PackageSearchResult>();
        foreach (var source in manager.GetPackageCatalogs())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var catalog = await ConnectAsync(source,
                    CompositeSearchBehavior.RemotePackagesFromAllCatalogs, ct);
                var search = new FindPackagesOptions { ResultLimit = 10 };
                search.Selectors.Add(new PackageMatchFilter
                {
                    Field = PackageMatchField.Name,
                    Option = PackageFieldMatchOption.ContainsCaseInsensitive,
                    Value = query
                });
                var result = await catalog.FindPackagesAsync(search).AsTask(ct);
                if (result.Status != FindPackagesResultStatus.Ok) continue;
                found.AddRange(result.Matches.Select(match => new PackageSearchResult(
                    match.CatalogPackage.Id, match.CatalogPackage.Name, source.Info.Name)));
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return found.DistinctBy(item => (item.PackageId.ToUpperInvariant(),
                item.Source.ToUpperInvariant()))
            .OrderBy(item => item.Name).ThenBy(item => item.PackageId).Take(25).ToArray();
    }

    public async Task<SetupPlanItem> ResolveAsync(SetupProfileItem item, CancellationToken ct)
    {
        try
        {
            var package = await FindExactAsync(item.PackageId, item.Source, ct);
            var target = package.DefaultInstallVersion
                ?? throw new InvalidOperationException("No version is currently offered.");
            var targetId = FindVersionId(package, target.Version);
            var options = CreateOptions(targetId, item.Scope, acceptAgreements: false);
            var installer = target.GetApplicableInstaller(options)
                ?? throw new InvalidOperationException(
                    "No installer is available for the exact selected scope.");
            var installed = package.InstalledVersion?.Version;
            var disposition = installed is null ? SetupDisposition.Install
                : package.IsUpdateAvailable ? SetupDisposition.Update : SetupDisposition.Current;
            return new(item, package.Name, installed, target.Version, disposition,
                installer.InstallerType.ToString(), installer.ElevationRequirement.ToString(),
                Agreements(target.GetCatalogPackageMetadata()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(item, item.PackageId, null, null, SetupDisposition.Blocked,
                "Unavailable", "Unknown", [], ex.Message);
        }
    }

    public async Task<ActionResult> InstallAsync(SetupPlanItem item, bool acceptAgreements,
        IProgress<ActionProgress>? progress, CancellationToken ct)
    {
        if (item.Disposition is not (SetupDisposition.Install or SetupDisposition.Update))
            throw new InvalidOperationException("Only install and update plan items can execute.");
        var package = await FindExactAsync(item.Item.PackageId, item.Item.Source, ct);
        var targetId = FindVersionId(package, item.TargetVersion
            ?? throw new InvalidOperationException("The approved target version is unavailable."));
        var target = package.GetPackageVersionInfo(targetId);
        var options = CreateOptions(targetId, item.Item.Scope, acceptAgreements);
        var installer = target.GetApplicableInstaller(options)
            ?? throw new InvalidOperationException(
                "No installer is available for the exact approved scope.");
        EnsureSetupStillMatches(item, package, target, installer);
        return await ExecuteNativeAsync(item.Name,
            item.Disposition == SetupDisposition.Update ? "Update" : "Install", progress, ct,
            (nativeCt, adapter) => item.Disposition == SetupDisposition.Update
                ? manager.UpgradePackageAsync(package, options).AsTask(nativeCt, adapter)
                : manager.InstallPackageAsync(package, options).AsTask(nativeCt, adapter));
    }

    private async Task<CatalogPackage> FindExactAsync(string packageId, string sourceName,
        CancellationToken ct)
    {
        var sources = manager.GetPackageCatalogs()
            .Where(source => string.Equals(source.Info.Name, sourceName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sources.Length != 1)
            throw new InvalidOperationException("The exact approved WinGet source is unavailable or ambiguous.");

        var options = new CreateCompositePackageCatalogOptions
        {
            CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs
        };
        options.Catalogs.Add(sources[0]);
        var connection = await manager.CreateCompositePackageCatalog(options).ConnectAsync().AsTask(ct);
        if (connection.Status != ConnectResultStatus.Ok)
            throw new InvalidOperationException($"Could not connect to the approved WinGet source: {connection.Status}.");

        var search = new FindPackagesOptions { ResultLimit = 2 };
        search.Filters.Add(new PackageMatchFilter
        {
            Field = PackageMatchField.Id,
            Option = PackageFieldMatchOption.Equals,
            Value = packageId
        });
        var result = await connection.PackageCatalog.FindPackagesAsync(search).AsTask(ct);
        if (result.Status != FindPackagesResultStatus.Ok || result.WasLimitExceeded
            || result.Matches.Count != 1)
            throw new InvalidOperationException(
                "WinGet could not resolve one exact package from the approved source.");

        var package = result.Matches[0].CatalogPackage;
        if (!string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WinGet returned an ambiguous package identity.");
        return package;
    }

    private async Task<PackageCatalog> ConnectAsync(PackageCatalogReference source,
        CompositeSearchBehavior behavior, CancellationToken ct)
    {
        var options = new CreateCompositePackageCatalogOptions
        {
            CompositeSearchBehavior = behavior
        };
        options.Catalogs.Add(source);
        var connection = await manager.CreateCompositePackageCatalog(options)
            .ConnectAsync().AsTask(ct);
        if (connection.Status != ConnectResultStatus.Ok)
            throw new InvalidOperationException(
                $"Could not connect to the selected WinGet source: {connection.Status}.");
        return connection.PackageCatalog;
    }

    private static PackageVersionId FindVersionId(CatalogPackage package, string version)
    {
        var matches = package.AvailableVersions
            .Where(candidate => string.Equals(candidate.Version, version,
                StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException(
                "The exact approved package version is no longer offered.");
    }

    private static InstallOptions CreateOptions(PackageVersionId versionId, string scope,
        bool acceptAgreements) => new()
    {
        PackageVersionId = versionId,
        PackageInstallMode = PackageInstallMode.Default,
        PackageInstallScope = ExactScope(scope),
        AcceptPackageAgreements = acceptAgreements,
        AllowHashMismatch = false
    };

    private static PackageInstallScope ExactScope(string scope) => NormalizeScope(scope) switch
    {
        "User" => PackageInstallScope.User,
        "Machine" => PackageInstallScope.System,
        _ => throw new InvalidOperationException("The installed package scope is not exact enough to update safely.")
    };

    private static string NormalizeScope(string scope)
        => scope.Equals("System", StringComparison.OrdinalIgnoreCase) ? "Machine"
            : scope.Equals("Machine", StringComparison.OrdinalIgnoreCase) ? "Machine"
            : scope.Equals("User", StringComparison.OrdinalIgnoreCase) ? "User"
            : scope;

    private static void EnsureExecutionStillMatches(UpdateResolution resolution,
        CatalogPackage package, PackageVersionInfo target, PackageInstallerInfo installer)
    {
        var execution = resolution with
        {
            PackageId = package.Id,
            TargetVersion = target.Version,
            Scope = NormalizeScope(resolution.Scope),
            InstallerType = installer.InstallerType.ToString(),
            ElevationRequirement = installer.ElevationRequirement.ToString(),
            Agreements = Agreements(target.GetCatalogPackageMetadata())
        };
        if (!PlanDigests.Equals(PlanDigests.Compute(resolution), PlanDigests.Compute(execution)))
            throw new InvalidOperationException(
                "WinGet package details changed after approval; review the update again.");
    }

    private static void EnsureSetupStillMatches(SetupPlanItem approved,
        CatalogPackage package, PackageVersionInfo target, PackageInstallerInfo installer)
    {
        var execution = approved with
        {
            Name = package.Name,
            TargetVersion = target.Version,
            InstallerType = installer.InstallerType.ToString(),
            ElevationRequirement = installer.ElevationRequirement.ToString(),
            Agreements = Agreements(target.GetCatalogPackageMetadata())
        };
        if (!PlanDigests.Equals(PlanDigests.Compute(approved), PlanDigests.Compute(execution)))
            throw new InvalidOperationException(
                "WinGet package details changed after approval; review the setup plan again.");
    }

    private static async Task<ActionResult> ExecuteNativeAsync(string subject, string verb,
        IProgress<ActionProgress>? progress, CancellationToken ct,
        Func<CancellationToken, IProgress<InstallProgress>, Task<InstallResult>> execute)
    {
        ct.ThrowIfCancellationRequested();
        var stateLock = new object();
        var installationStarted = false;
        using var nativeCancellation = new CancellationTokenSource();
        using var cancellationRegistration = ct.Register(() =>
        {
            bool cancel;
            lock (stateLock) cancel = !installationStarted;
            if (cancel) nativeCancellation.Cancel();
        });
        var adapter = new InlineProgress<InstallProgress>(value =>
        {
            var stage = value.State switch
            {
                PackageInstallProgressState.Downloading => ActionProgressStage.Downloading,
                PackageInstallProgressState.Installing => ActionProgressStage.Installing,
                PackageInstallProgressState.PostInstall => ActionProgressStage.PostInstall,
                _ => ActionProgressStage.Queued
            };
            bool canCancel;
            lock (stateLock)
            {
                installationStarted |= stage is ActionProgressStage.Installing
                    or ActionProgressStage.PostInstall;
                canCancel = !installationStarted;
            }
            progress?.Report(new(stage, $"{subject}: {StageMessage(stage)}",
                value.BytesDownloaded, value.BytesRequired, value.DownloadProgress * 100,
                value.InstallationProgress * 100, canCancel));
        });
        progress?.Report(new(ActionProgressStage.Queued,
            $"{verb} for {subject} queued in WinGet."));
        try
        {
            return MapResult(subject, await execute(nativeCancellation.Token, adapter));
        }
        catch (OperationCanceledException)
        {
            lock (stateLock)
                return UpdateExecutionResults.Cancellation(subject, installationStarted,
                    DateTimeOffset.UtcNow);
        }
    }

    private static IReadOnlyList<PackageAgreementInfo> Agreements(CatalogPackageMetadata metadata)
        => metadata.Agreements.Select(agreement => new PackageAgreementInfo(agreement.Label,
            agreement.Text, agreement.Url?.ToString())).ToArray();

    private static string StageMessage(ActionProgressStage stage) => stage switch
    {
        ActionProgressStage.Downloading => "Downloading package.",
        ActionProgressStage.Installing => "Installer is running.",
        ActionProgressStage.PostInstall => "Completing installation.",
        _ => "Waiting for WinGet."
    };

    private static ActionResult MapResult(string subject, InstallResult result)
    {
        var code = result.Status == InstallResultStatus.InstallError
            ? unchecked((int)result.InstallerErrorCode)
            : result.ExtendedErrorCode?.HResult;
        return UpdateExecutionResults.NativeTerminal(subject,
            result.Status == InstallResultStatus.Ok, result.RebootRequired,
            result.Status.ToString(), code, DateTimeOffset.UtcNow);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
