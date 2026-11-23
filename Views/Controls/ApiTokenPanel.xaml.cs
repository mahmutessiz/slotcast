using SlotCast.ViewModels.Pages;
using System.Windows.Controls;

namespace SlotCast.Views.Controls;

public partial class ApiTokenPanel : UserControl
{
    public event EventHandler? TokenSaved;
    public ApiTokenPanel() => InitializeComponent();
    private async void SaveToken(object sender, RoutedEventArgs e)
    {
        if (DataContext is ApiTokenViewModel viewModel && await viewModel.SaveAsync(TokenInput.Password))
        {
            TokenInput.Clear();
            TokenSaved?.Invoke(this, EventArgs.Empty);
        }
    }
}