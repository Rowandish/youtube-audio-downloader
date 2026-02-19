# 🎧 YouTube Audio Downloader

A modern **WPF desktop app** for downloading audio from YouTube URLs as MP3 files, powered by `yt-dlp` + `ffmpeg`.

## ✨ Highlights

- ✅ Clean desktop UI with real-time progress
- 🚀 Parallel downloads (up to 3 at once)
- 🎚️ Audio quality presets: High, Medium, Low
- 📁 Custom output folder selection
- ⏱️ Batch progress, processed counters, elapsed time
- 🛑 Stop/cancel active queue safely
- 🧾 Error panel with per-item failure details

## 🧱 Tech Stack

- `.NET 10` (`net10.0-windows`)
- `WPF`
- `MVVM` pattern (ViewModels + command abstractions)
- External tools: `yt-dlp`, `ffmpeg`

## 📦 Requirements

- Windows
- .NET 10 SDK
- `yt-dlp` installed and available in `PATH`
- `ffmpeg` installed and available in `PATH`

## 🚀 Quick Start

```bash
git clone <your-repo-url>
cd YoutubeAudioDownloader
```

```bash
dotnet restore
dotnet run
```

## 🛠️ How To Use

1. Paste one or more YouTube URLs (supports separators/new lines).
2. Click **Add URL**.
3. Choose output folder and quality preset.
4. Click **Start Downloads**.
5. Monitor per-item and overall progress.
6. Use **Stop** to cancel the current batch.

## 🗂️ Project Structure

- `MainWindow.xaml` / `MainWindow.xaml.cs`: UI and main window bootstrap
- `ViewModels/MainWindowViewModel.cs`: download workflow orchestration
- `Services/YtDlpDownloadService.cs`: process execution + output parsing
- `Models/*`: domain models and enums
- `Infrastructure/*`: observable base class and command helpers

## ⚠️ Notes

- This app currently validates YouTube domains (`youtube.com`, `youtu.be`).
- UI status strings are currently in Italian; documentation is in English.
- Use responsibly and respect content rights and platform terms.

## 📄 License

No license file is included yet. Add one (for example MIT) before public reuse.
