using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PictureBook.UI;

namespace PictureBook.UI.Views;

public partial class LibraryView : UserControl
{
    private DispatcherTimer? _sizeDebounce;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private LibraryViewModel? Vm => DataContext as LibraryViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        var scroller = this.FindControl<ScrollViewer>("GridScroller");
        if (scroller is not null)
        {
            scroller.SizeChanged -= OnScrollerSizeChanged;
            scroller.SizeChanged += OnScrollerSizeChanged;
        }
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 列数随窗口宽度变化；RebuildRows 内部幂等
        Vm?.UpdateViewportWidth(e.NewSize.Width);
    }

    /// <summary>瓦片进入视口（虚拟化面板实现该行）：按当前尺寸开始加载缩略图。</summary>
    private void OnTileAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var vm = Vm;
        if (vm is null || sender is not Border { DataContext: PhotoTile tile })
        {
            return;
        }

        // 延到 Background 优先级，避免布局阶段做解码调度
        Dispatcher.UIThread.Post(() => tile.BeginLoad(vm.Cache, vm.TileSize), DispatcherPriority.Background);
    }

    /// <summary>瓦片滚出视口（该行被回收）：释放位图，字节留在缓存 LRU。</summary>
    private void OnTileDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border { DataContext: PhotoTile tile })
        {
            tile.Unload();
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        var vm = Vm;
        if (top is null || vm is null)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择要导入的照片文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }

        await vm.ImportAsync(folders[0].Path.LocalPath);
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && sender is ComboBox { SelectedItem: LibraryViewModel.FilterItem item })
        {
            Vm.Filter = item.Value;
        }
    }

    private void OnSizeValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // 拖动中只记值；停顿 250ms 后一次性生效，避免解码风暴
        _sizeDebounce?.Stop();
        _sizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _sizeDebounce.Tick += (_, _) =>
        {
            _sizeDebounce?.Stop();
            Vm?.ApplyTileSize();
        };
        _sizeDebounce.Start();
    }

    private void OnTileTapped(object? sender, RoutedEventArgs e)
    {
        if ((sender as Border)?.DataContext is PhotoTile tile)
        {
            tile.CycleFlag(); // 无 → P → X → 无，写库由 VM 回调完成
        }
    }
}
