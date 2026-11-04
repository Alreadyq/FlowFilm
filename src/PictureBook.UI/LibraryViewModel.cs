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
    private bool _isSelected;
    private int _loadedSize;
    private int _pixelSize = 176;
    private ThumbnailCache? _cache;
    private int _requestedSize;
    private CancellationTokenSource? _cts;
    private List<KeyValuePair<string, string>>? _details;

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

    /// <summary>旗标角标配色：入选蓝、排除红。</summary>
    public bool IsReject => Photo.Flag == PhotoFlag.Reject;

    /// <summary>当前是否被选中（图库网格与胶片条共用全局选中态）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

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

    /// <summary>右栏信息面板的键值行（惰性构建）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Details => _details ??= BuildDetails();

    private List<KeyValuePair<string, string>> BuildDetails()
    {
        var rows = new List<KeyValuePair<string, string>>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new(key, value));
            }
        }

        Add("拍摄时间", Photo.TakenAt?.ToString("yyyy-MM-dd HH:mm"));
        Add("机身", Photo.CameraDisplay);
        Add("镜头", Photo.LensModel);
        Add("曝光", Photo.ExposureDisplay);
        Add("格式", Photo.IsRaw ? $"RAW ({Photo.Extension.TrimStart('.').ToUpperInvariant()})"
            : Photo.Extension.TrimStart('.').ToUpperInvariant());
        Add("大小", $"{Photo.FileBytes / 1024d / 1024d:F1} MB");
        Add("像素", Photo.PixelWidth > 0 ? $"{Photo.PixelWidth} × {Photo.PixelHeight}" : null);
        Add("路径", Photo.FilePath);
        return rows;
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

    /// <summary>设置旗标并写库。</summary>
    public void SetFlag(PhotoFlag flag)
    {
        if (Photo.Flag == flag)
        {
            return;
        }

        Photo.Flag = flag;
        OnPropertyChanged(nameof(FlagText));
        OnPropertyChanged(nameof(HasFlag));
        OnPropertyChanged(nameof(IsReject));
        _onFlagChanged(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
    private PhotoTile? _selectedTile;
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
        Filters[0].IsSelected = true;
        LoadFromDb();
        ReloadSources();
    }

    public sealed class FilterItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public FilterItem(LibraryFilter value, string label)
        {
            Value = value;
            Label = label;
        }

        public LibraryFilter Value { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public IReadOnlyList<FilterItem> Filters { get; }

    public ThumbnailCache Cache => _cache;

    /// <summary>按行分块的瓦片集合，供行级虚拟化。</summary>
    public ObservableCollection<TileRow> Rows { get; } = [];

    /// <summary>平铺瓦片（胶片条等模块复用），与 Rows 同步重建。</summary>
    public IReadOnlyList<PhotoTile> FlatTiles { get; private set; } = [];

    /// <summary>网格重建完成（含导入/筛选/尺寸变化后）。</summary>
    public event Action? TilesChanged;

    /// <summary>侧栏「来源」文件夹名列表。</summary>
    public ObservableCollection<string> Sources { get; } = [];

    public PhotoTile? SelectedTile
    {
        get => _selectedTile;
        private set { _selectedTile = value; }
    }

    public bool HasSelection => SelectedTile is not null;

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
            foreach (var f in Filters)
            {
                f.IsSelected = f.Value == value;
            }

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

    /// <summary>全局选中一张照片（图库网格、胶片条共用）。</summary>
    public void Select(PhotoTile? tile)
    {
        if (ReferenceEquals(SelectedTile, tile))
        {
            return;
        }

        if (SelectedTile is not null)
        {
            SelectedTile.IsSelected = false;
        }

        _selectedTile = tile;
        if (tile is not null)
        {
            tile.IsSelected = true;
            // 信息面板的预览图至少 256px，独立于网格瓦片尺寸
            tile.BeginLoad(_cache, Math.Max(_tileSize, 256));
        }

        OnPropertyChanged(nameof(SelectedTile));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>对选中照片设置旗标并写库（右栏 P / 无 / X 按钮）。</summary>
    public void SetSelectedFlag(PhotoFlag flag)
    {
        SelectedTile?.SetFlag(flag);
        RefreshCounts();
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
            ReloadSources();
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

    private void ReloadSources()
    {
        Sources.Clear();
        foreach (var (_, path) in _db.LoadSources())
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            Sources.Add(string.IsNullOrEmpty(name) ? path : name);
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
        var flat = new List<PhotoTile>();
        foreach (var photo in source)
        {
            var tile = new PhotoTile(photo, flagWrite) { PixelSize = _tileSize };
            if (photo.Id == SelectedTile?.Photo.Id)
            {
                tile.IsSelected = true;
                _selectedTile = tile; // 重建后选中态迁移到新瓦片实例
            }

            flat.Add(tile);
            buffer.Add(tile);
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

        FlatTiles = flat;
        TilesChanged?.Invoke();
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
