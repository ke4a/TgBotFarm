using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        [Route("/")]
        public IActionResult Index()
        {
#if !DEBUG
            return Content("Ok", "text/plain");
#else
            return View("~/Views/Index.cshtml");
#endif
        }
    }
}
