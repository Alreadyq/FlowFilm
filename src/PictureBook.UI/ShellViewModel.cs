using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PictureBook.UI;

public sealed class FeatureNavItem : INotifyPropertyChanged
{
    private bool _isActive;

    public FeatureNavItem(string id, string title, string step, string subtitle)
    {
        Id = id;
        Title = title;
        Step = step;
        Subtitle = subtitle;
    }

    public string Id { get; }
    public string Title { get; }
    public string Step { get; }
    public string Subtitle { get; }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 应用外壳：顶部模块切换（图库 / 胶片 / 索引图），每个模块一个独立功能区。
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    public ShellViewModel(LibraryViewModel library)
    {
        Library = library;
        Film = new FilmViewModel(library);
        Sheet = new SheetViewModel(library);

        Tabs =
        [
            new FeatureNavItem("library", "图库", "01", "导入 · 选片"),
            new FeatureNavItem("film", "胶片", "02", "胶片处理"),
            new FeatureNavItem("sheet", "索引图", "03", "排版 · 导出"),
        ];
        Tabs[0].IsActive = true;
        _selected = Tabs[0];

        // 流转链：图库勾选 → 胶片 → 索引图
        Library.FlowRequested += () =>
        {
            Film.SetWorkingSet(Library.TakeFlow());
            NavigateTo("film");
        };
        Film.FlowToSheetRequested += () =>
        {
            Sheet.SetFrames(Film.WorkingSet.ToArray());
            NavigateTo("sheet");
        };
    }

    public IReadOnlyList<FeatureNavItem> Tabs { get; }

    public LibraryViewModel Library { get; }
    public FilmViewModel Film { get; }
    public SheetViewModel Sheet { get; }

    private FeatureNavItem _selected;

    public FeatureNavItem Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected.IsActive = false;
            _selected = value;
            _selected.IsActive = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLibrary));
            OnPropertyChanged(nameof(IsFilm));
            OnPropertyChanged(nameof(IsSheet));
        }
    }

    public bool IsLibrary => Selected.Id == "library";
    public bool IsFilm => Selected.Id == "film";
    public bool IsSheet => Selected.Id == "sheet";

    /// <summary>程序化切换模块（流转链用）。</summary>
    public void NavigateTo(string id)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == id);
        if (tab is not null)
        {
            Selected = tab;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
