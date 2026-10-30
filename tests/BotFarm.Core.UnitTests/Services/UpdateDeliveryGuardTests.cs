using BotFarm.Core.Services;
using BotFarm.TestKit;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class UpdateDeliveryGuardTests
{
    private ManualTimeProvider _timeProvider = null!;
    private UpdateDeliveryGuard _guard = null!;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new ManualTimeProvider();
        _guard = new UpdateDeliveryGuard(_timeProvider);
    }

    [Test]
    public void TryAcceptUpdate_FirstDelivery_ReturnsTrue()
    {
        Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.True);
    }

    [Test]
    public void TryAcceptUpdate_SameIdWithinWindow_ReturnsFalse()
    {
        _guard.TryAcceptUpdate("BotA", 1);
        _timeProvider.Advance(UpdateDeliveryGuard.DuplicateWindow - TimeSpan.FromSeconds(1));

        Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.False);
    }

    [Test]
    public void TryAcceptUpdate_SameIdAfterWindow_ReturnsTrue()
    {
        _guard.TryAcceptUpdate("BotA", 1);
        _timeProvider.Advance(UpdateDeliveryGuard.DuplicateWindow);

        Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.True);
    }

    [Test]
    public void TryAcceptUpdate_SameIdForDifferentBots_AcceptsBoth()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.True);
            Assert.That(_guard.TryAcceptUpdate("BotB", 1), Is.True);
        }
    }

    [Test]
    public void TryAcceptUpdate_WhenLimitExceeded_DropsOldestIds()
    {
        for (var id = 0; id <= UpdateDeliveryGuard.MaxTrackedUpdatesPerBot; id++)
        {
            _guard.TryAcceptUpdate("BotA", id);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_guard.TryAcceptUpdate("BotA", 0), Is.True, "oldest id should have been dropped");
            Assert.That(_guard.TryAcceptUpdate("BotA", UpdateDeliveryGuard.MaxTrackedUpdatesPerBot), Is.False);
        }
    }

    [Test]
    public void TryAcquireAlertSlot_SecondCallWithinInterval_ReturnsFalse()
    {
        Assert.That(_guard.TryAcquireAlertSlot("BotA"), Is.True);
        _timeProvider.Advance(UpdateDeliveryGuard.AlertInterval - TimeSpan.FromSeconds(1));

        Assert.That(_guard.TryAcquireAlertSlot("BotA"), Is.False);
    }

    [Test]
    public void TryAcquireAlertSlot_AfterInterval_ReturnsTrueAgain()
    {
        _guard.TryAcquireAlertSlot("BotA");
        _timeProvider.Advance(UpdateDeliveryGuard.AlertInterval);

        Assert.That(_guard.TryAcquireAlertSlot("BotA"), Is.True);
    }

    [Test]
    public void TryAcquireAlertSlot_IsTrackedPerBot()
    {
        _guard.TryAcquireAlertSlot("BotA");

        Assert.That(_guard.TryAcquireAlertSlot("BotB"), Is.True);
    }

    [Test]
    public void TryReleaseForRetry_BelowMax_ReleasesIdAndCountsAttempts()
    {
        _guard.TryAcceptUpdate("BotA", 1);

        var released = _guard.TryReleaseForRetry("BotA", 1, 5, out var attempts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(released, Is.True);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.True, "released id should be accepted again");
        }

        _guard.TryReleaseForRetry("BotA", 1, 5, out attempts);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void TryReleaseForRetry_AtMax_KeepsIdAcceptedAndClearsCount()
    {
        _guard.TryAcceptUpdate("BotA", 1);
        for (var i = 0; i < 4; i++)
        {
            _guard.TryReleaseForRetry("BotA", 1, 5, out _);
            _guard.TryAcceptUpdate("BotA", 1);
        }

        var released = _guard.TryReleaseForRetry("BotA", 1, 5, out var attempts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(released, Is.False);
            Assert.That(attempts, Is.EqualTo(5));
            Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.False, "dropped id should stay accepted");
        }

        _guard.TryReleaseForRetry("BotA", 1, 5, out attempts);
        Assert.That(attempts, Is.EqualTo(1), "count should start over after the update was dropped");
    }

    [Test]
    public void ClearFailures_ResetsCount()
    {
        _guard.TryReleaseForRetry("BotA", 1, 5, out _);
        _guard.TryReleaseForRetry("BotA", 1, 5, out _);

        _guard.ClearFailures("BotA", 1);
        _guard.TryReleaseForRetry("BotA", 1, 5, out var attempts);

        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void TryReleaseForRetry_CountsAreTrackedPerBot()
    {
        _guard.TryReleaseForRetry("BotA", 1, 5, out _);
        _guard.TryReleaseForRetry("BotB", 1, 5, out var attempts);

        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void TryReleaseForRetry_CountsExpireAfterFailureWindow()
    {
        _guard.TryReleaseForRetry("BotA", 1, 5, out _);
        _timeProvider.Advance(UpdateDeliveryGuard.FailureWindow - TimeSpan.FromSeconds(1));
        _guard.TryReleaseForRetry("BotA", 1, 5, out var withinWindow);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _guard.TryReleaseForRetry("BotA", 1, 5, out var afterWindow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withinWindow, Is.EqualTo(2));
            Assert.That(afterWindow, Is.EqualTo(1));
        }
    }

    [Test]
    public void TryReleaseForRetry_WhenLimitExceeded_DropsOldestCounts()
    {
        for (var id = 0; id <= UpdateDeliveryGuard.MaxTrackedFailuresPerBot; id++)
        {
            _guard.TryReleaseForRetry("BotA", id, 5, out _);
        }

        _guard.TryReleaseForRetry("BotA", 0, 5, out var oldest);
        _guard.TryReleaseForRetry("BotA", UpdateDeliveryGuard.MaxTrackedFailuresPerBot, 5, out var newest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest, Is.EqualTo(1), "oldest count should have been dropped");
            Assert.That(newest, Is.EqualTo(2));
        }
    }

    [Test]
    public void TryAcceptUpdate_AfterReleaseAndReaccept_KeepsFullDuplicateWindow()
    {
        _guard.TryAcceptUpdate("BotA", 1);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        _guard.TryReleaseForRetry("BotA", 1, 5, out _);
        _guard.TryAcceptUpdate("BotA", 1);

        _timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.That(_guard.TryAcceptUpdate("BotA", 1), Is.False, "the earlier acceptance must not expire the new one");
    }
}
