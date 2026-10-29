using BotFarm.Core.Abstractions;
using BotFarm.Shared.Controllers;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TestBot.Controllers;

namespace TestBot.UnitTests.Controllers;

[TestFixture]
public class UpdateControllerTests
{
    private const string WebhookSecret = "expected_webhook-secret";

    private IUpdateService _updateService = null!;
    private IWebhookSecretProvider _webhookSecretProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _updateService = Substitute.For<IUpdateService>();
        _updateService.Name.Returns(Constants.Name);
        _webhookSecretProvider = Substitute.For<IWebhookSecretProvider>();
        _webhookSecretProvider.GetSecret(Constants.Name).Returns(WebhookSecret);
    }

    private UpdateController CreateController(string? secretHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (secretHeader != null)
        {
            httpContext.Request.Headers[BotFarm.Core.Constants.SecretTokenHeaderName] = secretHeader;
        }

        return new UpdateController(
            _updateService,
            _webhookSecretProvider,
            NullLogger<UpdateController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Test]
    public async Task Post_ForwardsUpdateToServiceAndReturnsOk()
    {
        _updateService.CanProcessUpdates.Returns(true);
        var controller = CreateController(WebhookSecret);
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await _updateService.Received(1).ProcessUpdate(update);
        Assert.That(result, Is.TypeOf<OkResult>());
    }

    [TestCase("disabled")]
    [TestCase("unknown")]
    [TestCase("enable pending")]
    public async Task Post_WhenBotCannotProcessUpdates_ReturnsServiceUnavailableWithoutCallingHandler(string state)
    {
        _updateService.CanProcessUpdates.Returns(false);
        var controller = CreateController(WebhookSecret);
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await _updateService.DidNotReceive().ProcessUpdate(Arg.Any<Telegram.Bot.Types.Update>());
        Assert.That(result, Is.TypeOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wrong_secret")]
    [TestCase("expected_webhook-secretX")]
    public async Task Post_WithMissingOrWrongSecretToken_ReturnsUnauthorizedWithoutCallingHandler(string? secretHeader)
    {
        _updateService.CanProcessUpdates.Returns(true);
        var controller = CreateController(secretHeader);
        var update = TelegramMessageFactory.CreateUpdate(123);

        var result = await controller.Post(update);

        await _updateService.DidNotReceive().ProcessUpdate(Arg.Any<Telegram.Bot.Types.Update>());
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public async Task Post_WithWrongSecretToken_IsRejectedBeforeReadinessCheck()
    {
        _updateService.CanProcessUpdates.Returns(false);
        var controller = CreateController("wrong_secret");

        var result = await controller.Post(TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
        _ = _updateService.DidNotReceive().CanProcessUpdates;
    }
}
