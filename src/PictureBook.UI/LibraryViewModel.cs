using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PictureBook.Core.Library;

namespace PictureBook.UI;

public enum LibraryFilter
{
    All,
    Picks,
    Rejects,
    Unflagged,
}

/// <summary>
/// 网格里的单张照片瓦片。缩略图按需加载：进入视口 BeginLoad，滚出视口 Unload 释放位图，
/// 字节仍留在 ThumbnailCache 的内存 LRU 里，回滚秒回。
/// </summary>
public sealed class PhotoTile : INotifyPropertyChanged
{
    private readonly Action<PhotoTile> _onFlagChanged;
    private IImage? _thumb;
    private bool _isLoading;
    private bool _isFailed;
    private int _loadedSize;
    private int _pixelSize = 176;
    private ThumbnailCache? _cache;
    private int _requestedSize;
    private CancellationTokenSource? _cts;

    public PhotoTile(Photo photo, Action<PhotoTile> onFlagChanged)
    {
        Photo = photo;
        _onFlagChanged = onFlagChanged;
    }

    public Photo Photo { get; }

    public string Title => Photo.FileName;

    public string InfoLine => string.IsNullOrEmpty(Photo.ExposureDisplay)
        ? Photo.CameraDisplay
        : Photo.ExposureDisplay;

    public string ExtBadge => Photo.IsRaw ? "RAW" : Photo.Extension.TrimStart('.').ToUpperInvariant();

    public string FlagText => Photo.Flag switch
    {
        PhotoFlag.Pick => "P",
        PhotoFlag.Reject => "X",
        _ => string.Empty,
    };

    public bool HasFlag => Photo.Flag != PhotoFlag.None;

    /// <summary>瓦片边长（正方形格子），由视图模型在重建行时设置。</summary>
    public int PixelSize
    {
        get => _pixelSize;
        set { _pixelSize = value; OnPropertyChanged(); }
    }

