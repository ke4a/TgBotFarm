using BotFarm.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace BotFarm.UnitTests.Controllers;

[TestFixture]
public class DashboardControllerTests
{
    private DashboardController _controller;
    private IHostApplicationLifetime _applicationLifetime;
    private ILogger<DashboardController> _logger;

    [SetUp]
    public void SetUp()
    {
        _applicationLifetime = Substitute.For<IHostApplicationLifetime>();
        _logger = Substitute.For<ILogger<DashboardController>>();

        _controller = new DashboardController(_applicationLifetime, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    [Test]
    public async Task Shutdown_ReturnsSuccessfulJsonResponse()
    {
        // Act
        var result = await _controller.Shutdown() as JsonResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        
        // Serialize and deserialize to get strongly typed object
        var json = JsonSerializer.Serialize(result.Value);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("message").GetString(), Is.EqualTo("Application stopping..."));
        }

        _applicationLifetime.Received(1).StopApplication();
        _logger.Received(1).LogWarning("Shutdown from Dashboard.");
    }
}
