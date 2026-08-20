# Download Architecture Deepening Design

## Goal

Deepen the download-item lifecycle, batch orchestration, and yt-dlp execution modules while preserving visible behavior, UI layout, status text, and queue semantics.

The final history contains exactly three focused commits, ordered by dependency:

1. Deepen the download-item lifecycle.
2. Extract deep batch-download orchestration.
3. Deepen yt-dlp execution at an internal process seam.

`CONTEXT.md` and this design belong to the first commit so no documentation-only commit remains in the final history.

## Non-goals

- No UI redesign or new user-facing feature.
- No change to URL acceptance rules or displayed status/error text.
- No change to the maximum of three concurrent attempts.
- No requirement for yt-dlp or ffmpeg during automated tests.
- No event-sourcing or immutable-snapshot infrastructure.

## Domain language

- A **download item** is one requested YouTube-to-MP3 extraction and its lifecycle.
- A **download batch** is the set coordinated by one start operation, including items added while it remains active.
- A **download attempt** is one execution of an item through yt-dlp.

The canonical definitions live in `CONTEXT.md`.

## Architecture

### Download-item lifecycle module

`DownloadItem` remains observable for Avalonia binding, but callers cannot set lifecycle fields independently. Its interface expresses lifecycle intent: queue/reset, start, report progress/status, complete, fail, and stop. Its implementation owns legal state combinations, progress clamping, error clearing, terminal status, and progress-bar visibility.

This module is internal to batch behavior rather than a new external seam. Its public observable getters exist for presentation; lifecycle mutation stays local.

### Batch-download module

`DownloadBatch` owns:

- the item and error collections;
- URL parsing, validation, and duplicate rejection;
- selection of eligible items;
- the active batch, including late-added items;
- the three-at-a-time scheduling policy;
- cancellation and terminal-state handling;
- processed/total counts and aggregate progress;
- current output-directory and quality settings;
- batch status text.

Its small behavioral interface adds/removes URLs, starts/stops a batch, clears errors, and exposes observable state. The current output directory and quality are read when each attempt starts, preserving the existing behavior where queued items can observe settings changed during an active batch.

`MainWindowViewModel` becomes the Avalonia adapter. It retains text input, selected-row state, commands, the folder dialog, and the UI refresh timer for elapsed-time presentation. It delegates operational behavior to `DownloadBatch` and does not coordinate item transitions or scheduling.

The batch depends on an audio-download executor seam. The production yt-dlp adapter and a deterministic test adapter make this a real seam.

### yt-dlp execution module

The yt-dlp module accepts a download request and reports typed download updates. It returns a success/failure result for expected execution outcomes; cancellation continues to use `OperationCanceledException`.

Its implementation owns:

- yt-dlp version discovery and caching;
- argument construction and quality mapping;
- stdout/stderr interpretation;
- progress and display-status translation;
- process exit interpretation;
- HTTP 403 guidance and version context;
- cancellation semantics.

An internal process seam separates these policies from `System.Diagnostics.Process`. A production process adapter and a scripted transcript adapter used by tests make that seam real. The standalone error formatter is absorbed because its deletion test shows no leverage: it has one caller and only relocates a small conditional.

## Data flow

1. The ViewModel delegates raw URL text to the batch.
2. The batch parses, validates, de-duplicates, and creates download items.
3. Starting prepares eligible items and launches up to three attempts.
4. Each attempt reads the current directory and quality, then calls the audio-download executor.
5. Typed updates drive the item's progress and display status.
6. A success result completes the item; a failure result fails it and records the timestamped error.
7. Cancellation stops active attempts and marks still-queued items as stopped.
8. Every transition recalculates processed counts and aggregate progress using the current rule that terminal items contribute 100 percent.

## Error handling

- Invalid URLs remain item-intake errors with the current message.
- Missing or unusable output directories retain current status and error messages.
- Failure to start yt-dlp retains the installation/PATH guidance.
- Nonzero exits use the latest output line containing `ERROR`, from stdout or stderr, falling back to the exit code.
- HTTP 403 failures include the cached yt-dlp version and current update/authentication guidance.
- Cancellation is a stopped outcome, not an error.
- An unexpected failure is contained to its item so remaining attempts continue.

## Testing

### Commit 1: lifecycle

Add transition tests for queue/reset, start, progress clamping, completion, failure, stop, retry, and progress-bar visibility. Existing callers must use lifecycle intent rather than property setters.

### Commit 2: batch

Use a deterministic audio-download adapter to verify:

- maximum parallelism of three;
- continuation after one failure;
- late additions to an active batch;
- cancellation of active and queued work;
- retry eligibility and reset behavior;
- processed/total counts;
- aggregate-progress invariants;
- settings captured at each attempt start;
- URL validation and duplicate handling.

### Commit 3: yt-dlp

Use a scripted process adapter to verify:

- exact command construction and quality mapping without reflection;
- version lookup and caching;
- stdout/stderr progress and status interpretation;
- nonzero exit fallback;
- HTTP 403 version guidance;
- process-start failure;
- cancellation propagation.

The final verification runs the full test project and a clean build after all three commits.

## Compatibility constraints

- Existing XAML remains visually unchanged; binding paths may change only as needed to reach batch-owned state.
- Existing English UI/status/error strings remain byte-for-byte equivalent where observable.
- The queue accepts items added while downloading.
- Failed and stopped items remain eligible for a later start.
- Overall progress preserves the current terminal-item calculation.
- The repository ends with exactly three new commits for the three candidates.
