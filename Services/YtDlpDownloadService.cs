using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using YoutubeAudioDownloader.Models;

namespace YoutubeAudioDownloader.Services;

public sealed class YtDlpDownloadService
{
    private static readonly Regex PercentRegex = new(@"(?<value>\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled);

    public async Task DownloadAudioAsync(
        string url,
        string outputDirectory,
        AudioQuality quality,
        IProgress<double> progress,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var processStartInfo = BuildProcessStartInfo(url, outputDirectory, quality);

        using var process = new Process
        {
            StartInfo = processStartInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start yt-dlp.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "yt-dlp not found. Install yt-dlp and ffmpeg and make sure they are in PATH.",
                ex);
        }

        using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));

        var errors = new ConcurrentQueue<string>();

        var outputTask = ReadLinesAsync(process.StandardOutput, progress, status, errors, cancellationToken);
        var errorTask = ReadLinesAsync(process.StandardError, progress, status, errors, cancellationToken);
        var waitTask = process.WaitForExitAsync();

        await Task.WhenAll(outputTask, errorTask, waitTask).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var latestError = errors.LastOrDefault();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(latestError)
                    ? $"yt-dlp exited with code {process.ExitCode}."
                    : latestError);
        }

        progress.Report(100d);
    }

    private static ProcessStartInfo BuildProcessStartInfo(string url, string outputDirectory, AudioQuality quality)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("--newline");
        processStartInfo.ArgumentList.Add("--no-playlist");
        processStartInfo.ArgumentList.Add("-x");
        processStartInfo.ArgumentList.Add("--audio-format");
        processStartInfo.ArgumentList.Add("mp3");
        processStartInfo.ArgumentList.Add("--audio-quality");
        processStartInfo.ArgumentList.Add(MapQuality(quality));
        processStartInfo.ArgumentList.Add("-o");
        processStartInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(title)s.%(ext)s"));
        processStartInfo.ArgumentList.Add(url);

        return processStartInfo;
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        IProgress<double> progress,
        IProgress<string>? status,
        ConcurrentQueue<string> errors,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmedLine = line.Trim();

            if (TryParseProgress(trimmedLine, out var value))
            {
                progress.Report(value);
            }

            if (trimmedLine.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                errors.Enqueue(trimmedLine);
            }

            if (trimmedLine.StartsWith("[download]", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("[ffmpeg]", StringComparison.OrdinalIgnoreCase))
            {
                status?.Report(trimmedLine);
            }
        }
    }

    private static bool TryParseProgress(string line, out double value)
    {
        var match = PercentRegex.Match(line);

        if (match.Success &&
            double.TryParse(match.Groups["value"].Value, CultureInfo.InvariantCulture, out var parsedValue))
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
