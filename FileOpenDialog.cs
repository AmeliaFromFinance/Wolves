using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wolves;

/// <summary>
/// Thin wrapper around the Windows Common Item Dialog (<c>IFileOpenDialog</c>) so a console
/// application can show the standard "Open" dialog without pulling in WinForms or WPF.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileOpenDialog
{
    private const int HResultCancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>
    /// Shows the dialog and returns the selected file's full path, or <c>null</c> if the user cancelled.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filterName">Display name of the file filter (e.g. "Unreal mappings (*.usmap)").</param>
    /// <param name="filterSpec">Filter pattern(s), semicolon separated (e.g. "*.usmap").</param>
    /// <param name="defaultExtension">Default extension without the dot (e.g. "usmap").</param>
    /// <param name="initialDirectory">Optional folder to open the dialog in.</param>
    public static string? ShowDialog(
        string title,
        string filterName,
        string filterSpec,
        string? defaultExtension = null,
        string? initialDirectory = null)
    {
        string? result = null;
        Exception? error = null;

        // The common item dialog must run on an STA thread; async Main is not one.
        var thread = new Thread(() =>
        {
            try
            {
                result = ShowCore(title, filterName, filterSpec, defaultExtension, initialDirectory);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            throw new InvalidOperationException("The file open dialog failed.", error);

        return result;
    }

    private static string? ShowCore(
        string title,
        string filterName,
        string filterSpec,
        string? defaultExtension,
        string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            dialog.SetTitle(title);

            var filters = new[]
            {
                new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = filterSpec }
            };
            dialog.SetFileTypes((uint)filters.Length, filters);
            dialog.SetFileTypeIndex(1);

            if (!string.IsNullOrEmpty(defaultExtension))
                dialog.SetDefaultExtension(defaultExtension);

            dialog.GetOptions(out FOS options);
            options |= FOS.FOS_FORCEFILESYSTEM | FOS.FOS_FILEMUSTEXIST | FOS.FOS_PATHMUSTEXIST | FOS.FOS_NOCHANGEDIR;
            dialog.SetOptions(options);

            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                var shellItemGuid = typeof(IShellItem).GUID;
                int hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref shellItemGuid, out IShellItem? folder);
                if (hr == 0 && folder is not null)
                {
                    try { dialog.SetFolder(folder); }
                    finally { Marshal.ReleaseComObject(folder); }
                }
            }

            int showResult = dialog.Show(IntPtr.Zero);
            if (showResult == HResultCancelled)
                return null;
            Marshal.ThrowExceptionForHR(showResult);

            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItem? ppv);

    // ----------------------------------------------------------------------------- COM definitions

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    [ClassInterface(ClassInterfaceType.None)]
    private class FileOpenDialogRCW
    {
    }

    [Flags]
    private enum FOS : uint
    {
        FOS_OVERWRITEPROMPT = 0x00000002,
        FOS_STRICTFILETYPES = 0x00000004,
        FOS_NOCHANGEDIR = 0x00000008,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_ALLNONSTORAGEITEMS = 0x00000080,
        FOS_NOVALIDATE = 0x00000100,
        FOS_ALLOWMULTISELECT = 0x00000200,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_CREATEPROMPT = 0x00002000,
        FOS_SHAREAWARE = 0x00004000,
        FOS_NOREADONLYRETURN = 0x00008000,
        FOS_NOTESTFILECREATE = 0x00010000,
        FOS_HIDEMRUPLACES = 0x00020000,
        FOS_HIDEPINNEDPLACES = 0x00040000,
        FOS_NODEREFERENCELINKS = 0x00100000,
        FOS_DONTADDTORECENT = 0x02000000,
        FOS_FORCESHOWHIDDEN = 0x10000000,
        FOS_DEFAULTNOMINIMODE = 0x20000000,
        FOS_FORCEPREVIEWPANEON = 0x40000000
    }

    private enum SIGDN : uint
    {
        SIGDN_NORMALDISPLAY = 0x00000000,
        SIGDN_PARENTRELATIVEPARSING = 0x80018001,
        SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
        SIGDN_PARENTRELATIVEEDITING = 0x80031001,
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004C000,
        SIGDN_FILESYSPATH = 0x80058000,
        SIGDN_URL = 0x80068000,
        SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007C001,
        SIGDN_PARENTRELATIVE = 0x80080001
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    /// <summary>
    /// IFileOpenDialog, flattened (IModalWindow -> IFileDialog -> IFileOpenDialog) in vtable order.
    /// </summary>
    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig]
        int Show(IntPtr hwndOwner);

        // IFileDialog
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);

        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }
}
