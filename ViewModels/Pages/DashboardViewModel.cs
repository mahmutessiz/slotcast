using episodeTracker.Models;
using episodeTracker.Services;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;

namespace episodeTracker.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly SimklService _simkl;

        public DashboardViewModel(SimklService simkl)
        {
            _simkl = simkl;
        }

        [ObservableProperty]
        private List<SimklTrendingAnime> _trendingAnimes = [];

        [ObservableProperty]
        private string _showTitle = "";

        [ObservableProperty]
        private int? _showYear;

        [ObservableProperty]
        private string _showOverview = "";

        [ObservableProperty]
        private string _errorMessage = "";


        [RelayCommand]
        private async Task LoadShowAsync()
        {
            ErrorMessage = "";

            try
            {
                var show = await _simkl.GetShowAsync(17465);
                ShowTitle = show?.Title ?? "Show not found.";
                ShowYear = show?.Year ?? 000;
                ShowOverview = show?.Overview ?? "";
            }
            catch (HttpRequestException)
            {
                ErrorMessage = "Could not load the show from SIMKL.";
            }
            catch (OperationCanceledException)
            {
                ErrorMessage = "The request was canceled or timed out.";
            }
        }

        [RelayCommand]
        private async Task LoadTrendingAnimesAsync()
        {
            ErrorMessage = "";
            try
            {
                TrendingAnimes = await _simkl.GetTrendingAnimesAsync();
            }
            catch (HttpRequestException)
            {
                ErrorMessage = "Could not load trending anime from SIMKL.";
            }
            catch (OperationCanceledException)
            {
                ErrorMessage = "The request was canceled or timed out.";
            }
            catch (JsonException)
            {
                ErrorMessage = "The trending feed returned an unexpected format.";
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        [RelayCommand]
        private void OpenSimkl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "simkl.com")
                return;

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                ErrorMessage = "Could not open the SIMKL page in your browser.";
            }
        }
    }
}
