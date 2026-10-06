using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace ZipDrop.Services;

/// <summary>
/// A data object carrying real files (CF_HDROP), as Explorer would put on the clipboard or in a drag.
/// Browsers (Gmail, WhatsApp Web, upload forms), Teams, Outlook and Explorer all accept it.
/// </summary>
internal static class FileDataObject
{
    private const int DropEffectCopy = 1;

    public static DataObject Create(params string[] paths)
    {
        var data = new DataObject();
        var list = new StringCollection();
        list.AddRange(paths);
        data.SetFileDropList(list);
        // Tell pasting apps (Explorer) this is a copy, never a cut/move of the user's ZIP.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(DropEffectCopy)));
        return data;
    }
}
