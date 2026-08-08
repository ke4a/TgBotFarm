using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Telegram.Bot.Types;
using TestBot.Controllers;

namespace TestBot.UnitTests.Controllers;

[TestFixture]
public class UpdateControllerTests
{
    [Test]
    public async Task Post_ForwardsUpdateToServiceAndReturnsOk()
    {
        var updateService = Substitute.For<IUpdateService>();
        var controller = new UpdateController(updateService);
        var update = new Update { Id = 123 };

        var result = await controller.Post(update);

        await updateService.Received(1).ProcessUpdate(update);
        Assert.That(result, Is.TypeOf<OkResult>());
    }
}
