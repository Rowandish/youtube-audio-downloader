using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Services;

public sealed class YtDlpAudioDownloadExecutor : IAudioDownloadExecutor
{
    private readonly YtDlpDownloadService _downloadService = new();

    public async Task<AudioDownloadResult> ExecuteAsync(
        AudioDownloadRequest request,
        IProgress<AudioDownloadUpdate> updates,
        CancellationToken cancellationToken)
    {
        IProgress<double> progress = new ForwardingProgress<double>(value =>
            updates.Report(new DownloadProgressUpdate(value)));
        IProgress<string> status = new ForwardingProgress<string>(message =>
            updates.Report(new DownloadStatusUpdate(message)));

        try
        {
            await _downloadService.DownloadAudioAsync(
                request.Url,
                request.OutputDirectory,
                request.Quality,
                progress,
                status,
                cancellationToken);

            return AudioDownloadResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AudioDownloadResult.Failure(ex.Message);
        }
    }

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
