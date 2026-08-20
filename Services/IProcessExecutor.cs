using System.Diagnostics;

namespace YoutubeAudioDownloader.Services;

internal interface IProcessExecutor
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<ProcessOutput> progress,
        CancellationToken cancellationToken);
}
