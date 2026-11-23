using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace SlotCast.Views.Pages
{
    public partial class DashboardPage : INavigableView<HomeViewModel>
    {
        public HomeViewModel ViewModel { get; }

        private async void OnTokenSaved(object? sender, EventArgs e)
        {
            await ViewModel.Workspace.ReloadAfterImportAsync();
            await ViewModel.RefreshHomeCommand.ExecuteAsync(null);
        }

        public DashboardPage(HomeViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
