using Microsoft.Win32;

namespace PcDashboard.Desktop;

public sealed class BackupDialogs
{
    private const string Filter = "PC Dashboard encrypted backup (*.pcdbak)|*.pcdbak";

    public string? ChooseBackupDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create encrypted PC Dashboard backup",
            Filter = Filter,
            DefaultExt = ".pcdbak",
            AddExtension = true,
            FileName = $"pc-dashboard-{DateTime.Now:yyyyMMdd}.pcdbak",
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseBackupToRestore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open encrypted PC Dashboard backup",
            Filter = Filter,
            DefaultExt = ".pcdbak",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
