using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace SlotCast.Views.Pages;

public partial class AnimeDetailsPage : INavigableView<AnimeDetailsViewModel>
{
    public AnimeDetailsViewModel ViewModel { get; }
    public AnimeDetailsPage(AnimeDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
