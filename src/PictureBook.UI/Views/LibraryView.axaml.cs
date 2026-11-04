using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PictureBook.Core.Library;
using PictureBook.UI;

namespace PictureBook.UI.Views;

public partial class LibraryView : UserControl
{
    private DispatcherTimer? _sizeDebounce;
    private bool _flagGuard;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private LibraryViewModel? Vm => DataContext as LibraryViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (this.FindControl<ScrollViewer>("GridScroller") is { } scroller)
        {
            scroller.SizeChanged -= OnScrollerSizeChanged;
            scroller.SizeChanged += OnScrollerSizeChanged;
        }

        if (Vm is not null)
        {
            Vm.PropertyChanged -= OnVmPropertyChanged;
            Vm.PropertyChanged += OnVmPropertyChanged;
            SyncFlagRadios();
        }
    }

    /// <summary>选中照片变化时，同步右侧旗标按钮的选中态。</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryViewModel.SelectedTile) or nameof(LibraryViewModel.HasSelection))
        {
            SyncFlagRadios();
        }
    }

    private void SyncFlagRadios()
    {
        var flag = Vm?.SelectedTile?.Photo.Flag ?? PhotoFlag.None;
        _flagGuard = true;
        if (this.FindControl<RadioButton>("FlagPick") is { } pick)
        {
            pick.IsChecked = flag == PhotoFlag.Pick;
        }

        if (this.FindControl<RadioButton>("FlagNone") is { } none)
        {
            none.IsChecked = flag == PhotoFlag.None;
        }

        if (this.FindControl<RadioButton>("FlagReject") is { } reject)
        {
            reject.IsChecked = flag == PhotoFlag.Reject;
        }

        _flagGuard = false;
    }

    private void OnFlagPickChecked(object? sender, RoutedEventArgs e) => ApplyFlag(PhotoFlag.Pick);
    private void OnFlagNoneChecked(object? sender, RoutedEventArgs e) => ApplyFlag(PhotoFlag.None);
    private void OnFlagRejectChecked(object? sender, RoutedEventArgs e) => ApplyFlag(PhotoFlag.Reject);

    private void ApplyFlag(PhotoFlag flag)
    {
        if (!_flagGuard && Vm?.SelectedTile is not null)
        {
            Vm.SetSelectedFlag(flag);
        }
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Vm?.UpdateViewportWidth(e.NewSize.Width);
    }

    /// <summary>瓦片进入视口：按当前尺寸开始加载缩略图。</summary>
    private void OnTileAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var vm = Vm;
        if (vm is null || sender is not Border { DataContext: PhotoTile tile })
        {
            return;
        }

        Dispatcher.UIThread.Post(() => tile.BeginLoad(vm.Cache, vm.TileSize), DispatcherPriority.Background);
    }

    /// <summary>瓦片滚出视口：释放位图，字节留在缓存 LRU。</summary>
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

    private void OnFilterChecked(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is RadioButton { DataContext: LibraryViewModel.FilterItem item } &&
            item.Value != Vm.Filter)
        {
            Vm.Filter = item.Value; // VM 会回写所有 IsSelected，RadioButton 随之联动
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

    /// <summary>点击瓦片 = 全局选中（右侧信息面板联动；旗标在右栏打）。</summary>
    private void OnTileTapped(object? sender, RoutedEventArgs e)
    {
        if ((sender as Border)?.DataContext is PhotoTile tile)
        {
            Vm?.Select(tile);
        }
    }

    /// <summary>点击瓦片左上角圆圈 = 勾选/取消进流转集合（不改变选中态）。</summary>
    private void OnFlowCheckTapped(object? sender, RoutedEventArgs e)
    {
        if ((sender as Border)?.DataContext is PhotoTile tile)
        {
            Vm?.ToggleFlow(tile);
            e.Handled = true; // 阻止冒泡到瓦片的选中点击
        }
    }

    private void OnClearFlowClick(object? sender, RoutedEventArgs e) => Vm?.ClearFlow();

    private void OnFlowToFilmClick(object? sender, RoutedEventArgs e) => Vm?.RequestFlow();
}
