using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Controllers;

#if !DEBUG
[Authorize]
#endif
public class DashboardController : Controller
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

    [HttpGet]
    [Route("/dashboard")]
    public IActionResult Index()
    {
        return View("~/Views/Dashboard.cshtml");
    }

    [HttpPost]
    [Route("/dashboard/shutdown")]
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

        return Json(new
        {
            success = true,
            message = "Application stopping...",
        });
    }
}
