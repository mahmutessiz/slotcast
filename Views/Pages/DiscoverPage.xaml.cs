using episodeTracker.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace episodeTracker.Views.Pages;

public partial class DiscoverPage : INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }

    public DiscoverPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}