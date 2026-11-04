namespace PictureBook.Core.Platform;

/// <summary>
/// 解码结果：统一为 sRGB 编码字节（JPEG 或 PNG），加载方按内容嗅探，
/// 不依赖扩展名。宽高为解码后真实尺寸（已应用 EXIF 方向）。
/// </summary>
public sealed record DecodedImage(byte[] EncodedBytes, int PixelWidth, int PixelHeight);

/// <summary>
/// 平台图像解码抽象。
/// Desktop：macOS 走系统 ImageIO（RAW/HEIC 全支持），其余走 Skia；
/// Android：走 BitmapFactory / ImageDecoder；iOS：走 ImageIO。
/// </summary>
public interface IImageDecoder
{
    /// <summary>数值越小越优先尝试。</summary>
    int Priority { get; }

    bool CanDecode(string filePath);

    Task<DecodedImage?> DecodeAsync(string filePath, int maxPixelSize, CancellationToken ct = default);
}
