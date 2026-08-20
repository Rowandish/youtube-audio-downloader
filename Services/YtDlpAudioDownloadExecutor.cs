using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Services;

public sealed class YtDlpAudioDownloadExecutor : IAudioDownloadExecutor
{
    private static readonly Regex PercentRegex = new(
        @"(?<value>\d{1,3}(?:\.\d+)?)%",
        RegexOptions.Compiled);
    private readonly IProcessExecutor _processExecutor;
    private readonly Lazy<Task<string?>> _ytDlpVersion;

    public YtDlpAudioDownloadExecutor()
        : this(new SystemProcessExecutor())
    {
    }

    internal YtDlpAudioDownloadExecutor(IProcessExecutor processExecutor)
    {
        _processExecutor = processExecutor;
        _ytDlpVersion = new Lazy<Task<string?>>(GetYtDlpVersionAsync);
    }

    public async Task<AudioDownloadResult> ExecuteAsync(
        AudioDownloadRequest request,
        IProgress<AudioDownloadUpdate> updates,
        CancellationToken cancellationToken)
    {
        try
        {
            var ytDlpVersion = await _ytDlpVersion.Value
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            updates.Report(new DownloadStatusUpdate(string.IsNullOrWhiteSpace(ytDlpVersion)
                ? "Unable to determine yt-dlp version."
                : $"Using yt-dlp version {ytDlpVersion}."));

            var errors = new ConcurrentQueue<string>();
            var output = new ForwardingProgress<ProcessOutput>(processOutput =>
                HandleOutput(processOutput, updates, errors));
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _processExecutor.ExecuteAsync(
                BuildProcessStartInfo(request),
                output,
                cancellationToken).ConfigureAwait(false);

            if (!result.Started)
            {
                return AudioDownloadResult.Failure("Unable to start yt-dlp.");
            }

            if (result.ExitCode != 0)
            {
                var latestError = errors.LastOrDefault();
                return AudioDownloadResult.Failure(string.IsNullOrWhiteSpace(latestError)
                    ? $"yt-dlp exited with code {result.ExitCode}."
                    : FormatError(latestError, ytDlpVersion));
            }

            updates.Report(new DownloadProgressUpdate(100d));
            return AudioDownloadResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            return AudioDownloadResult.Failure(
                "yt-dlp not found. Install yt-dlp and ffmpeg and make sure they are in PATH.");
        }
        catch (Exception ex)
        {
            return AudioDownloadResult.Failure(ex.Message);
        }
    }

    private async Task<string?> GetYtDlpVersionAsync()
    {
        var processStartInfo = CreateProcessStartInfo();
        processStartInfo.ArgumentList.Add("--version");
        var outputLines = new ConcurrentQueue<string>();
        var output = new ForwardingProgress<ProcessOutput>(processOutput =>
        {
            if (!processOutput.IsError)
            {
                outputLines.Enqueue(processOutput.Text);
            }
        });

        try
        {
            var result = await _processExecutor.ExecuteAsync(
                processStartInfo,
                output,
                CancellationToken.None).ConfigureAwait(false);

            if (!result.Started || result.ExitCode != 0)
            {
                return null;
            }

            var version = string.Join(Environment.NewLine, outputLines).Trim();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(AudioDownloadRequest request)
    {
        var processStartInfo = CreateProcessStartInfo();
        processStartInfo.ArgumentList.Add("--newline");
        processStartInfo.ArgumentList.Add("--no-playlist");
        processStartInfo.ArgumentList.Add("--extractor-args");
        processStartInfo.ArgumentList.Add("youtube:player_client=web_embedded,android_vr");
        processStartInfo.ArgumentList.Add("-x");
        processStartInfo.ArgumentList.Add("--audio-format");
        processStartInfo.ArgumentList.Add("mp3");
        processStartInfo.ArgumentList.Add("--audio-quality");
        processStartInfo.ArgumentList.Add(MapQuality(request.Quality));
        processStartInfo.ArgumentList.Add("--embed-metadata");
        processStartInfo.ArgumentList.Add("-o");
        processStartInfo.ArgumentList.Add(Path.Combine(
            request.OutputDirectory,
            "%(title)s.%(ext)s"));
        processStartInfo.ArgumentList.Add(request.Url);

        return processStartInfo;
    }

    private static ProcessStartInfo CreateProcessStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static void HandleOutput(
        ProcessOutput processOutput,
        IProgress<AudioDownloadUpdate> updates,
        ConcurrentQueue<string> errors)
    {
        if (string.IsNullOrWhiteSpace(processOutput.Text))
        {
            return;
        }

        var line = processOutput.Text.Trim();

        if (TryParseProgress(line, out var value))
        {
            updates.Report(new DownloadProgressUpdate(value));
        }

        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Enqueue(line);
        }

        if (line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("[ffmpeg]", StringComparison.OrdinalIgnoreCase))
        {
            updates.Report(new DownloadStatusUpdate(line));
        }
    }

    private static bool TryParseProgress(string line, out double value)
    {
        var match = PercentRegex.Match(line);

        if (match.Success &&
            double.TryParse(
                match.Groups["value"].Value,
                CultureInfo.InvariantCulture,
                out var parsedValue))
        {
            value = Math.Clamp(parsedValue, 0d, 100d);
            return true;
        }

        value = 0d;
        return false;
    }

    private static string MapQuality(AudioQuality quality)
    {
        return quality switch
        {
            AudioQuality.High => "0",
            AudioQuality.Medium => "5",
            AudioQuality.Low => "9",
            _ => "0"
        };
    }

    private static string FormatError(string error, string? ytDlpVersion)
    {
        if (!error.Contains("403", StringComparison.Ordinal))
        {
            return error;
        }

        var version = string.IsNullOrWhiteSpace(ytDlpVersion) ? "unavailable" : ytDlpVersion;

        return $"{error}{Environment.NewLine}" +
               $"yt-dlp version: {version}.{Environment.NewLine}" +
               "YouTube denied this request (HTTP 403). First, update yt-dlp with: yt-dlp -U. " +
               "If it is already current, check authentication, cookies, or a temporary IP restriction.";
    }

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
