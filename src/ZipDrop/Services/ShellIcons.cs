using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipDrop.Core.Baskets;

namespace ZipDrop.Services;

/// <summary>
/// Windows' own file-type icons (the ones Explorer shows), cached per extension.
/// Uses SHGFI_USEFILEATTRIBUTES so it never touches the disk: fast, and works for missing files.
/// Must be called on the UI (STA) thread.
/// </summary>
internal static class ShellIcons
{
    public enum Size
    {
        Small = 1,      // SHIL_SMALL (16px at 100%)
        Large = 0,      // SHIL_LARGE (32px)
        ExtraLarge = 2, // SHIL_EXTRALARGE (48px)
    }

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(BasketItem item, Size size) =>
        For(item.Kind == BasketItemKind.Folder ? null : Path.GetExtension(item.FullPath), size);

    /// <summary>Icon for a file extension (".pdf"), or for a folder when null.</summary>
    public static ImageSource? For(string? extension, Size size)
    {
        var key = $"{extension ?? "<folder>"}|{size}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var icon = Load(extension, size);
        Cache[key] = icon;
        return icon;
    }

    private static ImageSource? Load(string? extension, Size size)
    {
        try
        {
            var info = new SHFILEINFO();
            var isFolder = extension is null;
            var name = isFolder ? "folder" : "file" + extension;
            var attributes = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (SHGetFileInfo(name, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                    SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES) == IntPtr.Zero)
                return null;

            var iid = typeof(IImageList).GUID;
            if (SHGetImageList((int)size, ref iid, out var list) != 0) return null;
            list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var hIcon);
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Interop (kept local: only used here) ----
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int ILD_TRANSPARENT = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Only the vtable prefix up to GetIcon is declared (order matters).</summary>
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }
}
