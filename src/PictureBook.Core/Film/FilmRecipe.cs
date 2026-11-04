namespace PictureBook.Core.Film;

/// <summary>
/// 一次胶片渲染的完整配方。此结构是「预览 LUT 引擎」与
/// 「Python 物理仿真导出引擎」之间的唯一契约。
/// </summary>
public sealed record FilmRecipe
{
    /// <summary>胶片 profile，如 kodak_portra_400。</summary>
    public required string FilmId { get; init; }

    /// <summary>相纸 / 印片 profile。反转片为 null。</summary>
    public string? PrintId { get; init; }

    /// <summary>胶片强度 0–1。1 = 完全胶片色彩。</summary>
    public double Strength { get; init; } = 1.0;

    /// <summary>曝光补偿（EV）。物理等价于负片曝光前的线性增益。</summary>
    public double ExposureCompensationEv { get; init; }

    /// <summary>放大机黄色滤光片偏移。近似为 LUT 前的通道增益。</summary>
    public double YFilterShift { get; init; }

    /// <summary>放大机品红滤光片偏移。</summary>
    public double MFilterShift { get; init; }

    /// <summary>附加颗粒强度 0–1。LUT 无法表达，仅导出引擎生效。</summary>
    public double GrainAmount { get; init; }

    /// <summary>附加光晕强度 0–1。LUT 无法表达，仅导出引擎生效。</summary>
    public double HalationAmount { get; init; }
}

public sealed record FilmExportJob(
    string SourceLinearTiffPath,
    string OutputPath,
    FilmRecipe Recipe,
    int MaxPixelSize = 0);

public sealed record ExportResult(bool Success, string? Error, TimeSpan Duration);

/// <summary>
/// 导出引擎抽象。桌面端实现 = 长驻 Python worker（spektrafilm 物理仿真）。
/// 移动端无实现，胶片导出走 LUT 路径。
/// </summary>
public interface IFilmExportEngine
{
    bool IsAvailable { get; }

    Task<ExportResult> RenderAsync(FilmExportJob job, IProgress<double>? progress, CancellationToken ct = default);
}
