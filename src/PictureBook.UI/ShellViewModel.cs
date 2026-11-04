using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PictureBook.UI;

public sealed class FeatureNavItem : INotifyPropertyChanged
{
    private bool _isActive;

    public FeatureNavItem(string id, string title, string subtitle, string[] capabilities, string status)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Capabilities = capabilities;
        Status = status;
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string[] Capabilities { get; }
    public string Status { get; }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ShellViewModel : INotifyPropertyChanged
{
    public ShellViewModel(LibraryViewModel? library)
    {
        Library = library;

        Features =
        [
            new FeatureNavItem("library", "图库",
                "RAW 照片导入与管理",
                [
                    "递归导入文件夹，原片只读，路径 + 修改时间 + 大小去重",
                    "EXIF 提取：拍摄时间 / 机身 / 镜头 / 曝光参数",
                    "macOS 系统 ImageIO 解码 RAW（CR2/CR3/NEF/ARW/DNG…）与 HEIC，零外部二进制",
                    "缩略图磁盘缓存 + 内存 LRU，SQLite 持久图库与图集",
                    "旗标 P / X / U，检索与筛选",
                ],
                "可用"),

            new FeatureNavItem("film", "胶片",
                "spektrafilm 胶片模拟",
                [
                    "离线烘焙 65³ LUT，四面体插值，预览毫秒级（M3 实测 4.4MP ≈ 33ms）",
                    "导出走 Python 物理仿真（长驻 worker），含真实颗粒与光晕",
                    "曝光补偿 = LUT 前线性增益（物理等价）；滤光片偏移 = 通道增益矩阵",
                    ".pbl 容器：文件头内置 CC BY-SA 4.0 署名，永不丢失",
                ],
                "引擎与 .pbl 格式已就绪，待烘焙工具链"),

            new FeatureNavItem("sheet", "索引图",
                "胶片索引图生成（对标 film-index-generator）",
                [
                    "135 / 半格 / 6×6 / 6×9 / XPan 画幅，毫米级真实几何排版",
                    "齿孔、片边型号文字、帧号、淡黄纸底",
                    "观片器放大检片与 A4 比例导出（SkiaSharp 渲染）",
                    "排版计算已就绪，待渲染器",
                ],
                "排版模型已就绪"),
        ];

        _selected = Features[0];
        _selected.IsActive = true;
    }

    public ObservableCollection<FeatureNavItem> Features { get; }

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
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedSubtitle));
            OnPropertyChanged(nameof(SelectedCapabilities));
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(IsLibrary));
        }
    }

    public LibraryViewModel? Library { get; }

    /// <summary>当前导航的是图库且服务就绪 —— 主内容区据此切换到网格视图。</summary>
    public bool IsLibrary => Selected.Id == "library" && Library is not null;

    public string SelectedTitle => Selected.Title;
    public string SelectedSubtitle => Selected.Subtitle;
    public string[] SelectedCapabilities => Selected.Capabilities;
    public string SelectedStatus => Selected.Status;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
