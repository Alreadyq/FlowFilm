using Avalonia.Controls;
using Avalonia.Threading;
using PictureBook.UI;

namespace PictureBook.UI.Views;

public partial class SheetView : UserControl
{
    public SheetView()
    {
        InitializeComponent();
    }

    private SheetViewModel? Vm => DataContext as SheetViewModel;

    /// <summary>帧缩略图按 160px 加载。</summary>
    private void OnFrameAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (Vm is null || sender is not Border { DataContext: PhotoTile tile })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => tile.BeginLoad(Vm.Library.Cache, 160), DispatcherPriority.Background);
    }

    private void OnFrameDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border { DataContext: PhotoTile tile })
        {
            tile.Unload();
        }
    }
}
