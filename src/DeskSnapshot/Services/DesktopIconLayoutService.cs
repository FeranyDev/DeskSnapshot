using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public sealed class DesktopIconLayoutService
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmGetItemPosition = LvmFirst + 16;
    private const uint LvmGetItemTextW = LvmFirst + 115;
    private const uint LvmSetItemPosition32 = LvmFirst + 49;
    private const uint LvifText = 0x0001;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint SmtoAbortIfHung = 0x0002;
    private const int TextCapacity = 1024;

    public DesktopLayoutBackup Capture(string name, string note = "", bool isSafetyBackup = false)
    {
        var listView = FindDesktopListView();
        using var remote = RemoteListViewMemory.Open(listView);
        var count = SendListViewMessage(listView, LvmGetItemCount, 0, IntPtr.Zero).ToInt32();
        var icons = new List<DesktopIconPosition>(Math.Max(0, count));

        for (var index = 0; index < count; index++)
        {
            remote.WriteItemRequest(index);
            SendListViewMessage(listView, LvmGetItemTextW, index, remote.ItemAddress);
            var iconName = DecodeRemoteText(remote.ReadTextBytes());

            SendListViewMessage(listView, LvmGetItemPosition, index, remote.PointAddress);
            var point = remote.ReadPoint();

            icons.Add(new DesktopIconPosition
            {
                Name = string.IsNullOrWhiteSpace(iconName) ? $"未命名图标 #{index + 1}" : iconName,
                X = point.X,
                Y = point.Y,
                CaptureOrder = index
            });
        }

        return new DesktopLayoutBackup
        {
            Name = name,
            Note = note,
            IsSafetyBackup = isSafetyBackup,
            Environment = ReadEnvironment(),
            Icons = icons
        };
    }

    public RestoreResult Restore(DesktopLayoutBackup backup)
    {
        var listView = FindDesktopListView();
        using var remote = RemoteListViewMemory.Open(listView);
        var count = SendListViewMessage(listView, LvmGetItemCount, 0, IntPtr.Zero).ToInt32();
        var currentByName = new Dictionary<string, Queue<int>>(StringComparer.CurrentCultureIgnoreCase);

        for (var index = 0; index < count; index++)
        {
            remote.WriteItemRequest(index);
            SendListViewMessage(listView, LvmGetItemTextW, index, remote.ItemAddress);
            var iconName = DecodeRemoteText(remote.ReadTextBytes());

            if (!currentByName.TryGetValue(iconName, out var indexes))
            {
                indexes = new Queue<int>();
                currentByName.Add(iconName, indexes);
            }

            indexes.Enqueue(index);
        }

        var restored = 0;
        var missing = 0;
        var failed = 0;

        foreach (var icon in backup.Icons.OrderBy(icon => icon.CaptureOrder))
        {
            if (!currentByName.TryGetValue(icon.Name, out var indexes) || indexes.Count == 0)
            {
                missing++;
                continue;
            }

            remote.WritePoint(new NativePoint { X = icon.X, Y = icon.Y });
            var result = SendListViewMessage(listView, LvmSetItemPosition32, indexes.Dequeue(), remote.PointAddress);
            if (result == IntPtr.Zero)
            {
                failed++;
            }
            else
            {
                restored++;
            }
        }

        return new RestoreResult(restored, missing, failed);
    }

    public DesktopEnvironment ReadEnvironment() => new()
    {
        VirtualLeft = GetSystemMetrics(76),
        VirtualTop = GetSystemMetrics(77),
        VirtualWidth = GetSystemMetrics(78),
        VirtualHeight = GetSystemMetrics(79),
        MonitorCount = GetSystemMetrics(80),
        Dpi = GetDpiForSystem()
    };

    private static string DecodeRemoteText(byte[] bytes)
    {
        var byteCount = 0;
        while (byteCount + 1 < bytes.Length)
        {
            if (bytes[byteCount] == 0 && bytes[byteCount + 1] == 0)
            {
                break;
            }

            byteCount += sizeof(char);
        }

        return Encoding.Unicode.GetString(bytes, 0, byteCount);
    }

    private static IntPtr FindDesktopListView()
    {
        var shellWindow = GetShellWindow();
        var defView = FindWindowEx(shellWindow, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            EnumWindows((topLevelWindow, _) =>
            {
                var candidate = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (candidate == IntPtr.Zero)
                {
                    return true;
                }

                defView = candidate;
                return false;
            }, IntPtr.Zero);
        }

        if (defView == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法找到 Windows Explorer 桌面视图。请确认 Explorer 正在运行。");
        }

        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法找到桌面图标列表。当前桌面外壳可能不受支持。");
        }

        return listView;
    }

    private static IntPtr SendListViewMessage(IntPtr window, uint message, int wParam, IntPtr lParam)
    {
        var succeeded = SendMessageTimeout(
            window,
            message,
            new IntPtr(wParam),
            lParam,
            SmtoAbortIfHung,
            3000,
            out var result);

        if (succeeded == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Explorer 未响应桌面布局请求。");
        }

        return result;
    }

    private sealed class RemoteListViewMemory : IDisposable
    {
        private readonly IntPtr _process;
        private readonly int _itemSize = Marshal.SizeOf<NativeLvItem>();
        private readonly int _pointSize = Marshal.SizeOf<NativePoint>();
        private readonly int _textBytes = TextCapacity * sizeof(char);

        public IntPtr ItemAddress { get; }
        public IntPtr PointAddress { get; }
        public IntPtr TextAddress { get; }

        private RemoteListViewMemory(IntPtr process)
        {
            _process = process;
            ItemAddress = Allocate(_itemSize);
            PointAddress = Allocate(_pointSize);
            TextAddress = Allocate(_textBytes);
        }

        public static RemoteListViewMemory Open(IntPtr listView)
        {
            GetWindowThreadProcessId(listView, out var processId);
            var process = OpenProcess(
                ProcessQueryInformation | ProcessVmOperation | ProcessVmRead | ProcessVmWrite,
                false,
                processId);

            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows Explorer 进程。");
            }

            try
            {
                return new RemoteListViewMemory(process);
            }
            catch
            {
                CloseHandle(process);
                throw;
            }
        }

        public void WriteItemRequest(int index)
        {
            var item = new NativeLvItem
            {
                Mask = LvifText,
                Item = index,
                SubItem = 0,
                Text = TextAddress,
                TextMax = TextCapacity
            };
            WriteStruct(ItemAddress, item);
        }

        public void WritePoint(NativePoint point) => WriteStruct(PointAddress, point);

        public NativePoint ReadPoint()
        {
            var bytes = Read(PointAddress, _pointSize);
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<NativePoint>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        public byte[] ReadTextBytes() => Read(TextAddress, _textBytes);

        private IntPtr Allocate(int size)
        {
            var address = VirtualAllocEx(_process, IntPtr.Zero, (nuint)size, MemCommit | MemReserve, PageReadWrite);
            if (address == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法分配桌面布局读取缓冲区。");
            }

            return address;
        }

        private void WriteStruct<T>(IntPtr address, T value) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var local = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, local, false);
                if (!WriteProcessMemory(_process, address, local, (nuint)size, out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入桌面布局缓冲区。");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(local);
            }
        }

        private byte[] Read(IntPtr address, int size)
        {
            var bytes = new byte[size];
            if (!ReadProcessMemory(_process, address, bytes, (nuint)size, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取桌面布局缓冲区。");
            }

            return bytes;
        }

        public void Dispose()
        {
            if (ItemAddress != IntPtr.Zero) VirtualFreeEx(_process, ItemAddress, 0, MemRelease);
            if (PointAddress != IntPtr.Zero) VirtualFreeEx(_process, PointAddress, 0, MemRelease);
            if (TextAddress != IntPtr.Zero) VirtualFreeEx(_process, TextAddress, 0, MemRelease);
            CloseHandle(_process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeLvItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public IntPtr Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public IntPtr ColumnIndexes;
        public IntPtr ColumnFormats;
        public int Group;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, IntPtr buffer, nuint size, out nuint bytesWritten);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
