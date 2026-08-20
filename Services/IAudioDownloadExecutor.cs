using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Services;

public interface IAudioDownloadExecutor
{
    Task<AudioDownloadResult> ExecuteAsync(
        AudioDownloadRequest request,
        IProgress<AudioDownloadUpdate> updates,
        CancellationToken cancellationToken);
}
