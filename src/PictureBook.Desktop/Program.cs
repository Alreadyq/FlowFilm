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

        if (args.Contains("--smoke"))
        {
            App.SmokeMode = true;
        }

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
}
