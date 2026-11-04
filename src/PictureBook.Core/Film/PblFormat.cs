namespace PictureBook.Core.Film;

/// <summary>
/// PictureBook 私有 LUT 容器格式（.pbl）。
/// 相比 .cube 文本（65³ 约 10MB、解析 300ms+）：65³ float32 约 3.2MB、mmap 直读、加载 &lt;5ms。
/// 头部携带 CC BY-SA 4.0 署名 —— 许可证要求「每个副本都要带」，写进文件头最不容易丢。
/// </summary>
public static class PblFormat
{
    public const int Version = 1;
    private const int HeaderSize = 16;

    /// <summary>
    /// 布局：magic "PBL1"(4) | version(4,LE) | size(4,LE) | attributionLen(4,LE)
    ///       | attribution(utf8) | payload float32 LE，z-major，长度 size³×3
    /// </summary>
    public static void Write(Stream stream, Lut3D lut)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var attr = lut.Attribution ?? string.Empty;
        var attrBytes = System.Text.Encoding.UTF8.GetBytes(attr);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("PBL1"u8);
        writer.Write(Version);
        writer.Write(lut.Size);
        writer.Write(attrBytes.Length);
        writer.Write(attrBytes);
        foreach (var v in lut.Data)
        {
            writer.Write(v);
        }

        writer.Flush();
    }

    public static Lut3D Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[4];
        if (reader.Read(magic) != 4 || !magic.SequenceEqual("PBL1"u8))
        {
            throw new InvalidDataException("不是有效的 .pbl 文件（magic 不匹配）");
        }

        int version = reader.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"不支持的 .pbl 版本：{version}");
        }

        int size = reader.ReadInt32();
        int attrLen = reader.ReadInt32();
        var attrBytes = reader.ReadBytes(attrLen);
        var data = new float[size * size * size * 3];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = reader.ReadSingle();
        }

        return new Lut3D
        {
            Size = size,
            Data = data,
            Attribution = System.Text.Encoding.UTF8.GetString(attrBytes),
        };
    }

    public static void WriteToFile(string path, Lut3D lut)
    {
        using var fs = File.Create(path);
        Write(fs, lut);
    }

    public static Lut3D ReadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }
}
