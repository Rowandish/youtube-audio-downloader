# Download Architecture Deepening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deepen lifecycle, batch orchestration, and yt-dlp execution without changing visible behavior, UI layout, status text, or queue semantics.

**Architecture:** `DownloadItem` owns legal transitions; `DownloadBatch` owns observable download operations behind a typed executor; the yt-dlp executor owns policy behind a process seam.

**Tech Stack:** .NET 10, C#, Avalonia 11.2.3, xUnit 2.9.2, `yt-dlp`, `ffmpeg`.

**Spec:** `docs/superpowers/specs/2026-08-20-download-architecture-deepening-design.md`

## Global Constraints

- Final history has exactly three commits: lifecycle, batch orchestration, yt-dlp process seam.
- Preserve UI layout, all English strings, URL acceptance, queue behavior, and three concurrent attempts.
- Late-added items join the active batch; failed/stopped items remain eligible; terminal items count as 100 percent.
- Read directory and quality when each attempt begins. Tests use no installed `yt-dlp` or `ffmpeg`.
- Do not add event sourcing or immutable snapshots.

---

### Task 1: Deepen the download-item lifecycle

**Files:**
- Modify: `Models/DownloadItem.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Create: `tests/YoutubeAudioDownloader.Tests/DownloadItemTests.cs`
- Create: `CONTEXT.md`, `docs/superpowers/specs/2026-08-20-download-architecture-deepening-design.md`, `docs/superpowers/plans/2026-08-20-download-architecture-deepening.md`

**Interfaces:**
- Produces: `Queue()`, `Start()`, `ReportProgress(double)`, `ReportStatus(string)`, `Complete()`, `Fail(string)`, `Stop()` on `DownloadItem`.
- Produces public read-only `Progress`, `State`, `StatusMessage`, `ErrorMessage`, `ShowProgress`; preserves constructor and `Url`.

- [ ] **Step 1: Write failing lifecycle tests**

```csharp
[Fact]
public void Queue_ShouldResetRetryableItem()
{
    var item = new DownloadItem("https://youtu.be/example");
    item.Fail("old error");
    item.Queue();
    Assert.Equal(DownloadState.Queued, item.State);
    Assert.Equal(0d, item.Progress);
    Assert.Equal("Queued", item.StatusMessage);
    Assert.Empty(item.ErrorMessage);
    Assert.True(item.ShowProgress);
}

[Fact]
public void Complete_ShouldSetTerminalStateAndHideProgress()
{
    var item = new DownloadItem("https://youtu.be/example");
    item.Queue(); item.Start(); item.ReportProgress(60d); item.Complete();
    Assert.Equal(DownloadState.Completed, item.State);
    Assert.Equal(100d, item.Progress);
    Assert.Equal("Completed", item.StatusMessage);
    Assert.False(item.ShowProgress);
}
```

Add real-item tests for `Start`, clamping, `ReportStatus`, `Fail`, `Stop`, and the downloading-at-100 extraction rule.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadItemTests`.

Expected: CS1061 because lifecycle methods do not yet exist.

- [ ] **Step 3: Implement lifecycle bundles**

```csharp
public void Queue() { Progress = 0d; State = DownloadState.Queued; StatusMessage = "Queued"; ErrorMessage = string.Empty; }
public void Start() { State = DownloadState.Downloading; StatusMessage = "Downloading"; }
public void ReportProgress(double value) => Progress = value;
public void ReportStatus(string message) => StatusMessage = message;
public void Complete() { Progress = 100d; State = DownloadState.Completed; StatusMessage = "Completed"; }
public void Fail(string error) { State = DownloadState.Failed; StatusMessage = "Error"; ErrorMessage = error; }
public void Stop() { State = DownloadState.Stopped; StatusMessage = "Stopped"; }
```

Make setters private, retain clamping/visibility notifications, and replace ViewModel assignments; queued cancellation must call `Stop()` then `ReportProgress(0d)`.

