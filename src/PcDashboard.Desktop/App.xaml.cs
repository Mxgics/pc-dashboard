using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcDashboard.Core;
using PcDashboard.Desktop.Providers;

namespace PcDashboard.Desktop;
public partial class App : Application
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcDashboard");
    private ServiceProvider? services;
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length != 0) { MessageBox.Show("Command-line exports are not supported.", "PC Dashboard"); Shutdown(1); return; }
            var providers = new List<IInventoryProvider>();
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                providers.Add(new RegistryInventory(hive, view));
            providers.Add(new PackagedInventory());
            instance = new Mutex(true, @"Local\PcDashboard-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value, out var first);
            if (!first) { MessageBox.Show("PC Dashboard is already open."); Shutdown(); return; }
            var collection = new ServiceCollection();
            var store = new SqliteDashboardStore(Path.Combine(DataDirectory, "dashboard.db"));
            var diagnostics = new SqliteDiagnostics(Path.Combine(DataDirectory, "dashboard.db"));
            diagnostics.ImportLegacyFile(Path.Combine(DataDirectory, "diagnostics.log"));
            collection.AddLogging(builder => builder.AddProvider(new LocalLogProvider(diagnostics, () =>
                Dispatcher.BeginInvoke(new Action(() => MessageBox.Show("Local diagnostics could not be saved. Check local data permissions and free disk space.", "PC Dashboard"))))));
            collection.AddWpfBlazorWebView();
            collection.AddSingleton<IDashboardStore>(store);
            foreach (var provider in providers) collection.AddSingleton(provider);
            collection.AddSingleton<IUpdateProvider, WinGetUpdates>();
            collection.AddSingleton<DashboardService>();
            services = collection.BuildServiceProvider();
            new MainWindow(services).Show();
        }
        catch (Exception)
        {
            MessageBox.Show("PC Dashboard could not start. Check runtime prerequisites, local data permissions, and database compatibility. Local data has not been reset.", "PC Dashboard");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        services?.GetService<DashboardService>()?.Cancel();
        services?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
