namespace PictureBook.Core.Platform;

/// <summary>
/// 把多个解码器按 Priority 排序后依次尝试，任何一个成功即返回。
/// </summary>
public sealed class CompositeImageDecoder : IImageDecoder
{
    private readonly IImageDecoder[] _decoders;

    public CompositeImageDecoder(params IImageDecoder[] decoders) =>
        _decoders = decoders.OrderBy(d => d.Priority).ToArray();

    public int Priority => 0;

    public bool CanDecode(string filePath) => _decoders.Any(d => d.CanDecode(filePath));

    public async Task<DecodedImage?> DecodeAsync(string filePath, int maxPixelSize, CancellationToken ct = default)
    {
        foreach (var decoder in _decoders)
        {
            if (!decoder.CanDecode(filePath))
            {
                continue;
            }

            var result = await decoder.DecodeAsync(filePath, maxPixelSize, ct).ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
