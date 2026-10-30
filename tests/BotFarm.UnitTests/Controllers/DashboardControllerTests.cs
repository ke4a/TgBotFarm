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
    private IBotControlService _botControlService = null!;

    [SetUp]
    public void SetUp()
    {
        _applicationLifetime = Substitute.For<IHostApplicationLifetime>();
        _logger = Substitute.For<ILogger<DashboardController>>();
        _botControlService = Substitute.For<IBotControlService>();

        _controller = new DashboardController(_applicationLifetime, _logger, _botControlService);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_WithPauseBotUpdatesParameter_ReturnsSuccessfulJsonResponse(bool pauseBotUpdates)
    {
        // Act
        var result = await _controller.Shutdown(pauseBotUpdates) as OkObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);

        var json = JsonSerializer.Serialize(result!.Value);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("message").GetString(), Is.EqualTo("Application stopping..."));
        }

        if (pauseBotUpdates)
        {
            await _botControlService.Received(1).PauseAllForShutdownAsync(Arg.Any<CancellationToken>());
        }
        else
        {
            await _botControlService.DidNotReceive().PauseAllForShutdownAsync(Arg.Any<CancellationToken>());
        }

        await _botControlService.DidNotReceive()
            .SetEnabledAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _applicationLifetime.Received(1).StopApplication();
        _logger.Received(1).LogWarning("Shutdown from Dashboard.");
    }

    [Test]
    public async Task Shutdown_WithPauseBotUpdates_PausesBotsBeforeStoppingApplication()
    {
        await _controller.Shutdown(pauseBotUpdates: true);

        Received.InOrder(() =>
        {
            _botControlService.PauseAllForShutdownAsync(Arg.Any<CancellationToken>());
            _applicationLifetime.StopApplication();
        });
    }
}
