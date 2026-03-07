using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Converters;

public sealed class DownloadStateToColorConverter : IValueConverter
{
    public static readonly DownloadStateToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DownloadState state)
        {
            return state switch
            {
                DownloadState.Completed   => new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981")),
                DownloadState.Downloading => new SolidColorBrush(Avalonia.Media.Color.Parse("#007BFF")),
                DownloadState.Failed      => new SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444")),
                DownloadState.Stopped     => new SolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B")),
                DownloadState.Queued      => new SolidColorBrush(Avalonia.Media.Color.Parse("#6366F1")),
                _                         => new SolidColorBrush(Avalonia.Media.Color.Parse("#94A3B8")),
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
