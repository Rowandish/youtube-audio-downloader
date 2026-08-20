using System.Diagnostics;

namespace YoutubeAudioDownloader.Services;

internal sealed class SystemProcessExecutor : IProcessExecutor
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<ProcessOutput> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return new ProcessExecutionResult(false, -1);
        }

        using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));

        var outputTask = ReadLinesAsync(
            process.StandardOutput,
            isError: false,
            progress,
            cancellationToken);
        var errorTask = ReadLinesAsync(
            process.StandardError,
            isError: true,
            progress,
            cancellationToken);
        var waitTask = process.WaitForExitAsync();

        await Task.WhenAll(outputTask, errorTask, waitTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessExecutionResult(true, process.ExitCode);
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        bool isError,
        IProgress<ProcessOutput> progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                return;
            }

            progress.Report(new ProcessOutput(line, isError));
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup errors during cancellation.
        }
    }
}
