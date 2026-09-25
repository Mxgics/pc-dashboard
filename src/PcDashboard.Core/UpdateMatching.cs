namespace PcDashboard.Core;

public sealed record PackageCandidate(string Id, string Source, string InstalledVersion,
    string? AvailableVersion, bool UpdateAvailable, IReadOnlyList<string> ProductCodes,
    IReadOnlyList<string> FamilyNames, string? Scope = null);

public static class UpdateMatching
{
    public static UpdateEvidence Match(InstalledApp app, IReadOnlyList<PackageCandidate> candidates, DateTimeOffset checkedAt)
    {
        var matches = candidates.Where(c => (app.Source == "Windows packages" ? c.FamilyNames : c.ProductCodes)
            .Contains(app.NativeId, StringComparer.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(c.Scope) || string.Equals(NormalizeScope(c.Scope), NormalizeScope(app.Scope), StringComparison.OrdinalIgnoreCase))
            .Distinct().ToArray();
        if (matches.Length == 0) return new(UpdateStatus.Unsupported, "WinGet", CheckedAt: checkedAt,
            Note: "No exact installed identity matched a configured source. Publisher updates may still exist.");
        if (matches.Length != 1) return new(UpdateStatus.Unknown, "WinGet", CheckedAt: checkedAt,
            Note: "Multiple package candidates matched this identity; no update conclusion was made.");
        var match = matches[0];
        if (string.IsNullOrWhiteSpace(app.Version) || string.Equals(match.InstalledVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(app.Version, match.InstalledVersion, StringComparison.OrdinalIgnoreCase))
            return new(UpdateStatus.Unknown, match.Source, match.Id, match.AvailableVersion, checkedAt,
                "Installed version is missing or differs from WinGet's installed evidence; versions were not compared.");
        return new(match.UpdateAvailable ? UpdateStatus.Available : UpdateStatus.NoneReported,
            match.Source, match.Id, match.AvailableVersion, checkedAt,
            "WinGet source evidence, not a guarantee of the publisher's latest release. Pin details are not exposed by this adapter.", "Not exposed");
    }
    private static string? NormalizeScope(string? scope) => string.Equals(scope, "System", StringComparison.OrdinalIgnoreCase) ? "Machine" : scope;
}
