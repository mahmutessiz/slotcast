using episodeTracker.Services;
using System.Net.Http;

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
        private string _showTitle = "";

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
    }
}
