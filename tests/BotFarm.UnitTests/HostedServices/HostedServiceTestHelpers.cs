namespace BotFarm.UnitTests.HostedServices;

/// <summary>
/// Helpers for observing background services whose work continues on other threads.
/// </summary>
internal static class HostedServiceTestHelpers
{
    /// <summary>
    /// Polls <paramref name="condition"/> until it is true, failing the test after five seconds.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {description}.");
            }

            await Task.Delay(10);
        }
    }
}
