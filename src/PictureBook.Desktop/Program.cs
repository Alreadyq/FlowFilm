using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using PictureBook.Core.Library;
using PictureBook.Core.Platform;
using PictureBook.Desktop.Platform;
using PictureBook.UI;

namespace PictureBook.Desktop;

internal static class Program
{
    private const string DbDirectory = "PictureBook";
    private const string DbFileName = "catalog.db";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--decode-test")
        {
            return RunDecodeTest(args[1]).GetAwaiter().GetResult();
        }

        if (args.Length >= 2 && args[0] == "--import")
        {
            return RunHeadlessImport(args[1]).GetAwaiter().GetResult();
        }

        if (args.Contains("--smoke"))
        {
            App.SmokeMode = true;
        }

        // 服务注入：GUI 模式下 App 据此构建图库视图模型
        App.PlatformDecoder = CreateDecoder();
        App.CatalogPath = DefaultCatalogPath;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    /// <summary>图库默认路径。按平台走应用数据目录。</summary>
    public static string DefaultCatalogPath
    {
        get
        {
            string baseDir = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(baseDir, DbDirectory, DbFileName);
        }
    }

    public static CatalogDatabase OpenDefaultCatalog() => new(DefaultCatalogPath);

    public static IImageDecoder CreateDecoder() =>
        new CompositeImageDecoder(new MacImageIODecoder(), new AvaloniaBitmapDecoder());

    private static async Task<int> RunDecodeTest(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"DECODE_FAIL 文件不存在: {path}");
            return 2;
        }

        // 注意：macOS 上 Avalonia 的 SetupWithoutStarting 会让进程收尾挂死，
        // 因此独立解码自检只测原生 ImageIO 链路；AvaloniaBitmapDecoder 在 GUI 内生效。
        if (!OperatingSystem.IsMacOS())
        {
            BuildAvaloniaApp().SetupWithoutStarting();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await CreateDecoder().DecodeAsync(path, 512);
        sw.Stop();
        if (result is null)
        {
            Console.Error.WriteLine($"DECODE_FAIL {path}");
            return 3;
        }

        Console.WriteLine(
            $"DECODE_OK {result.PixelWidth}x{result.PixelHeight} bytes={result.EncodedBytes.Length} elapsed={sw.ElapsedMilliseconds}ms");
        Console.Out.Flush();
        return 0;
    }

    /// <summary>
    /// 无头导入自检：扫描 → 入库 → 逐张生成缩略图（含 RAW 解码），全链路不依赖 GUI。
    /// 用法：PictureBook --import &lt;folder&gt; [thumbSize]
    /// </summary>
    private static async Task<int> RunHeadlessImport(string folder, int thumbSize = 256)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"IMPORT_FAIL 目录不存在: {folder}");
            return 2;
        }

        var db = new CatalogDatabase(DefaultCatalogPath);
        var thumbsDir = Path.Combine(Path.GetDirectoryName(DefaultCatalogPath)!, "thumbs");
        var cache = new ThumbnailCache(CreateDecoder(), thumbsDir);
        var importer = new LibraryImporter(db);

        Console.WriteLine($"IMPORT_START {folder}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = await importer.ImportFolderAsync(folder,
            new Progress<ImportProgress>(p =>
                Console.WriteLine($"  …scanned={p.Scanned} added={p.Added} skipped={p.Skipped} failed={p.Failed}")));
        Console.WriteLine(
            $"IMPORT_DONE scanned={report.Scanned} added={report.Added} skipped={report.Skipped} failed={report.Failed} elapsed={report.Duration.TotalMilliseconds:F0}ms");

        var photos = db.LoadAllPhotos();
        int thumbs = 0, misses = 0;
        sw.Restart();
        foreach (var photo in photos)
        {
            try
            {
                var bytes = await cache.GetAsync(photo, thumbSize);
                if (bytes is not null)
                {
                    thumbs++;
                    Console.WriteLine($"  THUMB {photo.FileName} {photo.PixelWidth}x{photo.PixelHeight} -> {bytes.Length}B");
                }
                else
                {
                    misses++;
                    Console.WriteLine($"  MISS  {photo.FileName}");
                }
            }
            catch (Exception ex)
            {
                misses++;
                Console.WriteLine($"  ERR   {photo.FileName}: {ex.Message}");
            }
        }

        Console.WriteLine(
            $"THUMBS_DONE ok={thumbs} miss={misses} elapsed={sw.ElapsedMilliseconds:F0}ms memCache={cache.MemoryBytes / 1024}KB");
        Console.Out.Flush();
        return misses == 0 ? 0 : 1;
    }
}
