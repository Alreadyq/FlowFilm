using Avalonia.Media.Imaging;
using PictureBook.Core.Platform;

namespace PictureBook.Desktop.Platform;

/// <summary>
/// Skia 兜底解码器（JPEG/PNG/WebP/BMP）。所有平台可用，优先级最低。
/// 不支持 RAW / HEIC，这两类在 macOS 由 MacImageIODecoder 接管，
/// 在 Windows/Linux 需后续接入 WIC / LibRaw。
/// </summary>
public sealed class AvaloniaBitmapDecoder : IImageDecoder
{
    public int Priority => 50;

    public bool CanDecode(string filePath) => File.Exists(filePath);

    public Task<DecodedImage?> DecodeAsync(string filePath, int maxPixelSize, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var fs = File.OpenRead(filePath);
                using var bitmap = Bitmap.DecodeToWidth(fs, maxPixelSize);
                using var ms = new MemoryStream();
                bitmap.Save(ms);
                return new DecodedImage(ms.ToArray(), bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            }
            catch (Exception ex)
            {
                if (Environment.GetEnvironmentVariable("PB_DEBUG") == "1")
                {
                    Console.Error.WriteLine($"[AvaloniaBitmapDecoder] {ex.Message}");
                }

                return null;
            }
        }, ct);
    }
}
