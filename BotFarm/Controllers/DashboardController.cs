using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Controllers;

/// <summary>
/// Hosts dashboard endpoints for operational actions.
/// </summary>
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ILogger<DashboardController> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IBotControlService _botControlService;

    /// <summary>
    /// Creates the dashboard controller with access to the bot control coordinator.
    /// </summary>
    public DashboardController(
        IHostApplicationLifetime appLifetime,
        ILogger<DashboardController> logger,
        IBotControlService botControlService)
    {
        _logger = logger;
        _applicationLifetime = appLifetime;
        _botControlService = botControlService;
    }

    /// <summary>
    /// Stops the host, optionally pausing all bot webhooks first.
    /// </summary>
    [HttpPost("shutdown")]
    public async Task<IActionResult> Shutdown([FromBody] bool pauseBotUpdates = false)
    {
        _logger.LogWarning("Shutdown from Dashboard.");

        if (pauseBotUpdates)
        {
            await _botControlService.PauseAllForShutdownAsync();
        }

        _applicationLifetime.StopApplication();

        return Ok(new
        {
            success = true,
            message = "Application stopping...",
        });
    }
}
