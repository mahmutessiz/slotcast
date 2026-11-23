using SlotCast.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using SlotCast.Models;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SlotCast.Views.Pages;

public partial class CalendarPage : INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }

    public CalendarPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => QueueCenter(false);
    }

    private bool _focusQueued;
    private bool _flashRequested;
    private bool _needsInitialCenter;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelChanged;
        _needsInitialCenter = true;
        QueueCenter(true);
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.PropertyChanged -= OnViewModelChanged;
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.TodayFocusVersion)) QueueCenter(true);
        else if (e.PropertyName == nameof(ViewModel.CalendarDays) && _needsInitialCenter) QueueCenter(true);
        else if (e.PropertyName == nameof(ViewModel.DisplayWeek)) QueueCenter(false);
    }

    private void QueueCenter(bool flash)
    {
        if (!IsLoaded) return;
        _flashRequested |= flash;
        if (_focusQueued) return;
        _focusQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _focusQueued = false;
            if (!IsLoaded) return;
            WeekScroller.UpdateLayout();
            var target = _flashRequested ? DateTime.Today : ViewModel.SelectedDate.Date;
            var index = ViewModel.CalendarDays.FindIndex(x => x.Date == target);
            if (index >= 0 && WeekScroller.ViewportWidth > 0)
            {
                _needsInitialCenter = false;
                var columnWidth = WeekItems.ActualWidth / 7;
                WeekScroller.ScrollToHorizontalOffset(Math.Clamp((index + .5) * columnWidth - WeekScroller.ViewportWidth / 2,
                    0, WeekScroller.ScrollableWidth));
                if (_flashRequested)
                {
                    var container = WeekItems.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
                    var header = container is null ? null : FindHeader(container);
                    if (header?.Template.FindName("TodayFlash", header) is Border pulse)
                        pulse.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 0, TimeSpan.FromSeconds(1.5))
                            { FillBehavior = FillBehavior.Stop });
                }
            }
            _flashRequested = false;
        }));
    }

    private static Button? FindHeader(DependencyObject parent)
    {
        if (parent is Button { DataContext: CalendarDay }) return (Button)parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindHeader(VisualTreeHelper.GetChild(parent, i)) is { } header) return header;
        return null;
    }
}
