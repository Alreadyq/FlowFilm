using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PictureBook.Core.Library;

public sealed record ImportProgress(int Scanned, int Added, int Skipped, int Failed, string CurrentFile);

public sealed record ImportReport(int Scanned, int Added, int Skipped, int Failed, TimeSpan Duration);

/// <summary>
/// 递归扫描文件夹并导入图库。原片不复制、不移动、不修改。
/// 去重规则：路径 + 修改时间 + 文件大小，三者都一致才跳过。
/// </summary>
public sealed class LibraryImporter
{
    private readonly CatalogDatabase _db;

    public LibraryImporter(CatalogDatabase db) => _db = db;

    public Task<ImportReport> ImportFolderAsync(
        string folder, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => ImportFolder(folder, progress, ct), ct);

    private ImportReport ImportFolder(string folder, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sourceId = _db.AddSourceIfMissing(Path.GetFullPath(folder));
        var known = _db.LoadPhotoStamps();

        int scanned = 0, added = 0, skipped = 0, failed = 0;
        var buffer = new List<Photo>(256);

        foreach (var file in EnumerateSupportedFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            try
            {
                var fi = new FileInfo(file);
                var stamp = (fi.LastWriteTimeUtc.Ticks, fi.Length);
                if (known.TryGetValue(file, out var old) && old == stamp)
                {
                    skipped++;
                }
                else
                {
                    buffer.Add(ReadMetadata(file, fi, sourceId));
                    if (buffer.Count >= 256)
                    {
                        _db.InsertPhotos(buffer);
                        added += buffer.Count;
                        buffer.Clear();
                    }
                }
            }
            catch
            {
                failed++;
            }

            if (scanned % 25 == 0)
            {
                progress?.Report(new ImportProgress(scanned, added, skipped, failed, file));
            }
        }

        if (buffer.Count > 0)
        {
            _db.InsertPhotos(buffer);
            added += buffer.Count;
        }

        progress?.Report(new ImportProgress(scanned, added, skipped, failed, string.Empty));
        return new ImportReport(scanned, added, skipped, failed, sw.Elapsed);
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string root)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            var name = Path.GetFileName(dir);
            if (!string.Equals(dir, root, StringComparison.Ordinal) &&
                (name.StartsWith('.') || name.StartsWith('$')))
            {
                // 跳过隐藏目录与 macOS 图库包（.photoslibrary 等）
                continue;
            }

            string[] files, dirs;
            try
            {
                files = System.IO.Directory.EnumerateFiles(dir).ToArray();
                dirs = System.IO.Directory.EnumerateDirectories(dir).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var f in files)
            {
                if (Photo.SupportedExtensions.Contains(Path.GetExtension(f)))
                {
                    yield return f;
                }
            }

            foreach (var d in dirs)
            {
                queue.Enqueue(d);
            }
        }
    }

    private static Photo ReadMetadata(string path, FileInfo fi, long sourceId)
    {
        var photo = new Photo
        {
            FilePath = path,
            Extension = Path.GetExtension(path),
            FileBytes = fi.Length,
            ModifiedTicks = fi.LastWriteTimeUtc.Ticks,
            SourceId = sourceId,
            ImportedAt = DateTime.UtcNow,
        };

        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            foreach (var dir in dirs)
            {
                switch (dir)
                {
                    case ExifIfd0Directory ifd0:
                        photo.CameraMake = Clean(ifd0.GetDescription(ExifDirectoryBase.TagMake));
                        photo.CameraModel = Clean(ifd0.GetDescription(ExifDirectoryBase.TagModel));
                        photo.PixelWidth = ifd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var w) ? w : 0;
                        photo.PixelHeight = ifd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var h) ? h : 0;
                        break;

                    case ExifSubIfdDirectory sub:
                        if (sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken) &&
                            taken.Year is > 1980 and < 2100)
                        {
                            photo.TakenAt = taken;
                        }

                        // ISO 标签 0x8827（ExifDirectoryBase 在不同版本里叫 TagIso / TagIsoEquivalent，用原始常量最稳）
                        if (sub.TryGetInt32(0x8827, out var iso))
                        {
                            photo.Iso = iso;
                        }

                        if (sub.TryGetDouble(ExifDirectoryBase.TagFNumber, out var fnum))
                        {
                            photo.Aperture = fnum;
                        }

                        if (sub.TryGetDouble(ExifDirectoryBase.TagExposureTime, out var exposure) && exposure > 0)
                        {
                            photo.Shutter = exposure >= 1
                                ? $"{exposure:F0}s"
                                : $"1/{Math.Round(1 / exposure)}s";
                        }

                        if (sub.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal))
                        {
                            photo.FocalLength = focal;
                        }

                        photo.LensModel = Clean(sub.GetDescription(ExifDirectoryBase.TagLensModel));
                        if (photo.PixelWidth == 0 &&
                            sub.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w2))
                        {
                            photo.PixelWidth = w2;
                        }

                        if (photo.PixelHeight == 0 &&
                            sub.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h2))
                        {
                            photo.PixelHeight = h2;
                        }

                        break;
                }
            }
        }
        catch
        {
            // 元数据缺失不阻断导入
        }

        return photo;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('\0');
}
