using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SlotCast.Services;

public static class PosterCache
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(PosterCache), new PropertyMetadata(null, OnUrlChanged));
    public static void SetUrl(DependencyObject target, string? value) => target.SetValue(UrlProperty, value);
    public static string? GetUrl(DependencyObject target) => (string?)target.GetValue(UrlProperty);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Downloads = new(4);
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage?>>> Pending = new();

    private static async void OnUrlChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Image image) return;
        image.Source = null;
        if (args.NewValue is not string url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        var request = Pending.GetOrAdd(url, key => new(() => Task.Run(() => LoadAsync(key))));
        try
        {
            var bitmap = await request.Value;
            if (!image.Dispatcher.HasShutdownStarted)
                await image.Dispatcher.InvokeAsync(() =>
                {
                    if (GetUrl(image) == url) image.Source = bitmap;
                });
        }
        catch (TaskCanceledException) { }
        finally { Pending.TryRemove(url, out _); }
    }

    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        await Downloads.WaitAsync();
        try
        {
            var folder = Path.Combine(SlotCastPaths.DataDirectory, "posters");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
            // Poster URLs include provider image versions. Keep successful images across restarts.
            var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path) : await Client.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 400;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (!File.Exists(path))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path, true);
            }
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
            or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.IO.FileFormatException)
        { return null; }
        finally { Downloads.Release(); }
    }
}
