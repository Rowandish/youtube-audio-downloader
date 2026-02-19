# Repository Guidelines

## Project Structure & Module Organization
This repository contains a .NET 10 WPF desktop app (`YoutubeAudioDownloader.csproj`). The codebase is organized by responsibility:
- `MainWindow.xaml` and `MainWindow.xaml.cs`: main UI and window bootstrap.
- `ViewModels/`: UI state and orchestration logic (`MainWindowViewModel.cs`).
- `Services/`: external process integration (`YtDlpDownloadService.cs`).
- `Models/`: domain entities and enums (`DownloadItem`, `DownloadState`, `AudioQuality`).
- `Infrastructure/`: MVVM plumbing (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`).
Build outputs are in `bin/` and `obj/` and must not be committed.

## Build, Test, and Development Commands
Run commands from the repository root:
- `dotnet restore`: restores NuGet dependencies.
- `dotnet build`: compiles the app and checks for compile errors.
- `dotnet run`: starts the WPF application locally.
- `dotnet publish -c Release`: creates release artifacts.
- `dotnet test`: runs tests (when a test project is added).

## Coding Style & Naming Conventions
Use C# conventions already present in the project:
- 4-space indentation, UTF-8, file-scoped namespaces.
- `PascalCase` for public types/members, `camelCase` for locals/parameters, `_camelCase` for private fields.
- Keep ViewModel logic in `ViewModels/`, process/tool logic in `Services/`, and avoid code-behind beyond UI wiring.
- Prefer short, explicit methods and clear status/error messages in English.

## Testing Guidelines
There is currently no test project. For new features, add tests under `tests/` (for example `tests/YoutubeAudioDownloader.Tests`).
- Suggested framework: xUnit.
- Test file naming: `<ClassName>Tests.cs`.
- Test method naming: `MethodName_ShouldExpectedBehavior_WhenCondition`.
Focus first on URL validation, queue state transitions, and error handling.

## Commit & Pull Request Guidelines
Follow the existing commit style: short, imperative, English messages (for example `Translate UI and status texts to English`).
- Keep commits focused and atomic.
- PRs should include: change summary, rationale, manual verification steps, and screenshots for UI changes.
- Link related issues when applicable.

## Security & Configuration Tips
This app depends on `yt-dlp` and `ffmpeg` in system `PATH`. Do not commit secrets or machine-specific paths. Validate external input and surface user-friendly errors.
