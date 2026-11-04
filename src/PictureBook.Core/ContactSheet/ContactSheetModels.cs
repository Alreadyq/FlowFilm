namespace PictureBook.Core.ContactSheet;

public enum FilmFormatKind
{
    Format135,
    HalfFrame135,
    Format6X6,
    Format6X9,
    Xpan,
}

public enum ContactSheetBackground
{
    PaperWarm,
    White,
    Blur,
}

/// <summary>
/// 画幅物理尺寸（mm）与默认列数。索引图按真实胶片几何排版，
/// 这是与 film-index-generator 对齐的关键：齿孔、片边、帧距都有物理依据。
/// </summary>
public static class FilmFormatInfo
{
    public static (double WidthMm, double HeightMm) FrameSize(this FilmFormatKind kind) => kind switch
    {
        FilmFormatKind.Format135 => (36.0, 24.0),
        FilmFormatKind.HalfFrame135 => (17.0, 24.0),
        FilmFormatKind.Format6X6 => (56.0, 56.0),
        FilmFormatKind.Format6X9 => (84.0, 56.0),
        FilmFormatKind.Xpan => (65.0, 24.0),
        _ => (36.0, 24.0),
    };

    public static int DefaultColumns(this FilmFormatKind kind) => kind switch
    {
        FilmFormatKind.Format135 => 6,
        FilmFormatKind.HalfFrame135 => 12,
        FilmFormatKind.Format6X6 => 3,
        FilmFormatKind.Format6X9 => 3,
        FilmFormatKind.Xpan => 4,
        _ => 6,
    };

    public static string DisplayName(this FilmFormatKind kind) => kind switch
    {
        FilmFormatKind.Format135 => "135 (36×24)",
        FilmFormatKind.HalfFrame135 => "135 半格 (17×24)",
        FilmFormatKind.Format6X6 => "120 6×6",
        FilmFormatKind.Format6X9 => "120 6×9",
        FilmFormatKind.Xpan => "XPan (65×24)",
        _ => kind.ToString(),
    };
}

public sealed record ContactSheetOptions
{
    public FilmFormatKind Format { get; init; } = FilmFormatKind.Format135;

    public int Columns { get; init; } = 6;

    public bool ShowSprocketHoles { get; init; } = true;

    public bool ShowEdgeMarkings { get; init; } = true;

    /// <summary>胶片型号文字，印在片边上，如 "KODAK GOLD 200"。</summary>
    public string? FilmStockName { get; init; }

    /// <summary>起始帧号（胶卷第几张）。</summary>
    public int FirstFrameNumber { get; init; } = 1;

    public ContactSheetBackground Background { get; init; } = ContactSheetBackground.PaperWarm;
}

public sealed record ContactSheetFrame(string ImagePath, int FrameNumber);

/// <summary>
/// 排版计算：输出每个帧在「毫米坐标系」中的矩形。
/// 渲染器（后续 SkiaSharp / 位图输出）只负责把毫米坐标按 DPI 换算为像素。
/// </summary>
public static class ContactSheetLayout
{
    /// <summary>帧与帧的间隙（mm）。</summary>
    public const double FrameGapMm = 2.0;

    /// <summary>片边留白（mm）。</summary>
    public const double EdgeMarginMm = 3.0;

    public sealed record FrameRect(int FrameNumber, double X, double Y, double Width, double Height);

    public sealed record LayoutResult(double SheetWidthMm, double SheetHeightMm, IReadOnlyList<FrameRect> Frames);

    public static LayoutResult Compute(ContactSheetOptions options, int frameCount)
    {
        var (fw, fh) = options.Format.FrameSize();
        int columns = Math.Max(1, options.Columns);
        int rows = (int)Math.Ceiling(frameCount / (double)columns);

        // 横向：margin + [frame + gap] × n + frame + margin（行内最后一个不加 gap）
        double innerWidth = columns * fw + (columns - 1) * FrameGapMm;
        double innerHeight = rows * fh + (rows - 1) * FrameGapMm;
        double sheetW = innerWidth + EdgeMarginMm * 2;
        double sheetH = innerHeight + EdgeMarginMm * 2;

        var frames = new List<FrameRect>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            int col = i % columns;
            int row = i / columns;
            double x = EdgeMarginMm + col * (fw + FrameGapMm);
            // 从上往下排；第 0 行在最上
            double y = EdgeMarginMm + row * (fh + FrameGapMm);
            frames.Add(new FrameRect(options.FirstFrameNumber + i, x, y, fw, fh));
        }

        return new LayoutResult(sheetW, sheetH, frames);
    }
}
