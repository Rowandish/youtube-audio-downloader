namespace YoutubeAudioDownloader.Services;

internal sealed record ProcessOutput(string Text, bool IsError);

internal sealed record ProcessExecutionResult(bool Started, int ExitCode);
