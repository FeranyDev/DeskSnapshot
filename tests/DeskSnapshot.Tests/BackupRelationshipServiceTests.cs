using DeskSnapshot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class BackupRelationshipServiceTests
{
    [TestMethod]
    public void GetDescendantIds_ReturnsChildrenAndNestedGrandchildren()
    {
        var parent = TestLayoutFactory.Backup();
        var firstChild = TestLayoutFactory.Backup(relatedId: parent.Id);
        var secondChild = TestLayoutFactory.Backup(relatedId: parent.Id);
        var grandchild = TestLayoutFactory.Backup(relatedId: firstChild.Id);
        var unrelated = TestLayoutFactory.Backup();

        var result = BackupRelationshipService.GetDescendantIds(
            [parent, firstChild, secondChild, grandchild, unrelated],
            parent.Id);

        CollectionAssert.AreEquivalent(
            new[] { firstChild.Id, secondChild.Id, grandchild.Id },
            result.ToArray());
    }

    [TestMethod]
    public void GetDescendantIds_ChildSelectionDoesNotIncludeParentOrSibling()
    {
        var parent = TestLayoutFactory.Backup();
        var child = TestLayoutFactory.Backup(relatedId: parent.Id);
        var sibling = TestLayoutFactory.Backup(relatedId: parent.Id);

        var result = BackupRelationshipService.GetDescendantIds([parent, child, sibling], child.Id);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetDescendantIds_CycleTerminatesWithoutReturningStartingNode()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var first = TestLayoutFactory.Backup(firstId, relatedId: thirdId);
        var second = TestLayoutFactory.Backup(secondId, relatedId: firstId);
        var third = TestLayoutFactory.Backup(thirdId, relatedId: secondId);

        var result = BackupRelationshipService.GetDescendantIds([first, second, third], firstId);

        CollectionAssert.AreEquivalent(new[] { secondId, thirdId }, result.ToArray());
        Assert.IsFalse(result.Contains(firstId));
    }

    [TestMethod]
    public void GetDescendantIds_UnknownParent_ReturnsEmptyList()
    {
        var result = BackupRelationshipService.GetDescendantIds(
            [TestLayoutFactory.Backup()],
            Guid.NewGuid());

        Assert.AreEqual(0, result.Count);
    }
}
