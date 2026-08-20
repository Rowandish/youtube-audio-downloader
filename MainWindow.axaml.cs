using Avalonia.Controls;
using YoutubeAudioDownloader.Services;
using YoutubeAudioDownloader.ViewModels;

namespace YoutubeAudioDownloader;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new(
        new DownloadBatch(new YtDlpAudioDownloadExecutor()));

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
