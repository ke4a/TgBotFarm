using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace TestBot.Controllers;

[ApiController]
[Route("api/TestBot/[controller]")]
public class UpdateController : ControllerBase
{
    private readonly IUpdateService _updateService;

    public UpdateController([FromKeyedServices(Constants.Name)] IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        await _updateService.ProcessUpdate(update);

        return Ok();
    }
}
