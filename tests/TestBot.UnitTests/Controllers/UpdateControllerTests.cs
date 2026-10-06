using BotFarm.Core.Abstractions;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Http;
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
        updateService.CanProcessUpdates.Returns(true);
        var controller = new UpdateController(updateService);
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await updateService.Received(1).ProcessUpdate(update);
        Assert.That(result, Is.TypeOf<OkResult>());
    }

    [TestCase("disabled")]
    [TestCase("unknown")]
    [TestCase("enable pending")]
    public async Task Post_WhenBotCannotProcessUpdates_ReturnsServiceUnavailableWithoutCallingHandler(string state)
    {
        var updateService = Substitute.For<IUpdateService>();
        updateService.CanProcessUpdates.Returns(false);
        var controller = new UpdateController(updateService);
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await updateService.DidNotReceive().ProcessUpdate(Arg.Any<Telegram.Bot.Types.Update>());
        Assert.That(result, Is.TypeOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }
}