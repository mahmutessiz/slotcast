using SlotCast.Services;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SlotCast.ViewModels.Pages;

public partial class UpdatesViewModel(AppUpdateService service) : ObservableObject
{
    private AppUpdate? _available;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;
    public bool IsIdle => !IsBusy;
    public bool CanInstall => _available is not null && !IsBusy;
    [ObservableProperty] private string _status = "Updates are checked at startup.";
    [ObservableProperty] private double _downloadProgress;

    [RelayCommand] private async Task CheckForUpdatesAsync() => await CheckAsync(false);

    public async Task CheckAsync(bool startup)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Checking GitHub releases…";
        try
        {
            _available = await service.CheckAsync();
            Status = _available is null ? "No newer stable release is available." : $"SlotCast {_available.Version} is available.";
        }
        catch (Exception ex) when (IsExpected(ex))
        { _available = null; Status = FriendlyError(ex); }
        finally { IsBusy = false; }
        if (startup && _available is not null && MessageBox.Show(
            $"SlotCast {_available.Version} is available. Download and install it now? SlotCast will close for the update. Your library will be kept.",
            "SlotCast update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            await InstallUpdateAsync();
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsBusy || _available is null) return;
        IsBusy = true;
        DownloadProgress = 0;
        Status = "Downloading and verifying the update…";
        try
        {
            var installer = await service.DownloadAsync(_available, new Progress<double>(value => DownloadProgress = value));
            // Preserve a custom installation folder; the database remains outside it.
            var target = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var start = new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Arguments = $"/SILENT /SP- /NORESTART /CLOSEAPPLICATIONS /DIR=\"{target}\" /SLOTCASTUPDATE=1"
            };
            using var process = Process.Start(start) ?? throw new IOException("The installer could not start.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally { IsBusy = false; }
    }

    private static bool IsExpected(Exception ex) => ex is HttpRequestException or TaskCanceledException
        or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
        or System.ComponentModel.Win32Exception or KeyNotFoundException or FormatException;
    private static string FriendlyError(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        HttpRequestException => "Could not reach GitHub. Check your connection or try again later (GitHub may limit requests).",
        TaskCanceledException => "The update request timed out. Try again later.",
        _ => "Could not complete the update. Your installed app and library have been kept. Try again or download the installer from GitHub."
    };
}