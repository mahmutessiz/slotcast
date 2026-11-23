using episodeTracker.Services;
using episodeTracker.ViewModels.Pages;
using episodeTracker.ViewModels.Windows;
using episodeTracker.Views.Pages;
using episodeTracker.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace episodeTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
        // https://docs.microsoft.com/dotnet/core/extensions/configuration
        // https://docs.microsoft.com/dotnet/core/extensions/logging
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c =>
            {
                c.SetBasePath(AppContext.BaseDirectory);
                c.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
                c.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddNavigationViewPageProvider();

                services.AddHostedService<ApplicationHostService>();

                // Theme manipulation
                services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<HomeViewModel>();
                services.AddSingleton<AnimeDetailsState>();
                services.AddSingleton<AnimeDetailsNavigationService>();
                services.AddSingleton<AnimeDetailsViewModel>();
                services.AddSingleton<AnimeDetailsPage>();
                services.AddSingleton<CalendarPage>();
                services.AddSingleton<LibraryPage>();
                services.AddSingleton<DiscoverPage>();
                services.AddSingleton<DataPage>();
                services.AddSingleton<DataViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                services.AddHttpClient("Simkl", client =>
                {
                    client.BaseAddress = new Uri("https://api.simkl.com/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                
                    var apiKey = context.Configuration["Simkl:ClientId"];
                
                    if (string.IsNullOrWhiteSpace(apiKey))
                        throw new InvalidOperationException("SIMKL Client ID is missing.");
                
                    client.DefaultRequestHeaders.Add("simkl-api-key", apiKey);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("slotcast/1.0");
                });
                
                services.AddHttpClient("SimklPublic", client =>
                {
                    client.BaseAddress = new Uri("https://data.simkl.in/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("slotcast/1.0");
                });

                services.AddSingleton<SimklService>();

                System.IO.Directory.CreateDirectory(SlotCastPaths.DataDirectory);
                services.AddDbContextFactory<SlotCastDbContext>(options =>
                    options.UseSqlite($"Data Source={SlotCastPaths.DatabaseFile}"));
                services.AddSingleton<AnimeLibraryService>();
                services.AddSingleton<WatchlistTransferService>();
                services.AddSingleton<AnimeScheduleService>();
                services.AddHttpClient("AnimeSchedule", client =>
                {
                    client.BaseAddress = new Uri("https://animeschedule.net/api/v3/");
                    client.Timeout = TimeSpan.FromSeconds(20);
                    var token = context.Configuration["AnimeScheduleApiToken"];
                    if (string.IsNullOrWhiteSpace(token))
                        throw new InvalidOperationException("Add AnimeScheduleApiToken to appsettings.local.json to search and refresh schedules.");
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SlotCast/1.0");
                });
            }).Build();

        /// <summary>
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            await _host.StartAsync();
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }
    }
}
