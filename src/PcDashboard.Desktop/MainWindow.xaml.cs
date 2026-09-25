using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PcDashboard.Core;
namespace PcDashboard.Desktop;
public partial class MainWindow : Window
{
    public MainWindow(ServiceProvider services)
    {
        InitializeComponent();
        WebView.Services = services;
        WebView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = System.IO.Path.Combine(App.DataDirectory, "WebView2");
        WebView.BlazorWebViewInitialized += (_, e) => {
            e.WebView.CoreWebView2.NavigationCompleted += (_, navigation) => {
                if (!navigation.IsSuccess) services.GetRequiredService<ILogger<MainWindow>>().LogWarning("WebView navigation failed: {Status}", navigation.WebErrorStatus);
            };
        };
        WebView.UrlLoading += (_, e) => {
            if (e.Url.Host is not ("0.0.0.0" or "0.0.0.1")) e.UrlLoadingStrategy = Microsoft.AspNetCore.Components.WebView.UrlLoadingStrategy.CancelLoad;
        };
        Closed += (_, _) => services.GetRequiredService<DashboardService>().Cancel();
    }
}
