using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Controllers
{
#if !DEBUG
    [Authorize]
#endif
    public class DashboardController : Controller
    {
        private readonly ILogger<DashboardController> _logger;

        private IHostApplicationLifetime ApplicationLifetime { get; set; }

        public DashboardController(
            IHostApplicationLifetime appLifetime,
            ILogger<DashboardController> logger)
        {
            _logger = logger;
            ApplicationLifetime = appLifetime;
        }

        [HttpGet]
        [Route("/dashboard")]
        public IActionResult Index()
        {
            return View("~/Views/Dashboard.cshtml");
        }

        [HttpPost]
        [Route("/dashboard/shutdown")]
        public async Task<IActionResult> Shutdown()
        {
            _logger.LogWarning("Shutdown from Dashboard.");
            ApplicationLifetime.StopApplication();
            return Json(new
            {
                success = true,
                message = "Application stopping...",
            });
        }
    }
}
