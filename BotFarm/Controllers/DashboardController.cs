using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ILogger<DashboardController> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IEnumerable<IBotService> _botServices;

    public DashboardController(
        IHostApplicationLifetime appLifetime,
        ILogger<DashboardController> logger,
        IEnumerable<IBotService> botServices)
    {
        _logger = logger;
        _applicationLifetime = appLifetime;
        _botServices = botServices;
    }

    [HttpPost("shutdown")]
    public async Task<IActionResult> Shutdown([FromBody] bool pauseBotUpdates = false)
    {
        _logger.LogWarning("Shutdown from Dashboard.");

        if (pauseBotUpdates)
        {
            foreach (var botService in _botServices)
            {
                _logger.LogWarning($"Pausing updates for bot {botService.Name}.");
                _ = await botService.Pause();
            }
        }

        _applicationLifetime.StopApplication();

        return Ok(new
        {
            success = true,
            message = "Application stopping...",
        });
    }
}
