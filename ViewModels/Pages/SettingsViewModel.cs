using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using SlotCast.Services;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace SlotCast.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private readonly WatchlistTransferService _transfer;
        private readonly DashboardViewModel _workspace;

        public UpdatesViewModel Updates { get; }
        public ApiTokenViewModel? Credentials { get; }
        public SettingsViewModel(WatchlistTransferService transfer, DashboardViewModel workspace, UpdatesViewModel updates, ApiTokenViewModel? credentials = null)
        {
            _transfer = transfer;
            Updates = updates;
            Credentials = credentials;
            _workspace = workspace;
        }

        public string DatabasePath => SlotCastPaths.DatabaseFile;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTransferIdle))]
        private bool _isTransferring;
        public bool IsTransferIdle => !IsTransferring;
        [ObservableProperty] private string _transferMessage = "";
        [ObservableProperty] private string _transferError = "";

        [RelayCommand]
        private async Task ExportWatchlistAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export SlotCast watchlist", Filter = "SlotCast JSON backup (*.json)|*.json",
                DefaultExt = ".json", AddExtension = true, OverwritePrompt = true,
                FileName = $"SlotCast-watchlist-{DateTime.Today:yyyy-MM-dd}.json"
            };
            if (dialog.ShowDialog() != true) return;
            await TransferAsync(async () =>
            {
                var result = await _transfer.ExportAsync(dialog.FileName);
                TransferMessage = $"Exported {result.AnimeCount} anime and {result.WatchedEpisodeCount} watched episodes.";
            });
        }

        [RelayCommand]
        private async Task ImportWatchlistAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import SlotCast watchlist", Filter = "SlotCast JSON backup (*.json)|*.json",
                CheckFileExists = true, Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            await TransferAsync(async () =>
            {
                var result = await _transfer.ImportAsync(dialog.FileName);
                await _workspace.ReloadAfterImportAsync();
                TransferMessage = $"Import complete: {result.AnimeCount} anime and {result.WatchedEpisodeCount} watched episodes added. Existing progress was kept.";
            });
        }

        private async Task TransferAsync(Func<Task> work)
        {
            if (IsTransferring) return;
            IsTransferring = true;
            TransferMessage = "";
            TransferError = "";
            try { await work(); }
            catch (InvalidDataException ex) { TransferError = ex.Message; }
            catch (JsonException) { TransferError = "This file is not a valid SlotCast JSON backup."; }
            catch (IOException) { TransferError = "Could not read or write the backup. Check the file location and available disk space."; }
            catch (UnauthorizedAccessException) { TransferError = "Access to the selected file was denied. Choose another location."; }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException) { TransferError = "The import could not be saved. Existing data was kept."; }
            catch (Microsoft.Data.Sqlite.SqliteException) { TransferError = "Could not access the local database. Close other SlotCast instances and try again."; }
            finally { IsTransferring = false; }
        }

        [RelayCommand]
        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(SlotCastPaths.DataDirectory);
                Process.Start(new ProcessStartInfo(SlotCastPaths.DataDirectory) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { TransferError = "Could not open the local data folder."; }
        }

        private bool _isInitialized = false;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = $"SlotCast - {GetAssemblyVersion()}";

            _isInitialized = true;
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
        }
    }
}
