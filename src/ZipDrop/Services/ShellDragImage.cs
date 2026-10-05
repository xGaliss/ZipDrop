using System.Runtime.InteropServices;
using System.Windows;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>
/// Keeps Explorer's drag image (thumbnails + "Copy to" badge) visible while
/// hovering ZipDrop. Without IDropTargetHelper the image disappears over WPF windows.
/// Purely cosmetic: every call is best-effort.
/// </summary>
internal static class ShellDragImage
{
    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, IComDataObject pDataObject, ref POINT ppt, int dwEffect);
        void DragLeave();
        void DragOver(ref POINT ppt, int dwEffect);
        void Drop(IComDataObject pDataObject, ref POINT ppt, int dwEffect);
        void Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    }

    [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
    private class DragDropHelper;

    private static IDropTargetHelper? _helper;

    private static IDropTargetHelper? Helper
    {
        get
        {
            if (_helper is not null) return _helper;
            try { _helper = (IDropTargetHelper)new DragDropHelper(); }
            catch (COMException) { }
            return _helper;
        }
    }

    private static POINT ScreenPoint(Window window, DragEventArgs e)
    {
        var p = window.PointToScreen(e.GetPosition(window));
        return new POINT((int)p.X, (int)p.Y);
    }

    public static void Enter(Window window, IntPtr hwnd, DragEventArgs e)
    {
        if (e.Data is not IComDataObject data) return;
        try
        {
            var pt = ScreenPoint(window, e);
            Helper?.DragEnter(hwnd, data, ref pt, (int)e.Effects);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException) { }
    }

    public static void Over(Window window, DragEventArgs e)
    {
        try
        {
            var pt = ScreenPoint(window, e);
            Helper?.DragOver(ref pt, (int)e.Effects);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
    }

    public static void Leave()
    {
        try { Helper?.DragLeave(); }
        catch (COMException) { }
    }

    public static void Drop(Window window, DragEventArgs e)
    {
        if (e.Data is not IComDataObject data) return;
        try
        {
            var pt = ScreenPoint(window, e);
            Helper?.Drop(data, ref pt, (int)e.Effects);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException) { }
    }
}