- [ ] **Step 4: Verify GREEN and amend**

Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadItemTests` and `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore`; expected pass, with only existing `NU1903` reported. Then run `git add CONTEXT.md docs/superpowers/specs/2026-08-20-download-architecture-deepening-design.md docs/superpowers/plans/2026-08-20-download-architecture-deepening.md Models/DownloadItem.cs ViewModels/MainWindowViewModel.cs tests/YoutubeAudioDownloader.Tests/DownloadItemTests.cs; git commit --amend --no-edit`.

Expected commit subject: `Deepen download item lifecycle`.

---

### Task 2: Extract deep batch-download orchestration

**Files:**
- Create: `Models/AudioDownloadExecution.cs`, `Services/IAudioDownloadExecutor.cs`, `Services/YtDlpAudioDownloadExecutor.cs`, `Services/DownloadBatch.cs`, `tests/YoutubeAudioDownloader.Tests/DownloadBatchTests.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`, `MainWindow.axaml.cs`

**Interfaces:**
- Consumes only Task 1 transition methods.
- Produces `AudioDownloadRequest(string Url, string OutputDirectory, AudioQuality Quality)`, abstract `AudioDownloadUpdate`, `DownloadProgressUpdate(double Value)`, `DownloadStatusUpdate(string Message)`, and `AudioDownloadResult(bool Succeeded, string? ErrorMessage)` with `Success()`/`Failure(string)`.
- Produces `IAudioDownloadExecutor.ExecuteAsync(AudioDownloadRequest, IProgress<AudioDownloadUpdate>, CancellationToken)`.
- Produces `DownloadBatch(IAudioDownloadExecutor)` with observable `Items`, `Errors`, `ProcessedCount`, `TotalCount`, `OverallProgress`, `StatusMessage`, `IsDownloading`; `OutputDirectory`, `SelectedQuality`; and `AddUrls`, `Remove`, `StartAsync`, `Stop`, `ClearErrors`.
- Task 3 preserves these types and signature unchanged.

- [ ] **Step 1: Write failing batch tests**

Create a test-local executor with recorded requests and controllable `TaskCompletionSource<AudioDownloadResult>` attempts; report typed updates and cancel attempts from supplied tokens. Test maximum three starts, failure continuation, late additions, active/queued cancellation, retry, counts, terminal progress, settings captured at start, invalid URL, and case-insensitive duplicates. Assert `Invalid URL: {url}`, `Added {n} URL(s).`, `No URLs added.`, `Downloads started.`, `Downloads completed.`, `Completed with errors.`, `Stopping downloads...`, and `Downloads stopped.`.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadBatchTests`.

Expected: compilation failure for missing batch and typed executor seam.

- [ ] **Step 3: Implement seam and batch**

Wrap the old service in `YtDlpAudioDownloadExecutor`, map primitive progress/status to typed updates, rethrow cancellation, and map other exceptions to failure results. Move intake, validation, active-batch membership, three-at-a-time scheduling, cancellation, statuses, errors, counts, and aggregate progress into `DownloadBatch`.

```csharp
item.Start();
var request = new AudioDownloadRequest(item.Url, OutputDirectory, SelectedQuality);
var result = await _executor.ExecuteAsync(request, updates, cancellationToken);
if (result.Succeeded) item.Complete(); else item.Fail(result.ErrorMessage!);
```

Cancellation is stopped, not error. Failed results log the existing timestamped line and other attempts continue. Queued leftovers stop then report zero. Terminal states are 100 percent.

- [ ] **Step 4: Adapt ViewModel, verify GREEN, commit**

