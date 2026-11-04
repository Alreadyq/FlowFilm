using System.Runtime.InteropServices;
using PictureBook.Core.Platform;

namespace PictureBook.Desktop.Platform;

/// <summary>
/// macOS 系统 ImageIO 解码器。通过 P/Invoke 直接驱动 CoreGraphics / ImageIO，
/// RAW（CR2/CR3/NEF/ARW/DNG/ORF/RW2/RAF…）与 HEIC 全部由系统解码，零第三方二进制。
/// 仅在 macOS 上可用；其他平台由 AvaloniaBitmapDecoder 兜底。
/// </summary>
public sealed class MacImageIODecoder : IImageDecoder
{
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string IIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    private const uint KcfStringEncodingUtf8 = 0x08000100;
    private const long KcfNumberSInt64Type = 4;

    public int Priority => 10;

    public bool CanDecode(string filePath) => OperatingSystem.IsMacOS() && File.Exists(filePath);

    public Task<DecodedImage?> DecodeAsync(string filePath, int maxPixelSize, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult<DecodedImage?>(null);
        }

        return Task.Run(() =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"pb_dec_{Guid.NewGuid():N}.jpg");
            try
            {
                if (!TryWriteJpeg(filePath, tmp, maxPixelSize, out int width, out int height))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(tmp);
                return new DecodedImage(bytes, width, height);
            }
            catch (Exception ex)
            {
                if (Environment.GetEnvironmentVariable("PB_DEBUG") == "1")
                {
                    Console.Error.WriteLine($"[MacImageIODecoder] {ex}");
                }

                return null;
            }
            finally
            {
                TryDelete(tmp);
            }
        }, ct);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不影响结果
        }
    }

    // ---- ImageIO ----

    private static bool TryWriteJpeg(string src, string dst, int maxPixelSize, out int width, out int height)
    {
        width = height = 0;
        var urlBytes = EncodePath(src);
        var outBytes = EncodePath(dst);

        var srcUrl = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, urlBytes, urlBytes.Length - 1, false);
        var dstUrl = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, outBytes, outBytes.Length - 1, false);
        var jpegType = CFStringCreateWithCString(IntPtr.Zero, "public.jpeg", KcfStringEncodingUtf8);

        try
        {
            var srcRef = CGImageSourceCreateWithURL(srcUrl, IntPtr.Zero);
            if (srcRef == IntPtr.Zero)
            {
                DebugLog("CGImageSourceCreateWithURL 失败");
                return false;
            }

            try
            {
                long[] numberValues = [maxPixelSize, (long)(0.88 * 100)];
                var numbers = new IntPtr[numberValues.Length];
                for (int i = 0; i < numberValues.Length; i++)
                {
                    long v = numberValues[i];
                    numbers[i] = CFNumberCreate(IntPtr.Zero, KcfNumberSInt64Type, ref v);
                }

                var always = SymRef(IIO, "kCGImageSourceCreateThumbnailFromImageAlways");
                var transform = SymRef(IIO, "kCGImageSourceCreateThumbnailWithTransform");
                var maxSizeKey = SymRef(IIO, "kCGImageSourceThumbnailMaxPixelSize");
                var qualityKey = SymRef(IIO, "kCGImageDestinationLossyCompressionQuality");
                var cfTrue = SymRef(CF, "kCFBooleanTrue");
                if (always == IntPtr.Zero || transform == IntPtr.Zero || maxSizeKey == IntPtr.Zero ||
                    qualityKey == IntPtr.Zero || cfTrue == IntPtr.Zero)
                {
                    DebugLog($"ImageIO 符号缺失: always={always != IntPtr.Zero} transform={transform != IntPtr.Zero} " +
                             $"maxSize={maxSizeKey != IntPtr.Zero} quality={qualityKey != IntPtr.Zero} bool={cfTrue != IntPtr.Zero}");
                    return false; // 系统 ImageIO 符号缺失，交给兜底解码器
                }

                IntPtr[] keys = [always, transform, maxSizeKey, qualityKey];
                IntPtr[] values = [cfTrue, cfTrue, numbers[0], numbers[1]];

                var keyCallbacks = SymAddr("CoreFoundation", "kCFTypeDictionaryKeyCallBacks");
                var valueCallbacks = SymAddr("CoreFoundation", "kCFTypeDictionaryValueCallBacks");
                var options = CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, keyCallbacks, valueCallbacks);

                IntPtr image = IntPtr.Zero, dest = IntPtr.Zero;
                try
                {
                    image = CGImageSourceCreateThumbnailAtIndex(srcRef, 0, options);
                    if (image == IntPtr.Zero)
                    {
                        DebugLog("CGImageSourceCreateThumbnailAtIndex 返回空");
                        return false;
                    }

                    width = (int)CGImageGetWidth(image);
                    height = (int)CGImageGetHeight(image);

                    dest = CGImageDestinationCreateWithURL(dstUrl, jpegType, 1, IntPtr.Zero);
                    if (dest == IntPtr.Zero)
                    {
                        DebugLog("CGImageDestinationCreateWithURL 失败");
                        return false;
                    }

                    CGImageDestinationAddImage(dest, image, IntPtr.Zero);
                    var ok = CGImageDestinationFinalize(dest);
                    if (!ok)
                    {
                        DebugLog("CGImageDestinationFinalize 失败");
                    }

                    return ok;
                }
                finally
                {
                    if (dest != IntPtr.Zero)
                    {
                        CFRelease(dest);
                    }

                    if (image != IntPtr.Zero)
                    {
                        CFRelease(image);
                    }

                    CFRelease(options);
                    foreach (var n in numbers)
                    {
                        CFRelease(n);
                    }
                }
            }
            finally
            {
                CFRelease(srcRef);
            }
        }
        finally
        {
            CFRelease(srcUrl);
            CFRelease(dstUrl);
            CFRelease(jpegType);
        }
    }

    private static byte[] EncodePath(string path) => System.Text.Encoding.UTF8.GetBytes(path + "\0");

    private static void DebugLog(string message)
    {
        if (Environment.GetEnvironmentVariable("PB_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[MacImageIODecoder] {message}");
        }
    }

    // ---- 动态符号 ----

    private static readonly Dictionary<string, IntPtr> Libs = new();

    private static IntPtr Lib(string path)
    {
        if (Libs.TryGetValue(path, out var handle))
        {
            return handle;
        }

        handle = dlopen(path, 2 /* RTLD_NOW */);
        Libs[path] = handle;
        return handle;
    }

    /// <summary>取数据符号的地址（指针变量本身），不解引用。</summary>
    private static IntPtr SymAddr(string framework, string symbol) => dlsym(Lib(framework), symbol);

    /// <summary>取数据符号指向的值（对 CFStringRef 这类指针常量先解引用）。</summary>
    private static IntPtr SymRef(string framework, string symbol)
    {
        var addr = SymAddr(framework, symbol);
        return addr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(addr);
    }

    // ---- P/Invoke ----

    [DllImport(LibSystem)] private static extern IntPtr dlopen(string path, int mode);
    [DllImport(LibSystem)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);
    [DllImport(CF)] private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
        IntPtr alloc, byte[] buffer, long bufferLength, bool isDirectory);
    [DllImport(CF)] private static extern IntPtr CFDictionaryCreate(
        IntPtr alloc, IntPtr[] keys, IntPtr[] values, long count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CF)] private static extern IntPtr CFNumberCreate(IntPtr alloc, long type, ref long value);
    [DllImport(CF)] private static extern void CFRelease(IntPtr cf);

    [DllImport(CG)] private static extern long CGImageGetWidth(IntPtr image);
    [DllImport(CG)] private static extern long CGImageGetHeight(IntPtr image);

    [DllImport(IIO)] private static extern IntPtr CGImageSourceCreateWithURL(IntPtr url, IntPtr options);
    [DllImport(IIO)] private static extern IntPtr CGImageSourceCreateThumbnailAtIndex(IntPtr source, long index, IntPtr options);
    [DllImport(IIO)] private static extern IntPtr CGImageDestinationCreateWithURL(IntPtr url, IntPtr type, long count, IntPtr options);
    [DllImport(IIO)] private static extern void CGImageDestinationAddImage(IntPtr dest, IntPtr image, IntPtr properties);
    [DllImport(IIO)] private static extern bool CGImageDestinationFinalize(IntPtr dest);
}
