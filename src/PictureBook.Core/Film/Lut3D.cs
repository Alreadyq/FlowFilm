namespace PictureBook.Core.Film;

/// <summary>
/// 33³ / 65³ sRGB 3D LUT。
/// 数据 z-major 排布：idx = ((z * n + y) * n + x) * 3，对四面体插值的局部访问友好。
/// </summary>
public sealed record Lut3D
{
    public required int Size { get; init; }

    /// <summary>长度 = Size³ × 3，float32，sRGB 编码域 [0,1]。</summary>
    public required float[] Data { get; init; }

    /// <summary>CC BY-SA 4.0 要求的署名文本，随文件头走，永不丢弃。</summary>
    public string? Attribution { get; init; }

    public static Lut3D Create(int size, string? attribution = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        return new Lut3D
        {
            Size = size,
            Data = new float[size * size * size * 3],
            Attribution = attribution,
        };
    }
}