Keep ViewModel input, selection, commands, folder dialog, timer, and existing binding names; forward `Items`, `Errors`, directory, quality, counters, progress, status, downloading/idle state to a `DownloadBatch`. Dispose once in `MainWindow.axaml.cs`. Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~DownloadBatchTests` and `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore`; expected pass with existing `NU1903` warning only. Then run `git add Models/AudioDownloadExecution.cs Services/IAudioDownloadExecutor.cs Services/YtDlpAudioDownloadExecutor.cs Services/DownloadBatch.cs ViewModels/MainWindowViewModel.cs MainWindow.axaml.cs tests/YoutubeAudioDownloader.Tests/DownloadBatchTests.cs; git commit -m "Extract deep batch-download orchestration"`.

Expected commit subject: `Extract deep batch-download orchestration`.

---

### Task 3: Deepen yt-dlp execution at an internal process seam

**Files:**
- Create: `Services/IProcessExecutor.cs`, `Services/SystemProcessExecutor.cs`, `Services/ProcessExecution.cs`, `tests/YoutubeAudioDownloader.Tests/YtDlpAudioDownloadExecutorTests.cs`
- Modify: `Services/YtDlpAudioDownloadExecutor.cs`
- Delete: old yt-dlp service/formatter sources and their tests.

**Interfaces:**
- Consumes Task 2 `IAudioDownloadExecutor` and typed records unchanged.
- Produces `ProcessOutput(string Text, bool IsError)`, `ProcessExecutionResult(bool Started, int ExitCode)`, and `IProcessExecutor.ExecuteAsync(ProcessStartInfo, IProgress<ProcessOutput>, CancellationToken)`.
- `SystemProcessExecutor` alone creates processes, reads streams, waits, and kills on cancellation.
- `YtDlpAudioDownloadExecutor` retains parameterless production constructor plus `YtDlpAudioDownloadExecutor(IProcessExecutor)`.

- [ ] **Step 1: Write failing scripted-process tests**

Scripted adapter tests must capture commands without reflection and verify high/medium/low mappings `0`/`5`/`9`, cached version lookup across two calls, stdout/stderr progress/status, latest error/exit fallback, 403 version guidance, failed start, and cancellation propagation.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~YtDlpAudioDownloadExecutorTests`.

Expected: compile failure for missing process seam/injected constructor.

- [ ] **Step 3: Implement seam and yt-dlp policy**

`SystemProcessExecutor` reports every stream line, returns `(true, ExitCode)` after both streams, returns `(false, -1)` on start failure, and kills before cancellation propagates. Executor caches `yt-dlp --version`; builds exact existing arguments (`--newline`, `--no-playlist`, web-embedded/android-vr extractor args, extraction/mp3/quality/metadata/output/URL); publishes typed progress/status; collects errors; returns existing missing-tool/start/nonzero/403 messages; reports 100 on success; propagates cancellation. Delete the old service and formatter.

- [ ] **Step 4: Verify GREEN and commit**

Replace old reflection/formatter tests with scripted seam tests. Run `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore --filter FullyQualifiedName~YtDlpAudioDownloadExecutorTests` and `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore`; expected pass without external tools and only existing `NU1903`. Then run `git add Services/IProcessExecutor.cs Services/SystemProcessExecutor.cs Services/ProcessExecution.cs Services/YtDlpAudioDownloadExecutor.cs tests/YoutubeAudioDownloader.Tests/YtDlpAudioDownloadExecutorTests.cs; git rm Services/YtDlpDownloadService.cs Services/YtDlpErrorMessageFormatter.cs tests/YoutubeAudioDownloader.Tests/YtDlpDownloadServiceTests.cs tests/YoutubeAudioDownloader.Tests/YtDlpErrorMessageFormatterTests.cs; git commit -m "Deepen yt-dlp execution at an internal process seam"`.

Expected commit subject: `Deepen yt-dlp execution at an internal process seam`.

---

## Final Verification

- [ ] Run full `dotnet test tests\YoutubeAudioDownloader.Tests\YoutubeAudioDownloader.Tests.csproj --no-restore`, `dotnet build --no-restore`, `git log --oneline -3`, `git diff --check`, and `git status --short`.
- [ ] Expected: test/build pass; the three ordered commit subjects match this plan; no whitespace errors/unintended changes; report, do not change, existing `NU1903`.
