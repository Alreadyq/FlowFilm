using PictureBook.Core.Library;
using PictureBook.Core.Platform;
using Xunit;

namespace PictureBook.Core.Tests;

/// <summary>测试用伪解码器：返回固定字节，计数调用次数。</summary>
public sealed class FakeDecoder : IImageDecoder
{
    public int Calls;
    public int Priority => 10;
    public bool CanDecode(string filePath) => true;

    public Task<DecodedImage?> DecodeAsync(string filePath, int maxPixelSize, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult<DecodedImage?>(new DecodedImage([1, 2, 3, 4], maxPixelSize, maxPixelSize));
    }
}

public class ThumbnailCacheTests
{
    private static Photo MakePhoto(long id) => new()
    {
        Id = id,
        FilePath = $"/fake/{id}.jpg",
        Extension = ".jpg",
        ModifiedTicks = 1234567890,
        SourceId = 1,
        ImportedAt = DateTime.UtcNow,
    };

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-thumbtest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task DecodesOnceThenServesFromCache()
    {
        var dir = TempDir();
        var decoder = new FakeDecoder();
        var cache = new ThumbnailCache(decoder, dir);

        var photo = MakePhoto(1);
        var first = await cache.GetAsync(photo, 256);
        var second = await cache.GetAsync(photo, 256);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task WritesDiskFileAndSurvivesRestart()
    {
        var dir = TempDir();
        var photo = MakePhoto(2);

        var cache1 = new ThumbnailCache(new FakeDecoder(), dir);
        var bytes1 = await cache1.GetAsync(photo, 256);

        // 新实例（内存 LRU 已空），应直接命中磁盘，不再调用解码器
        var decoder2 = new FakeDecoder();
        var cache2 = new ThumbnailCache(decoder2, dir);
        var bytes2 = await cache2.GetAsync(photo, 256);

        Assert.NotNull(bytes1);
        Assert.Equal(bytes1, bytes2);
        Assert.Equal(0, decoder2.Calls);
    }

    [Fact]
    public async Task CacheKeyIncludesSizeAndMtime()
    {
        var dir = TempDir();
        var decoder = new FakeDecoder();
        var cache = new ThumbnailCache(decoder, dir);
        var photo = MakePhoto(3);

        _ = await cache.GetAsync(photo, 256);
        _ = await cache.GetAsync(photo, 512);
        Assert.Equal(2, decoder.Calls);

        photo.ModifiedTicks += 1; // 原片变动 → 键失效 → 重新解码
        _ = await cache.GetAsync(photo, 256);
        Assert.Equal(3, decoder.Calls);
    }

    [Fact]
    public async Task MemoryLruEvictsOldest()
    {
        var dir = TempDir();
        // 容量只够 2 个小对象，插入第 3 个时应淘汰最早的
        var cache = new ThumbnailCache(new FakeDecoder(), dir, memoryCapacityBytes: 8);
        await cache.GetAsync(MakePhoto(4), 256);
        await cache.GetAsync(MakePhoto(5), 256);
        Assert.True(cache.MemoryBytes <= 8 + 4);
        await cache.GetAsync(MakePhoto(6), 256);
        Assert.InRange(cache.MemoryBytes, 4, 8);
    }

    [Fact]
    public async Task RejectsPhotosWithoutId()
    {
        var cache = new ThumbnailCache(new FakeDecoder(), TempDir());
        var result = await cache.GetAsync(MakePhoto(0), 256);
        Assert.Null(result);
    }
}
