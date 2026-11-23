using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace SlotCast.Views.Pages
{
    public partial class DashboardPage : INavigableView<HomeViewModel>
    {
        public HomeViewModel ViewModel { get; }

        public DashboardPage(HomeViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
