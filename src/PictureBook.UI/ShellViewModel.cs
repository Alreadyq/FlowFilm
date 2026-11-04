using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PictureBook.UI;

public sealed class FeatureNavItem : INotifyPropertyChanged
{
    private bool _isActive;

    public FeatureNavItem(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }

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
            new FeatureNavItem("library", "图库"),
            new FeatureNavItem("film", "胶片"),
            new FeatureNavItem("sheet", "索引图"),
        ];
        Tabs[0].IsActive = true;
        _selected = Tabs[0];
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
