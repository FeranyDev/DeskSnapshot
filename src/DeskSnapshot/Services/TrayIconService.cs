using System.Runtime.InteropServices;

namespace DeskSnapshot.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8001;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint CommandOpen = 1001;
    private const uint CommandBackup = 1002;
    private const uint CommandExit = 1003;
    private static readonly UIntPtr SubclassId = new(0x44534E50);

    private readonly IntPtr _windowHandle;
    private readonly SubclassProc _subclassProc;
    private NotifyIconData _iconData;
    private IntPtr _iconHandle;
    private bool _isVisible;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? BackupRequested;
    public event Action? ExitRequested;

    public TrayIconService(IntPtr windowHandle, string iconPath)
    {
        _windowHandle = windowHandle;
        _subclassProc = WindowSubclassProc;
        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
        if (_iconHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(LocalizationService.Get("TrayIconLoadFailed"));
        }

        _iconData = CreateIconData();
        if (!SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, UIntPtr.Zero))
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
            throw new InvalidOperationException(LocalizationService.Get("TrayMessageHookFailed"));
        }
    }

    public void SetVisible(bool visible)
    {
        if (_disposed || visible == _isVisible)
        {
            return;
        }

        if (!ShellNotifyIcon(visible ? NimAdd : NimDelete, ref _iconData))
        {
            throw new InvalidOperationException(LocalizationService.Get(visible ? "TrayIconCreateFailed" : "TrayIconRemoveFailed"));
        }

        _isVisible = visible;
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isVisible || _disposed)
        {
            return;
        }

        var data = _iconData;
        data.uFlags = NifInfo;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = 0x00000001;
        ShellNotifyIcon(NimModify, ref data);
    }

    private NotifyIconData CreateIconData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _windowHandle,
        uID = IconId,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = CallbackMessage,
        hIcon = _iconHandle,
        szTip = LocalizationService.Get("WindowTitle"),
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private IntPtr WindowSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData)
    {
        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (mouseMessage == WmLeftButtonDoubleClick)
            {
                OpenRequested?.Invoke();
                return IntPtr.Zero;
            }

            if (mouseMessage is WmRightButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, CommandOpen, LocalizationService.Get("TrayOpen"));
            AppendMenu(menu, MfString, CommandBackup, LocalizationService.Get("TrayBackupNow"));
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CommandExit, LocalizationService.Get("TrayExit"));
            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
            switch (command)
            {
                case CommandOpen:
                    OpenRequested?.Invoke();
                    break;
                case CommandBackup:
                    BackupRequested?.Invoke();
                    break;
                case CommandExit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isVisible)
        {
            ShellNotifyIcon(NimDelete, ref _iconData);
            _isVisible = false;
        }

        RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr subclassId, UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
