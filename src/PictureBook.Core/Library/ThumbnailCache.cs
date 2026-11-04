namespace PictureBook.Core.Library;

/// <summary>
/// 缩略图两级缓存：磁盘持久化 + 内存 LRU（按字节计）。
/// 缓存键 = 照片 Id + 修改时间 + 目标尺寸，原文件变动后自动失效。
/// 解码通过注入的 <see cref="Platform.IImageDecoder"/> 完成（macOS 走系统 ImageIO，RAW/HEIC 全支持）。
/// 解码并发由内部信号量限制（默认 4），避免批量滚动时打满磁盘与 CPU。
/// </summary>
public sealed class ThumbnailCache
{
    private readonly Platform.IImageDecoder _decoder;
    private readonly string _root;
    private readonly int _memoryCapacityBytes;
    private readonly SemaphoreSlim _decodeGate;
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
    private long _bytes;

    private sealed record Entry(string Key, byte[] Bytes);

    public ThumbnailCache(
        Platform.IImageDecoder decoder,
        string rootDirectory,
        int memoryCapacityBytes = 96 * 1024 * 1024,
        int decodeConcurrency = 4)
    {
        _decoder = decoder;
        _root = rootDirectory;
        _memoryCapacityBytes = memoryCapacityBytes;
        _decodeGate = new SemaphoreSlim(decodeConcurrency, decodeConcurrency);
        Directory.CreateDirectory(_root);
    }

    /// <summary>当前内存缓存占用量（字节），供状态栏/调试。</summary>
    public long MemoryBytes
    {
        get { lock (_gate) { return _bytes; } }
    }

    /// <summary>
    /// 取缩略图（已编码的 JPEG/PNG 字节）。命中内存或磁盘即返回；未命中则解码原片并写入两级缓存。
    /// </summary>
    public async Task<byte[]?> GetAsync(Photo photo, int size, CancellationToken ct = default)
    {
        if (photo.Id == 0)
        {
            return null; // 未入库的照片没有稳定缓存键
        }

        var key = CacheKey(photo, size);

        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Bytes;
            }
        }

        var path = CachePath(key);
        byte[]? bytes = null;
        if (File.Exists(path))
        {
            try
            {
                bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                bytes = null; // 竞争下文件可能刚被清理，走重解码
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        if (bytes is null)
        {
            await _decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 双检：等信号量期间可能已被并行请求填充
                if (File.Exists(path))
                {
                    bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                }

                if (bytes is null)
                {
                    var decoded = await _decoder.DecodeAsync(photo.FilePath, size, ct).ConfigureAwait(false);
                    if (decoded is null)
                    {
                        return null;
                    }

                    bytes = decoded.EncodedBytes;
                    WriteAtomic(path, bytes);
                }
            }
            finally
            {
                _decodeGate.Release();
            }
        }

        lock (_gate)
        {
            InsertLocked(key, bytes);
        }

        return bytes;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            // 磁盘写失败不阻断使用：内存里已经有了，下次再试
        }
    }

    private void InsertLocked(string key, byte[] bytes)
    {
        if (_index.ContainsKey(key))
        {
            return;
        }

        var node = new LinkedListNode<Entry>(new Entry(key, bytes));
        _lru.AddFirst(node);
        _index[key] = node;
        _bytes += bytes.Length;

        while (_bytes > _memoryCapacityBytes && _lru.Count > 1)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _index.Remove(last.Value.Key);
            _bytes -= last.Value.Bytes.Length;
        }
    }

    private static string CacheKey(Photo photo, int size) =>
        $"{photo.Id:x16}-{photo.ModifiedTicks:x16}-{size}.jpg";

    private string CachePath(string key) => Path.Combine(_root, key[..2], key);
}
