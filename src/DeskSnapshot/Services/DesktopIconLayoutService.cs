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
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint EddGetDeviceInterfaceName = 0x00000001;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint DisplayConfigGetSourceName = 1;
    private const uint DisplayConfigGetTargetName = 2;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int TextCapacity = 1024;

    public DesktopLayoutBackup Capture(string name, string note = "", bool isSafetyBackup = false)
    {
        var listView = FindDesktopListView();
        using var remote = RemoteListViewMemory.Open(listView);
        var count = SendListViewMessage(listView, LvmGetItemCount, 0, IntPtr.Zero).ToInt32();
        var icons = new List<DesktopIconPosition>(Math.Max(0, count));
        var environment = ReadEnvironment();

        for (var index = 0; index < count; index++)
        {
            remote.WriteItemRequest(index);
            SendListViewMessage(listView, LvmGetItemTextW, index, remote.ItemAddress);
            var iconName = DecodeRemoteText(remote.ReadTextBytes());

            SendListViewMessage(listView, LvmGetItemPosition, index, remote.PointAddress);
            var point = remote.ReadPoint();
            var monitor = FindMonitorForPoint(environment, point.X, point.Y);

            icons.Add(new DesktopIconPosition
            {
                Name = string.IsNullOrWhiteSpace(iconName) ? LocalizationService.Format("UnnamedIconFormat", index + 1) : iconName,
                X = point.X,
                Y = point.Y,
                CaptureOrder = index,
                MonitorId = monitor?.Id ?? string.Empty,
                MonitorOffsetX = monitor is null ? 0 : point.X - (monitor.Left - environment.VirtualLeft),
                MonitorOffsetY = monitor is null ? 0 : point.Y - (monitor.Top - environment.VirtualTop)
            });
        }

        return new DesktopLayoutBackup
        {
            Name = name,
            Note = note,
            IsSafetyBackup = isSafetyBackup,
            SchemaVersion = 2,
            Environment = environment,
            Icons = icons
        };
    }

    public RestoreResult Restore(DesktopLayoutBackup backup, bool followMonitorPositions = false)
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
        var remapped = 0;
        var currentEnvironment = followMonitorPositions ? ReadEnvironment() : null;

        foreach (var icon in backup.Icons.OrderBy(icon => icon.CaptureOrder))
        {
            if (!currentByName.TryGetValue(icon.Name, out var indexes) || indexes.Count == 0)
            {
                missing++;
                continue;
            }

            var targetPoint = new NativePoint { X = icon.X, Y = icon.Y };
            if (currentEnvironment is not null && TryMapToCurrentMonitor(backup, icon, currentEnvironment, out var mappedPoint))
            {
                targetPoint = mappedPoint;
                remapped++;
            }

            remote.WritePoint(targetPoint);
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

        return new RestoreResult(restored, missing, failed, remapped);
    }

    public DesktopEnvironment ReadEnvironment()
    {
        var environment = new DesktopEnvironment
        {
            VirtualLeft = GetSystemMetrics(76),
            VirtualTop = GetSystemMetrics(77),
            VirtualWidth = GetSystemMetrics(78),
            VirtualHeight = GetSystemMetrics(79),
            MonitorCount = GetSystemMetrics(80),
            Dpi = GetDpiForSystem()
        };
        var displayConfigIdentities = ReadDisplayConfigIdentities();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitorHandle, _, _, _) =>
        {
            var info = new NativeMonitorInfo
            {
                Size = Marshal.SizeOf<NativeMonitorInfo>(),
                DeviceName = string.Empty
            };
            if (!GetMonitorInfo(monitorHandle, ref info))
            {
                return true;
            }

            var displayDevice = new NativeDisplayDevice
            {
                Size = Marshal.SizeOf<NativeDisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceId = string.Empty,
                DeviceKey = string.Empty
            };
            var hasDisplayDevice = EnumDisplayDevices(info.DeviceName, 0, ref displayDevice, EddGetDeviceInterfaceName);
            displayConfigIdentities.TryGetValue(info.DeviceName, out var displayConfigIdentity);
            var friendlyName = !string.IsNullOrWhiteSpace(displayConfigIdentity.FriendlyName)
                ? displayConfigIdentity.FriendlyName
                : hasDisplayDevice && !string.IsNullOrWhiteSpace(displayDevice.DeviceString)
                ? displayDevice.DeviceString
                : info.DeviceName;
            var stableId = !string.IsNullOrWhiteSpace(displayConfigIdentity.DevicePath)
                ? displayConfigIdentity.DevicePath
                : hasDisplayDevice && !string.IsNullOrWhiteSpace(displayDevice.DeviceId)
                ? displayDevice.DeviceId
                : info.DeviceName;

            environment.Monitors.Add(new DesktopMonitor
            {
                Id = stableId,
                DeviceName = info.DeviceName,
                Name = friendlyName,
                Left = info.Monitor.Left,
                Top = info.Monitor.Top,
                Width = info.Monitor.Right - info.Monitor.Left,
                Height = info.Monitor.Bottom - info.Monitor.Top,
                IsPrimary = (info.Flags & MonitorInfoPrimary) != 0
            });
            return true;
        }, IntPtr.Zero);

        environment.MonitorCount = environment.Monitors.Count > 0
            ? environment.Monitors.Count
            : environment.MonitorCount;
        return environment;
    }

    private static Dictionary<string, DisplayConfigIdentity> ReadDisplayConfigIdentities()
    {
        var identities = new Dictionary<string, DisplayConfigIdentity>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (result != ErrorSuccess)
            {
                return identities;
            }

            var paths = new NativeDisplayConfigPathInfo[pathCount];
            var modes = new NativeDisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(
                QdcOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);
            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }
            if (result != ErrorSuccess)
            {
                return identities;
            }

            for (var index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                var sourceName = new NativeDisplayConfigSourceDeviceName
                {
                    Header = new NativeDisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigGetSourceName,
                        Size = (uint)Marshal.SizeOf<NativeDisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    },
                    ViewGdiDeviceName = string.Empty
                };
                var targetName = new NativeDisplayConfigTargetDeviceName
                {
                    Header = new NativeDisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigGetTargetName,
                        Size = (uint)Marshal.SizeOf<NativeDisplayConfigTargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    },
                    MonitorFriendlyDeviceName = string.Empty,
                    MonitorDevicePath = string.Empty
                };

                if (DisplayConfigGetDeviceInfo(ref sourceName) != ErrorSuccess ||
                    DisplayConfigGetDeviceInfo(ref targetName) != ErrorSuccess ||
                    string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
                {
                    continue;
                }

                identities[sourceName.ViewGdiDeviceName] = new DisplayConfigIdentity(
                    targetName.MonitorFriendlyDeviceName,
                    targetName.MonitorDevicePath);
            }

            return identities;
        }

        return identities;
    }

    private static DesktopMonitor? FindMonitorForPoint(DesktopEnvironment environment, int x, int y)
    {
        var containing = environment.Monitors.FirstOrDefault(monitor =>
        {
            var left = monitor.Left - environment.VirtualLeft;
            var top = monitor.Top - environment.VirtualTop;
            return x >= left && x < left + monitor.Width && y >= top && y < top + monitor.Height;
        });
        if (containing is not null)
        {
            return containing;
        }

        return environment.Monitors.OrderBy(monitor =>
        {
            var left = monitor.Left - environment.VirtualLeft;
            var top = monitor.Top - environment.VirtualTop;
            var nearestX = Math.Clamp(x, left, left + Math.Max(0, monitor.Width - 1));
            var nearestY = Math.Clamp(y, top, top + Math.Max(0, monitor.Height - 1));
            return Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2);
        }).FirstOrDefault();
    }

    private static bool TryMapToCurrentMonitor(
        DesktopLayoutBackup backup,
        DesktopIconPosition icon,
        DesktopEnvironment currentEnvironment,
        out NativePoint point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(icon.MonitorId) || backup.Environment.Monitors.Count == 0)
        {
            return false;
        }

        var sourceMonitor = backup.Environment.Monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.Id, icon.MonitorId, StringComparison.OrdinalIgnoreCase));
        if (sourceMonitor is null)
        {
            return false;
        }

        var currentMonitor = currentEnvironment.Monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.Id, sourceMonitor.Id, StringComparison.OrdinalIgnoreCase));
        currentMonitor ??= currentEnvironment.Monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.DeviceName, sourceMonitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (currentMonitor is null)
        {
            var sameName = currentEnvironment.Monitors
                .Where(monitor => string.Equals(monitor.Name, sourceMonitor.Name, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (sameName.Count == 1)
            {
                currentMonitor = sameName[0];
            }
        }

        if (currentMonitor is null)
        {
            return false;
        }

        var offsetX = Math.Clamp(icon.MonitorOffsetX, 0, Math.Max(0, currentMonitor.Width - 1));
        var offsetY = Math.Clamp(icon.MonitorOffsetY, 0, Math.Max(0, currentMonitor.Height - 1));
        point = new NativePoint
        {
            X = currentMonitor.Left - currentEnvironment.VirtualLeft + offsetX,
            Y = currentMonitor.Top - currentEnvironment.VirtualTop + offsetY
        };
        return true;
    }

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
            throw new InvalidOperationException(LocalizationService.Get("ExplorerDesktopNotFound"));
        }

        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
        if (listView == IntPtr.Zero)
        {
            throw new InvalidOperationException(LocalizationService.Get("DesktopIconListNotFound"));
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), LocalizationService.Get("ExplorerNotResponding"));
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
                throw new Win32Exception(Marshal.GetLastWin32Error(), LocalizationService.Get("ExplorerProcessUnavailable"));
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
                throw new Win32Exception(Marshal.GetLastWin32Error(), LocalizationService.Get("LayoutBufferAllocationFailed"));
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), LocalizationService.Get("LayoutBufferWriteFailed"));
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
                throw new Win32Exception(Marshal.GetLastWin32Error(), LocalizationService.Get("LayoutBufferReadFailed"));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplayConfigPathSourceInfo
    {
        public NativeLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplayConfigPathTargetInfo
    {
        public NativeLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public NativeDisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplayConfigPathInfo
    {
        public NativeDisplayConfigPathSourceInfo SourceInfo;
        public NativeDisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct NativeDisplayConfigModeInfo
    {
        private byte _reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public NativeLuid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDisplayConfigSourceDeviceName
    {
        public NativeDisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDisplayConfigTargetDeviceName
    {
        public NativeDisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    private readonly record struct DisplayConfigIdentity(string FriendlyName, string DevicePath);

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
    private delegate bool EnumMonitorsProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        EnumMonitorsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string deviceName,
        uint deviceNumber,
        ref NativeDisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint pathInfoArraySize,
        out uint modeInfoArraySize);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathInfoArraySize,
        [Out] NativeDisplayConfigPathInfo[] pathInfoArray,
        ref uint modeInfoArraySize,
        [Out] NativeDisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref NativeDisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref NativeDisplayConfigTargetDeviceName requestPacket);

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
