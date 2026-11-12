using System.Text.Json;
using BotFarm.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.UnitTests.Health;

/// <summary>
/// Checks the JSON body the <c>/health</c> endpoint writes.
/// </summary>
[TestFixture]
public class HealthReportJsonWriterTests
{
    [Test]
    public async Task WriteAsync_WritesOverallStatusAndEachEntry()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["MemoryCheck"] = new(HealthStatus.Healthy, "Reports memory.", TimeSpan.FromMilliseconds(5), null,
                new Dictionary<string, object> { ["WorkingSetBytes"] = 2048L }, ["BotFarmHealth"]),
            ["TestBot Stats"] = new(HealthStatus.Unhealthy, null, TimeSpan.Zero,
                new InvalidOperationException("database offline"), null)
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(5));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthReportJsonWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;
        var memory = root.GetProperty("entries").GetProperty("MemoryCheck");
        var bot = root.GetProperty("entries").GetProperty("TestBot Stats");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.ContentType, Does.StartWith("application/json"));
            Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
            Assert.That(memory.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(memory.GetProperty("data").GetProperty("WorkingSetBytes").GetInt64(), Is.EqualTo(2048));
            Assert.That(memory.GetProperty("tags")[0].GetString(), Is.EqualTo("BotFarmHealth"));
            Assert.That(bot.GetProperty("exception").GetString(), Is.EqualTo("database offline"));
        }
    }
}
