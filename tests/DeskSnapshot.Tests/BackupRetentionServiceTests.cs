using DeskSnapshot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class BackupRetentionServiceTests
{
    [TestMethod]
    public void TrimAutomaticBackups_RemovesOldestAutomaticBackupsOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-3), automatic: true);
        var middle = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-2), automatic: true);
        var newest = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-1), automatic: true);
        var manual = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-4));
        var safety = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-5), safety: true);
        var backups = new[] { oldest, manual, newest, safety, middle }.ToList();

        var removed = BackupRetentionService.TrimAutomaticBackups(backups, 2);

        CollectionAssert.AreEqual(new[] { oldest.Id }, removed.Select(item => item.Id).ToArray());
        Assert.IsFalse(backups.Contains(oldest));
        Assert.IsTrue(backups.Contains(middle));
        Assert.IsTrue(backups.Contains(newest));
        Assert.IsTrue(backups.Contains(manual));
        Assert.IsTrue(backups.Contains(safety));
    }

    [TestMethod]
    public void TrimAutomaticBackups_ClampsRetentionToMinimumOne()
    {
        var now = DateTimeOffset.UtcNow;
        var old = TestLayoutFactory.Backup(createdAt: now.AddMinutes(-1), automatic: true);
        var newest = TestLayoutFactory.Backup(createdAt: now, automatic: true);
        var backups = new[] { old, newest }.ToList();

        var removed = BackupRetentionService.TrimAutomaticBackups(backups, 0);

        Assert.AreEqual(1, removed.Count);
        CollectionAssert.AreEqual(new[] { newest.Id }, backups.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void TrimAutomaticBackups_ClampsRetentionToMaximumTwoHundred()
    {
        var now = DateTimeOffset.UtcNow;
        var backups = Enumerable.Range(0, 201)
            .Select(index => TestLayoutFactory.Backup(createdAt: now.AddMinutes(index), automatic: true))
            .ToList();

        var removed = BackupRetentionService.TrimAutomaticBackups(backups, 500);

        Assert.AreEqual(1, removed.Count);
        Assert.AreEqual(200, backups.Count);
        Assert.AreEqual(now, removed[0].CreatedAt);
    }

    [TestMethod]
    public void TrimAutomaticBackups_WithNoAutomaticBackups_DoesNothing()
    {
        var backups = new[]
        {
            TestLayoutFactory.Backup(),
            TestLayoutFactory.Backup(safety: true)
        }.ToList();

        var removed = BackupRetentionService.TrimAutomaticBackups(backups, 10);

        Assert.AreEqual(0, removed.Count);
        Assert.AreEqual(2, backups.Count);
    }

    [TestMethod]
    public void TrimAutomaticBackups_PreservesAutomaticBackupUsedAsDisplayProfile()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = TestLayoutFactory.Backup(createdAt: now.AddHours(-2), automatic: true);
        profile.DisplayProfileName = "Docked";
        var old = TestLayoutFactory.Backup(createdAt: now.AddHours(-1), automatic: true);
        var newest = TestLayoutFactory.Backup(createdAt: now, automatic: true);
        var backups = new[] { profile, old, newest }.ToList();

        var removed = BackupRetentionService.TrimAutomaticBackups(backups, 1);

        CollectionAssert.AreEqual(new[] { old.Id }, removed.Select(item => item.Id).ToArray());
        Assert.IsTrue(backups.Contains(profile));
        Assert.IsTrue(backups.Contains(newest));
    }
}
