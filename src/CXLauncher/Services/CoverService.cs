using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CXLauncher.Services;

public sealed class CoverService(AppPaths paths)
{
    private readonly SemaphoreSlim decoders = new(2, 2);
    private readonly object cacheGate = new();
    private readonly Dictionary<string, BitmapSource> images = [];
    private readonly LinkedList<string> recent = [];
    public int CachedImageCount { get { lock (cacheGate) return images.Count; } }

    public async Task<string> ImportAsync(string source)
    {
        await decoders.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (new FileInfo(source).Length > 30 * 1024 * 1024) throw new InvalidDataException("请选择小于 30 MB 的本地图片。");
                var bitmap = Decode(source, 960);
                Directory.CreateDirectory(paths.Covers);
                var id = Guid.NewGuid().ToString("N");
                var destination = paths.CoverPath(id)!;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                try { using var file = File.Create(destination); encoder.Save(file); }
                catch { if (File.Exists(destination)) File.Delete(destination); throw; }
                return id;
            });
        }
        finally { decoders.Release(); }
    }

    public async Task<BitmapSource?> GetAsync(string? coverId, string target, CancellationToken cancellationToken)
    {
        var key = coverId ?? "icon:" + target;
        lock (cacheGate)
        {
            if (images.TryGetValue(key, out var cached))
            { recent.Remove(key); recent.AddFirst(key); return cached; }
        }
        await decoders.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() =>
            {
                var source = paths.CoverPath(coverId);
                try
                {
                    if (source is not null && File.Exists(source))
                    {
                        Directory.CreateDirectory(paths.Thumbnails);
                        var thumbnail = Path.Combine(paths.Thumbnails, coverId + ".png");
                        if (!File.Exists(thumbnail))
                        {
                            var decoded = Decode(source, 256);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(decoded));
                            try { using var stream = new FileStream(thumbnail, FileMode.Create, FileAccess.Write, FileShare.None); encoder.Save(stream); }
                            catch (IOException) { return decoded; }
                            return decoded;
                        }
                        try { return Decode(thumbnail, 256); }
                        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
                        { File.Delete(thumbnail); return Decode(source, 256); }
                    }
                    return ReadIcon(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException)
                { return null; }
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not null)
            {
                lock (cacheGate)
                {
                    images[key] = result;
                    recent.Remove(key); recent.AddFirst(key);
                    while (images.Count > 64)
                    { var oldest = recent.Last!.Value; recent.RemoveLast(); images.Remove(oldest); }
                }
            }
            return result;
        }
        finally { decoders.Release(); }
    }

    private static BitmapSource Decode(string source, int width)
    {
        using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var metadata = BitmapDecoder.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        if (metadata.PixelWidth > 32768 || metadata.PixelHeight > 32768 || (long)metadata.PixelWidth * metadata.PixelHeight > 100_000_000)
            throw new InvalidDataException("图片尺寸过大，请选择不超过 1 亿像素的图片。");
        var decodeByHeight = (double)metadata.PixelHeight / metadata.PixelWidth > 2;
        file.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeByHeight) image.DecodePixelHeight = Math.Min(width * 2, metadata.PixelHeight);
        else image.DecodePixelWidth = Math.Min(width, metadata.PixelWidth);
        image.StreamSource = file; image.EndInit(); image.Freeze();
        return image;
    }

    private static BitmapSource? ReadIcon(string path)
    {
        if (!File.Exists(path)) return null;
        if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 /* SHGFI_ICON */) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
            image.Freeze(); return image;
        }
        finally { DestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