    public IImage? Thumb
    {
        get => _thumb;
        private set { _thumb = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowPlaceholder)); }
    }

    public bool ShowPlaceholder => Thumb is null;

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set { _isFailed = value; OnPropertyChanged(); }
    }

    /// <summary>请求加载缩略图。重复调用且尺寸未变时是幂等的。</summary>
    public void BeginLoad(ThumbnailCache cache, int size)
    {
        _cache = cache;
        if (Thumb is not null && _loadedSize == size)
        {
            return;
        }

        _requestedSize = size;
        RestartLoad();
    }

    /// <summary>滚出视口：取消在途解码并释放位图。</summary>
    public void Unload()
    {
        _cts?.Cancel();
        Thumb = null;
        _loadedSize = 0;
        IsLoading = false;
    }

    /// <summary>点击循环切换旗标：无 → P（入选）→ X（排除）→ 无。</summary>
    public void CycleFlag()
    {
        Photo.Flag = Photo.Flag switch
        {
            PhotoFlag.None => PhotoFlag.Pick,
            PhotoFlag.Pick => PhotoFlag.Reject,
            _ => PhotoFlag.None,
        };
        OnPropertyChanged(nameof(FlagText));
        OnPropertyChanged(nameof(HasFlag));
        _onFlagChanged(this);
    }

    private async void RestartLoad()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var cache = _cache;
        var size = _requestedSize;
        if (cache is null || size <= 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var bytes = await cache.GetAsync(Photo, size, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (bytes is null)
            {
                IsFailed = true;
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    using var ms = new MemoryStream(bytes);
                    Thumb = new Bitmap(ms);
                    _loadedSize = size;
                    IsFailed = false;
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 正常：滚出视口或尺寸切换
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                IsFailed = true;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>一行瓦片。行是虚拟化单元：滚出视口的整行都会被释放。</summary>
public sealed class TileRow
{
    public TileRow(IReadOnlyList<PhotoTile> tiles) => Tiles = tiles;

    public IReadOnlyList<PhotoTile> Tiles { get; }
}

public sealed class LibraryViewModel : INotifyPropertyChanged
{
    private const int GridPadding = 32; // ScrollViewer 左右 Padding
    private const int GridSpacing = 12;

    private readonly CatalogDatabase _db;
    private readonly ThumbnailCache _cache;
    private readonly LibraryImporter _importer;
    private List<Photo> _all = [];
    private LibraryFilter _filter = LibraryFilter.All;
    private int _tileSize = 176;
    private int _pendingTileSize;
    private double _viewportWidth;
    private string _statusText = string.Empty;
    private bool _isBusy;

    public LibraryViewModel(CatalogDatabase db, ThumbnailCache cache, LibraryImporter importer)
    {
        _db = db;
        _cache = cache;
        _importer = importer;
        Filters =
        [
            new FilterItem(LibraryFilter.All, "全部"),
            new FilterItem(LibraryFilter.Picks, "入选 P"),
            new FilterItem(LibraryFilter.Rejects, "排除 X"),
            new FilterItem(LibraryFilter.Unflagged, "未标记"),
        ];
        LoadFromDb();
    }

    public sealed record FilterItem(LibraryFilter Value, string Label);

    public IReadOnlyList<FilterItem> Filters { get; }

    public ThumbnailCache Cache => _cache;

    /// <summary>按行分块的瓦片集合，供行级虚拟化。</summary>
    public ObservableCollection<TileRow> Rows { get; } = [];

    public int TotalCount => _all.Count;

    public int PickCount => _all.Count(p => p.Flag == PhotoFlag.Pick);

    public int RejectCount => _all.Count(p => p.Flag == PhotoFlag.Reject);

    /// <summary>当前列数，由视口宽度与瓦片尺寸推算。</summary>
    public int Columns
    {
        get
        {
            var usable = _viewportWidth > 0 ? _viewportWidth - GridPadding : 1200;
            return Math.Max(1, (int)((usable + GridSpacing) / (_tileSize + GridSpacing)));
        }
    }

    public LibraryFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter == value)
            {
                return;
            }

            _filter = value;
            OnPropertyChanged();
            RebuildRows();
        }
    }

    /// <summary>滑杆实时预览值；调用 <see cref="ApplyTileSize"/> 才真正生效，避免拖动解码风暴。</summary>
    public int TileSize
    {
        get => _tileSize;
        set
        {
            _pendingTileSize = Math.Clamp(value, 120, 320);
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool EmptyVisible => Rows.Count == 0 && !IsBusy;

    /// <summary>视图尺寸变化时由视图层调用；列数变化才重建，避免滚动抖动。</summary>
    public void UpdateViewportWidth(double width)
    {
        _viewportWidth = width;
        OnPropertyChanged(nameof(Columns));
        RebuildRows();
    }

    public void ApplyTileSize()
    {
        if (_pendingTileSize == _tileSize)
        {
            return;
        }

        _tileSize = _pendingTileSize;
        OnPropertyChanged(nameof(TileSize));
        OnPropertyChanged(nameof(Columns));
        RebuildRows(); // 尺寸变了缓存键就变，可见瓦片按新尺寸重新加载
    }

    public async Task ImportAsync(string folder)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在扫描…";
        var progress = new Progress<ImportProgress>(p =>
        {
            var text = $"扫描中 {p.Scanned} · 新增 {p.Added} · {Path.GetFileName(p.CurrentFile)}";
            if (Dispatcher.UIThread.CheckAccess())
            {
                StatusText = text;
            }
            else
            {
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusText = text);
            }
        });

        try
        {
            var report = await _importer.ImportFolderAsync(folder, progress).ConfigureAwait(true);
            LoadFromDb();
            StatusText =
                $"导入完成：新增 {report.Added} · 跳过 {report.Skipped} · 失败 {report.Failed} · {report.Duration.TotalSeconds:F1}s";
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshCounts();
        }
    }

    private void LoadFromDb()
    {
        _all = _db.LoadAllPhotos();
        RebuildRows();
        RefreshCounts();
    }

    private void RebuildRows()
    {
        foreach (var row in Rows)
        {
            foreach (var tile in row.Tiles)
            {
                tile.Unload();
            }
        }

        Rows.Clear();

        var flagWrite = new Action<PhotoTile>(t => _db.SetFlag(t.Photo.Id, t.Photo.Flag));
        var source = _filter switch
        {
            LibraryFilter.Picks => _all.Where(p => p.Flag == PhotoFlag.Pick),
            LibraryFilter.Rejects => _all.Where(p => p.Flag == PhotoFlag.Reject),
            LibraryFilter.Unflagged => _all.Where(p => p.Flag == PhotoFlag.None),
            _ => _all,
        };

        var cols = Columns;
        var buffer = new List<PhotoTile>(cols);
        foreach (var photo in source)
        {
            buffer.Add(new PhotoTile(photo, flagWrite) { PixelSize = _tileSize });
            if (buffer.Count == cols)
            {
                Rows.Add(new TileRow(buffer.ToArray()));
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            Rows.Add(new TileRow(buffer.ToArray()));
        }

        OnPropertyChanged(nameof(EmptyVisible));
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PickCount));
        OnPropertyChanged(nameof(RejectCount));
        OnPropertyChanged(nameof(EmptyVisible));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
