using System.Collections.Frozen;

namespace PictureBook.Core.Library;

public enum PhotoFlag
{
    Reject = -1,
    None = 0,
    Pick = 1,
}

public sealed class Photo
{
    public static readonly FrozenSet<string> RawExtensions = new[]
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf",
        ".pef", ".srw", ".nrw", ".kdc", ".dcr", ".mef", ".mos", ".erf",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> SupportedExtensions = RawExtensions.Union(new[]
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".tif", ".tiff", ".bmp",
    }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public long Id { get; set; }

    public required string FilePath { get; set; }

    public long SourceId { get; set; }

    public string FileName => Path.GetFileName(FilePath);

    public string Extension { get; set; } = string.Empty;

    public long FileBytes { get; set; }

    public long ModifiedTicks { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public DateTime? TakenAt { get; set; }

    public string? CameraMake { get; set; }

    public string? CameraModel { get; set; }

    public string? LensModel { get; set; }

    public int? Iso { get; set; }

    public double? Aperture { get; set; }

    public string? Shutter { get; set; }

    public double? FocalLength { get; set; }

    public PhotoFlag Flag { get; set; } = PhotoFlag.None;

    public int Rating { get; set; }

    public DateTime ImportedAt { get; set; }

    public bool IsRaw => RawExtensions.Contains(Extension);

    public string CameraDisplay =>
        string.Join(" ", new[] { CameraMake, CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    public string ExposureDisplay =>
        string.Join(" · ", new[]
        {
            FocalLength is > 0 ? $"{FocalLength:F0}mm" : null,
            Aperture is > 0 ? $"f/{Aperture:F1}" : null,
            Shutter,
            Iso is > 0 ? $"ISO {Iso}" : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
}
