using Avalonia.Controls;
using Avalonia.Interactivity;
using PictureBook.UI;

namespace PictureBook.UI.Views;

public partial class FilmView : UserControl
{
    public FilmView()
    {
        InitializeComponent();
    }

    private FilmViewModel? Vm => DataContext as FilmViewModel;

    /// <summary>胶片条缩略图按 96px 加载。</summary>
    private void OnStripAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (Vm is null || sender is not Border { DataContext: PhotoTile tile })
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => tile.BeginLoad(Vm.Library.Cache, 96), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnStripDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // 不卸载：胶片条只有 64px 小图，字节极小，保留即可秒回
    }

    private void OnStripTapped(object? sender, RoutedEventArgs e)
    {
        if ((sender as Border)?.DataContext is PhotoTile tile)
        {
            Vm?.Select(tile);
        }
    }
}
