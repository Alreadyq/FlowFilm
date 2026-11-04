using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PictureBook.UI;

/// <summary>
/// 索引图模块。画幅几何模型已在 Core/ContactSheet 落位，
/// 渲染器与导出待下一阶段接入，当前先把功能区骨架立起来。
/// </summary>
public sealed class SheetViewModel : INotifyPropertyChanged
{
    public SheetViewModel(LibraryViewModel library)
    {
        Library = library;
        Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.TotalCount) or nameof(LibraryViewModel.PickCount))
            {
                OnPropertyChanged(nameof(PhotoSummary));
                OnPropertyChanged(nameof(HasPhotos));
            }
        };
    }

    public LibraryViewModel Library { get; }

    public IReadOnlyList<string> Formats { get; } =
    [
        "135 · 36 帧",
        "半格 · 72 帧",
        "6×6 · 12 帧",
        "6×9 · 8 帧",
        "XPan · 21 帧",
    ];

    public IReadOnlyList<string> Papers { get; } = ["A4", "A3"];

    public string PhotoSummary => Library.PickCount > 0
        ? $"{Library.PickCount} 张入选照片可用"
        : $"{Library.TotalCount} 张照片可用";

    public bool HasPhotos => Library.TotalCount > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
