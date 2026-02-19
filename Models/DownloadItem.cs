using YoutubeAudioDownloader.Infrastructure;

namespace YoutubeAudioDownloader.Models;

public sealed class DownloadItem : ObservableObject
{
    private double _progress;
    private DownloadState _state = DownloadState.Pending;
    private string _statusMessage = "Pending";
    private string _errorMessage = string.Empty;

    public DownloadItem(string url)
    {
        Url = url;
    }

    public string Url { get; }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0d, 100d));
    }

    public DownloadState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
}
