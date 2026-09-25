using Microsoft.Win32;
using PcDashboard.Core;

namespace PcDashboard.Desktop.Providers;

public sealed class RegistryInventory(RegistryHive hive, RegistryView view) : IInventoryProvider
{
    public string Source => $"Registry/{hive}/{view}";
    public Task<InventoryResult> CollectAsync(CancellationToken ct) => Task.Run(() =>
    {
        var now = DateTimeOffset.UtcNow; var apps = new List<InstalledApp>();
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is not null)
        foreach (var id in uninstall.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            using var key = uninstall.OpenSubKey(id);
            if (key is null) continue;
            var name = key.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(name) || key.GetValue("SystemComponent") is int hidden && hidden == 1) continue;
            // HKCU\Software is shared by WOW64; do not display the same current-user registration twice.
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32)
            {
                using var canonical = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var existing = canonical.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + id);
                if (existing is not null && Equals(existing.GetValue("DisplayName"), name)
                    && Equals(existing.GetValue("DisplayVersion"), key.GetValue("DisplayVersion"))
                    && Equals(existing.GetValue("Publisher"), key.GetValue("Publisher"))) continue;
            }
            apps.Add(new($"{Source}/{id}", name, key.GetValue("DisplayVersion") as string,
                key.GetValue("Publisher") as string, Source, id, hive == RegistryHive.CurrentUser ? "User" : "Machine",
                view == RegistryView.Registry64 ? "64-bit registry" : "32-bit registry", now));
        }
        return new InventoryResult(Source, apps, new(Source, now, true));
    }, ct);
}

public sealed class PackagedInventory : IInventoryProvider
{
    public string Source => "Windows packages";
    public Task<InventoryResult> CollectAsync(CancellationToken ct) => Task.Run(() =>
    {
        var now = DateTimeOffset.UtcNow; var apps = new List<InstalledApp>();
        var manager = new Windows.Management.Deployment.PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            ct.ThrowIfCancellationRequested();
            if (package.IsFramework || package.IsResourcePackage) continue;
            var id = package.Id; var v = id.Version;
            var name = package.DisplayName;
            apps.Add(new($"Appx/{id.FamilyName}/{id.Architecture}/{id.ResourceId}",
                string.IsNullOrWhiteSpace(name) ? id.Name : name, $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                package.PublisherDisplayName, Source, id.FamilyName, "User", id.Architecture.ToString(), now));
        }
        return new InventoryResult(Source, apps, new(Source, now, true));
    }, ct);
}
