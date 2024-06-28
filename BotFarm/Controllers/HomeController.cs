using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BotFarm.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        [Route("/")]
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true || Debugger.IsAttached)
            {
                return View("~/Views/Index.cshtml");
                
            }
            else
            {
                return Content(
@"
     _
   _| |
 _| | |
| | | |
| | | | __
| | | |/  \
|       /\ \
|      /  \/
|      \  /\
|       \/ /
 \        /
  |     /
  |    |
"
                , "text/plain");
            }
        }
    }
}
