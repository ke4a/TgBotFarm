using BotFarm.Controllers;
using BotFarm.Core.Abstractions;
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
    private IEnumerable<IBotService> _botServices;

    [SetUp]
    public void SetUp()
    {
        _applicationLifetime = Substitute.For<IHostApplicationLifetime>();
        _logger = Substitute.For<ILogger<DashboardController>>();
        _botServices = Substitute.For<IEnumerable<IBotService>>();

        _controller = new DashboardController(_applicationLifetime, _logger, _botServices);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_WithPauseBotUpdatesParameter_ReturnsSuccessfulJsonResponse(bool pauseBotUpdates)
    {
        // Arrange
        var mockBot1 = Substitute.For<IBotService>();
        mockBot1.Name.Returns("Bot1");
        mockBot1.Pause().Returns(Task.FromResult(true));

        var mockBot2 = Substitute.For<IBotService>();
        mockBot2.Name.Returns("Bot2");
        mockBot2.Pause().Returns(Task.FromResult(true));

        var botServices = new List<IBotService> { mockBot1, mockBot2 };
        _controller = new DashboardController(_applicationLifetime, _logger, botServices);

        // Act
        var result = await _controller.Shutdown(pauseBotUpdates) as JsonResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        
        var json = JsonSerializer.Serialize(result.Value);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("message").GetString(), Is.EqualTo("Application stopping..."));
        }

        if (pauseBotUpdates)
        {
            await mockBot1.Received(1).Pause();
            await mockBot2.Received(1).Pause();
            _logger.Received(1).LogWarning("Pausing updates for bot Bot1.");
            _logger.Received(1).LogWarning("Pausing updates for bot Bot2.");
        }
        else
        {
            await mockBot1.DidNotReceive().Pause();
            await mockBot2.DidNotReceive().Pause();
            _logger.DidNotReceive().LogWarning("Pausing updates for bot Bot1.");
            _logger.DidNotReceive().LogWarning("Pausing updates for bot Bot2.");
        }
        
        _applicationLifetime.Received(1).StopApplication();
        _logger.Received(1).LogWarning("Shutdown from Dashboard.");
    }
}
