using SlotCast.Services;
using System.IO;
using System.Net.Http;

namespace SlotCast.ViewModels.Pages;

public partial class ApiTokenViewModel : ObservableObject
{
    private readonly AnimeScheduleCredentialService _credentials;
    public ApiTokenViewModel(AnimeScheduleCredentialService credentials)
    {
        _credentials = credentials;
        credentials.Changed += () =>
        {
            void Notify() { OnPropertyChanged(nameof(NeedsToken)); OnPropertyChanged(nameof(ConnectionStatus)); }
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Notify);
            else Notify();
        };
    }
    public bool NeedsToken => !_credentials.IsConfigured;
    public int Revision => _credentials.Revision;
    public string ConnectionStatus => NeedsToken ? "Add an AnimeSchedule token to load anime and schedules." : "An AnimeSchedule token is configured on this PC.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isSaving;
    public bool IsIdle => !IsSaving;
    [ObservableProperty] private string _status = "";

    public async Task<bool> SaveAsync(string token)
    {
        if (IsSaving) return false;
        IsSaving = true;
        Status = "Checking and saving your token…";
        try
        {
            await _credentials.ValidateAndSaveAsync(token);
            Status = "Token verified and saved on this PC.";
            return true;
        }
        catch (InvalidDataException ex) { Status = ex.Message; }
        catch (HttpRequestException) { Status = "Could not verify the token. Check your connection and try again. Your previous token was kept."; }
        catch (TaskCanceledException) { Status = "Token verification timed out. Try again."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Could not save the token on this PC. Check folder permissions and free space."; }
        finally { IsSaving = false; }
        return false;
    }
}