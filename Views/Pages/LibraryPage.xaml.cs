using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.ComponentModel;
using System.Windows.Threading;

namespace SlotCast.Views.Pages;

public partial class LibraryPage : INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }

    public LibraryPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    private int? _shownAnimeId;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelChanged;
        ResetForSelectedAnime();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedAnime)) ResetForSelectedAnime();
    }

    private void ResetForSelectedAnime()
    {
        if (_shownAnimeId == ViewModel.SelectedAnime?.Id) return;
        _shownAnimeId = ViewModel.SelectedAnime?.Id;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded && ViewModel.EpisodeRows.Count > 0)
                EpisodeList.ScrollIntoView(ViewModel.EpisodeRows[0]);
        }));
    }
}
