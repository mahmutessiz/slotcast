using SlotCast.Models;
using SlotCast.Views.Pages;
using Wpf.Ui;

namespace SlotCast.Services;

public sealed class AnimeDetailsState
{
    public HomeAnimeCard? Card { get; set; }
}

public sealed class AnimeDetailsNavigationService(AnimeDetailsState state, INavigationService navigation)
{
    public void Open(HomeAnimeCard card)
    {
        state.Card = card;
        navigation.Navigate(typeof(AnimeDetailsPage));
    }
}
