using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace SlotCast.Views.Pages;

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