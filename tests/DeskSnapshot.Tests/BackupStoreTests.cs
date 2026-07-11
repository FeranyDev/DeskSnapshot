using DeskSnapshot.Models;
using DeskSnapshot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class BackupStoreTests
{
    private string _testFolder = null!;
    private string _filePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "DeskSnapshot.Tests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_testFolder, "backups.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testFolder))
        {
            Directory.Delete(_testFolder, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsEmptyList()
    {
        var store = new BackupStore(_filePath);

        var result = await store.LoadAsync();

        Assert.AreEqual(0, result.Count);
        Assert.IsTrue(Directory.Exists(_testFolder));
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsBackupRelationshipsMonitorsAndIcons()
    {
        var parent = TestLayoutFactory.Layout(
            [TestLayoutFactory.Icon("Example", 120, 240, 3)],
            monitors: [TestLayoutFactory.Monitor("DISPLAY-A", -1920, 0, 1920, 1080, true)]);
        parent.Name = "Parent";
        parent.Note = "Note";
        parent.TriggerReason = "manual";
        var child = TestLayoutFactory.Backup(safety: true, relatedId: parent.Id);
        child.Name = "Safety";
        var store = new BackupStore(_filePath);

        await store.SaveAsync([parent, child]);
        var result = await store.LoadAsync();

        Assert.AreEqual(2, result.Count);
        var loadedParent = result.Single(item => item.Id == parent.Id);
        var loadedChild = result.Single(item => item.Id == child.Id);
        Assert.AreEqual("Parent", loadedParent.Name);
        Assert.AreEqual("Note", loadedParent.Note);
        Assert.AreEqual("DISPLAY-A", loadedParent.Environment.Monitors.Single().Id);
        Assert.AreEqual("Example", loadedParent.Icons.Single().Name);
        Assert.AreEqual(120, loadedParent.Icons.Single().X);
        Assert.AreEqual(parent.Id, loadedChild.RelatedBackupId);
        Assert.IsTrue(loadedChild.IsSafetyBackup);
    }

    [TestMethod]
    public async Task SaveAsync_ReplacesExistingFileAndRemovesTemporaryFile()
    {
        var store = new BackupStore(_filePath);
        var first = TestLayoutFactory.Backup();
        var second = TestLayoutFactory.Backup();

        await store.SaveAsync([first]);
        await store.SaveAsync([second]);
        var result = await store.LoadAsync();

        CollectionAssert.AreEqual(new[] { second.Id }, result.Select(item => item.Id).ToArray());
        Assert.IsFalse(File.Exists(_filePath + ".tmp"));
    }

    [TestMethod]
    public async Task LoadAsync_WithMalformedJson_QuarantinesFileAndReturnsEmptyList()
    {
        var store = new BackupStore(_filePath);
        await File.WriteAllTextAsync(_filePath, "{ malformed json");

        var result = await store.LoadAsync();

        Assert.AreEqual(0, result.Count);
        Assert.IsFalse(File.Exists(_filePath));
        Assert.AreEqual(1, Directory.GetFiles(_testFolder, "backups.json.damaged-*").Length);
    }

    [TestMethod]
    public async Task LoadAsync_WithJsonNull_ReturnsEmptyList()
    {
        var store = new BackupStore(_filePath);
        await File.WriteAllTextAsync(_filePath, "null");

        var result = await store.LoadAsync();

        Assert.AreEqual(0, result.Count);
        Assert.IsTrue(File.Exists(_filePath));
    }
}
