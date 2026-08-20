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
        private set
        {
            if (SetProperty(ref _progress, Math.Clamp(value, 0d, 100d)))
            {
                OnPropertyChanged(nameof(ShowProgress));
            }
        }
    }

    public DownloadState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(ShowProgress));
            }
        }
    }

    /// <summary>
    /// True only while actively downloading and below 100%.
    /// Hides the bar during the audio-extraction phase (yt-dlp has
    /// already reported 100% download but ffmpeg is still running).
    /// Also shows an empty bar for queued items.
    /// </summary>
    public bool ShowProgress =>
        State == DownloadState.Queued ||
        (State == DownloadState.Downloading && Progress < 100d);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public void Queue()
    {
        Progress = 0d;
        State = DownloadState.Queued;
        StatusMessage = "Queued";
        ErrorMessage = string.Empty;
    }

    public void Start()
    {
        State = DownloadState.Downloading;
        StatusMessage = "Downloading";
    }

    public void ReportProgress(double value) => Progress = value;

    public void ReportStatus(string message) => StatusMessage = message;

    public void Complete()
    {
        Progress = 100d;
        State = DownloadState.Completed;
        StatusMessage = "Completed";
    }

    public void Fail(string error)
    {
        State = DownloadState.Failed;
        StatusMessage = "Error";
        ErrorMessage = error;
    }

    public void Stop()
    {
        State = DownloadState.Stopped;
        StatusMessage = "Stopped";
    }
}
