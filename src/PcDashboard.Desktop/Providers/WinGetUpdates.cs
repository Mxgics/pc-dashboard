using Microsoft.Management.Deployment;
using PcDashboard.Core;

namespace PcDashboard.Desktop.Providers;

public sealed class WinGetUpdates : IUpdateProvider
{
    public IReadOnlyList<PackageCandidate> LastCandidates { get; private set; } = [];
    public Task<UpdateResult> CheckAsync(IReadOnlyList<InstalledApp> apps, CancellationToken ct) => Task.Run(async () =>
    {
        var now = DateTimeOffset.UtcNow;
        var manager = new PackageManager();
        var candidates = new List<PackageCandidate>(); var errors = new List<string>();
        var catalogs = manager.GetPackageCatalogs();
        if (catalogs.Count == 0) throw new InvalidOperationException("WinGet has no configured package sources.");
        for (var sourceIndex = 0; sourceIndex < catalogs.Count; sourceIndex++)
        {
            var source = catalogs[sourceIndex];
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var options = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs };
                options.Catalogs.Add(source);
                var reference = manager.CreateCompositePackageCatalog(options);
                var connection = await reference.ConnectAsync().AsTask(timeout.Token);
                if (connection.Status != ConnectResultStatus.Ok) throw new InvalidOperationException($"Connection: {connection.Status}");
                var result = await connection.PackageCatalog.FindPackagesAsync(new FindPackagesOptions()).AsTask(timeout.Token);
                if (result.Status != FindPackagesResultStatus.Ok || result.WasLimitExceeded)
                    throw new InvalidOperationException($"Search: {result.Status}; truncated: {result.WasLimitExceeded}");
                for (var matchIndex = 0; matchIndex < result.Matches.Count; matchIndex++)
                {
                    var found = result.Matches[matchIndex];
                    ct.ThrowIfCancellationRequested();
                    var package = found.CatalogPackage; var installed = package.InstalledVersion;
                    if (installed is null || package.AvailableVersions.Count == 0) continue;
                    candidates.Add(new(package.Id, source.Info.Name, installed.Version,
                        package.DefaultInstallVersion?.Version, package.IsUpdateAvailable,
                        Copy(installed.ProductCodes), Copy(installed.PackageFamilyNames),
                        installed.GetMetadata(PackageVersionMetadataField.InstalledScope)));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { errors.Add($"{source.Info.Name}: check timed out after 45 seconds."); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{source.Info.Name}: {ex.Message}"); }
        }
        var evidence = new Dictionary<string, UpdateEvidence>();
        LastCandidates = candidates;
        foreach (var app in apps)
        {
            var match = UpdateMatching.Match(app, candidates, now);
            // Incomplete catalog coverage cannot establish a negative result. Retain prior evidence.
            if (errors.Count > 0 && match.Status != UpdateStatus.Available) continue;
            evidence[app.Id] = match;
        }
        return new UpdateResult(evidence, new("WinGet", now, errors.Count == 0,
            errors.Count == 0 ? null : string.Join("; ", errors)));
    }, ct);
    private static string[] Copy(IReadOnlyList<string> values)
    {
        var result = new string[values.Count];
        for (var i = 0; i < result.Length; i++) result[i] = values[i];
        return result;
    }
}
