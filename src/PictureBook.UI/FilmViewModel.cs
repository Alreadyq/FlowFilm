using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PictureBook.UI;

/// <summary>
/// 胶片工作台。选中态与图库全局共用（Library.SelectedTile），
/// 预览管线待 lut-bake 产出 LUT 后接入，当前 UI 先行。
/// </summary>
public sealed class FilmViewModel : INotifyPropertyChanged
{
    public FilmViewModel(LibraryViewModel library)
    {
        Library = library;
        Library.TilesChanged += RebuildStrip;
        Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.SelectedTile) or nameof(LibraryViewModel.HasSelection))
            {
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(HasSelection));
            }
        };
        RebuildStrip();
    }

    public LibraryViewModel Library { get; }

    /// <summary>底部胶片条（平铺全部照片）。</summary>
    public ObservableCollection<PhotoTile> Strip { get; } = [];

    /// <summary>规划接入的胶片型号（待 lut-bake 烘焙后逐个点亮）。</summary>
    public IReadOnlyList<string> Stocks { get; } =
    [
        "Kodak Portra 160", "Kodak Portra 400", "Kodak Portra 800",
        "Kodak Gold 200", "Kodak Ultramax 400", "Kodak Ektar 100",
        "Kodak Vision3 50D", "Kodak Vision3 250D", "Kodak Vision3 500T",
        "Fujifilm Velvia 100", "Fujifilm Provia 100F", "CineStill 800T",
    ];

    public PhotoTile? Selected => Library.SelectedTile;

    public bool HasSelection => Selected is not null;

    public string SelectedTitle => Selected?.Title ?? string.Empty;

    private void RebuildStrip()
    {
        Strip.Clear();
        foreach (var tile in Library.FlatTiles)
        {
            Strip.Add(tile);
        }

        OnPropertyChanged(nameof(Strip));
    }

    /// <summary>胶片条点击 → 全局选中。</summary>
    public void Select(PhotoTile tile) => Library.Select(tile);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
