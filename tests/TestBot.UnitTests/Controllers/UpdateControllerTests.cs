using BotFarm.Core.Abstractions;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
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
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await updateService.Received(1).ProcessUpdate(update);
        Assert.That(result, Is.TypeOf<OkResult>());
    }
}
