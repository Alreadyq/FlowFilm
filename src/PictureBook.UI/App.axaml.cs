using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PictureBook.Core.Library;
using PictureBook.Core.Platform;
using PictureBook.UI.Views;

namespace PictureBook.UI;

public partial class App : Application
{
    /// <summary>供桌面端 --smoke 自检模式使用。</summary>
    public static bool SmokeMode { get; set; }

    /// <summary>平台解码器，由桌面入口在启动前注入。</summary>
    public static IImageDecoder? PlatformDecoder { get; set; }

    /// <summary>图库数据库路径，由桌面入口在启动前注入。</summary>
    public static string? CatalogPath { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LibraryViewModel? library = null;
            if (PlatformDecoder is not null && CatalogPath is not null)
            {
                var db = new CatalogDatabase(CatalogPath);
                var thumbsDir = Path.Combine(Path.GetDirectoryName(CatalogPath)!, "thumbs");
                var cache = new ThumbnailCache(PlatformDecoder, thumbsDir);
                library = new LibraryViewModel(db, cache, new LibraryImporter(db));
            }

            desktop.MainWindow = new MainWindow(new ShellViewModel(library));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
