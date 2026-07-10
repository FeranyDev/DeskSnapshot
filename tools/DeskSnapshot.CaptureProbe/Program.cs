using DeskSnapshot.Services;
using System.Text.Json;

try
{
    var service = new DesktopIconLayoutService();
    var backup = await Task.Run(() => service.Capture("诊断备份"));
    Console.WriteLine($"Capture succeeded: {backup.Icons.Count} icons");
    Console.WriteLine($"Environment: {backup.Environment.VirtualWidth}x{backup.Environment.VirtualHeight}, DPI {backup.Environment.Dpi}");
    foreach (var icon in backup.Icons.Take(10))
    {
        Console.WriteLine($"[{icon.CaptureOrder}] {icon.Name}: ({icon.X}, {icon.Y})");
    }
    var json = JsonSerializer.Serialize(backup);
    Console.WriteLine($"JSON serialization succeeded: {json.Length} chars");

    var probePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskSnapshot",
        "capture-probe.json");
    var store = new BackupStore(probePath);
    await store.SaveAsync([backup]);
    var reloaded = await store.LoadAsync();
    Console.WriteLine($"Storage round-trip succeeded: {reloaded.Count} backup, {new FileInfo(probePath).Length} bytes");
    File.Delete(probePath);

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
