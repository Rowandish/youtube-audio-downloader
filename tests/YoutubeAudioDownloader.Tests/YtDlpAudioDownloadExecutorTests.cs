using System.ComponentModel;
using System.Diagnostics;
using YoutubeAudioDownloader.Models;
using YoutubeAudioDownloader.Services;
using Xunit;

namespace YoutubeAudioDownloader.Tests;

public sealed class YtDlpAudioDownloadExecutorTests
{
    private const string Url = "https://www.youtube.com/watch?v=RuZIpHrXopk";
    private const string OutputDirectory = "C:\\Downloads";
    private const string Version = "2026.08.12";

    [Theory]
    [InlineData(AudioQuality.High, "0")]
    [InlineData(AudioQuality.Medium, "5")]
    [InlineData(AudioQuality.Low, "9")]
    public async Task ExecuteAsync_ShouldBuildExistingCommandArguments_ForQuality(
        AudioQuality quality,
        string expectedQuality)
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.EnqueueSuccess();
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            new AudioDownloadRequest(Url, OutputDirectory, quality),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var invocation = Assert.Single(processExecutor.Invocations.Skip(1));
        Assert.Equal("yt-dlp", invocation.FileName);
        Assert.True(invocation.RedirectStandardOutput);
        Assert.True(invocation.RedirectStandardError);
        Assert.False(invocation.UseShellExecute);
        Assert.True(invocation.CreateNoWindow);
        Assert.Equal(
            [
                "--newline",
                "--no-playlist",
                "--extractor-args",
                "youtube:player_client=web_embedded,android_vr",
                "-x",
                "--audio-format",
                "mp3",
                "--audio-quality",
                expectedQuality,
                "--embed-metadata",
                "-o",
                Path.Combine(OutputDirectory, "%(title)s.%(ext)s"),
                Url
            ],
            invocation.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCacheVersionLookup_AcrossTwoCalls()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.EnqueueSuccess();
        processExecutor.EnqueueSuccess();
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);
        await executor.ExecuteAsync(
            CreateRequest(AudioQuality.Low),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.Equal(3, processExecutor.Invocations.Count);
        Assert.Equal(["--version"], processExecutor.Invocations[0].Arguments);
        Assert.Equal(1, processExecutor.Invocations.Count(invocation =>
            invocation.Arguments.SequenceEqual(["--version"])));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPublishProgressAndStatus_FromStandardOutputAndError()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.EnqueueSuccess(
            new ProcessOutput("  [download] 12.5% of 3.00MiB", false),
            new ProcessOutput("[download] 48.25% of 3.00MiB", true),
            new ProcessOutput("[ExtractAudio] Destination: track.mp3", true),
            new ProcessOutput("[ffmpeg] Correcting container", false));
        var updates = new RecordingProgress<AudioDownloadUpdate>();
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            updates,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            [12.5d, 48.25d, 100d],
            updates.Values.OfType<DownloadProgressUpdate>().Select(update => update.Value));
        Assert.Equal(
            [
                $"Using yt-dlp version {Version}.",
                "[download] 12.5% of 3.00MiB",
                "[download] 48.25% of 3.00MiB",
                "[ExtractAudio] Destination: track.mp3",
                "[ffmpeg] Correcting container"
            ],
            updates.Values.OfType<DownloadStatusUpdate>().Select(update => update.Message));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnLatestError_WhenProcessExitsNonzero()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.Enqueue(
            new ProcessExecutionResult(true, 1),
            new ProcessOutput("ERROR: first failure", false),
            new ProcessOutput("ERROR: final failure", true));
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.Equal(AudioDownloadResult.Failure("ERROR: final failure"), result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnExitCodeFallback_WhenProcessHasNoErrorLine()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.Enqueue(new ProcessExecutionResult(true, 7));
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.Equal(AudioDownloadResult.Failure("yt-dlp exited with code 7."), result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnVersionGuidance_WhenErrorContainsHttp403()
    {
        const string error = "ERROR: unable to download video data: HTTP Error 403: Forbidden";
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.Enqueue(
            new ProcessExecutionResult(true, 1),
            new ProcessOutput(error, true));
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.Equal(
            error + Environment.NewLine +
            $"yt-dlp version: {Version}." + Environment.NewLine +
            "YouTube denied this request (HTTP 403). First, update yt-dlp with: yt-dlp -U. " +
            "If it is already current, check authentication, cookies, or a temporary IP restriction.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnUnableToStart_WhenProcessDoesNotStart()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.Enqueue(new ProcessExecutionResult(false, -1));
        processExecutor.Enqueue(new ProcessExecutionResult(false, -1));
        var updates = new RecordingProgress<AudioDownloadUpdate>();
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            updates,
            CancellationToken.None);

        Assert.Equal(AudioDownloadResult.Failure("Unable to start yt-dlp."), result);
        Assert.Contains(
            new DownloadStatusUpdate("Unable to determine yt-dlp version."),
            updates.Values);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnMissingToolMessage_WhenProcessStartThrowsWin32Exception()
    {
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.Enqueue((_, _, _) => throw new Win32Exception(2));
        processExecutor.Enqueue((_, _, _) => throw new Win32Exception(2));
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var result = await executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            CancellationToken.None);

        Assert.Equal(
            AudioDownloadResult.Failure(
                "yt-dlp not found. Install yt-dlp and ffmpeg and make sure they are in PATH."),
            result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateCancellation_FromProcessExecutor()
    {
        using var cancellationSource = new CancellationTokenSource();
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.EnqueueSuccess(new ProcessOutput(Version, false));
        processExecutor.Enqueue((_, _, cancellationToken) =>
        {
            cancellationSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProcessExecutionResult(true, 0));
        });
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(
                CreateRequest(AudioQuality.High),
                new RecordingProgress<AudioDownloadUpdate>(),
                cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(cancellationSource.Token, processExecutor.Invocations[1].CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelBeforeDownload_WhenVersionLookupIsPending()
    {
        using var cancellationSource = new CancellationTokenSource();
        var versionCompletion = new TaskCompletionSource<ProcessExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var processExecutor = new ScriptedProcessExecutor();
        processExecutor.Enqueue((_, _, _) => versionCompletion.Task);
        processExecutor.EnqueueSuccess();
        var executor = new YtDlpAudioDownloadExecutor(processExecutor);

        var execution = executor.ExecuteAsync(
            CreateRequest(AudioQuality.High),
            new RecordingProgress<AudioDownloadUpdate>(),
            cancellationSource.Token);
        Assert.Single(processExecutor.Invocations);

        cancellationSource.Cancel();

        try
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                execution.WaitAsync(TimeSpan.FromSeconds(1)));

            Assert.Equal(cancellationSource.Token, exception.CancellationToken);
            Assert.Single(processExecutor.Invocations);
        }
        finally
        {
            versionCompletion.TrySetResult(new ProcessExecutionResult(true, 0));

            try
            {
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // Expected once the caller cancellation participates in the version wait.
            }
        }
    }

    [Fact]
    public async Task SystemProcessExecutor_ShouldObservePreCanceledToken_BeforeProcessStart()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var startInfo = new ProcessStartInfo
        {
            FileName = $"youtube-audio-downloader-nonexistent-{Guid.NewGuid():N}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var executor = new SystemProcessExecutor();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(
                startInfo,
                new RecordingProgress<ProcessOutput>(),
                cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    private static AudioDownloadRequest CreateRequest(AudioQuality quality)
    {
        return new AudioDownloadRequest(Url, OutputDirectory, quality);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value)
        {
            Values.Add(value);
        }
    }

    private sealed class ScriptedProcessExecutor : IProcessExecutor
    {
        private readonly Queue<ProcessScript> _scripts = [];

        public List<ProcessInvocation> Invocations { get; } = [];

        public void Enqueue(ProcessExecutionResult result, params ProcessOutput[] outputs)
        {
            Enqueue((_, progress, _) =>
            {
                foreach (var output in outputs)
                {
                    progress.Report(output);
                }

                return Task.FromResult(result);
            });
        }

        public void Enqueue(ProcessScript script)
        {
            _scripts.Enqueue(script);
        }

        public void EnqueueSuccess(params ProcessOutput[] outputs)
        {
            Enqueue(new ProcessExecutionResult(true, 0), outputs);
        }

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessStartInfo startInfo,
            IProgress<ProcessOutput> progress,
            CancellationToken cancellationToken)
        {
            Invocations.Add(new ProcessInvocation(
                startInfo.FileName,
                startInfo.ArgumentList.ToArray(),
                startInfo.RedirectStandardOutput,
                startInfo.RedirectStandardError,
                startInfo.UseShellExecute,
                startInfo.CreateNoWindow,
                cancellationToken));

            return _scripts.Dequeue()(startInfo, progress, cancellationToken);
        }
    }

    private delegate Task<ProcessExecutionResult> ProcessScript(
        ProcessStartInfo startInfo,
        IProgress<ProcessOutput> progress,
        CancellationToken cancellationToken);

    private sealed record ProcessInvocation(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool RedirectStandardOutput,
        bool RedirectStandardError,
        bool UseShellExecute,
        bool CreateNoWindow,
        CancellationToken CancellationToken);
}
