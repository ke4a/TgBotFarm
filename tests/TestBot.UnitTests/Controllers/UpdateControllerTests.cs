using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using TestBot.Controllers;

namespace TestBot.UnitTests.Controllers;

[TestFixture]
public class UpdateControllerTests
{
    private const string WebhookSecret = "expected_webhook-secret";

    private IUpdateService _updateService = null!;
    private IWebhookSecretProvider _webhookSecretProvider = null!;
    private INotificationService _notificationService = null!;
    private ILogger<UpdateController> _logger = null!;
    private ManualTimeProvider _timeProvider = null!;
    private ServiceProvider _serviceProvider = null!;
    private IUpdateDeliveryGuard _deliveryGuard = null!;

    [SetUp]
    public void SetUp()
    {
        _updateService = Substitute.For<IUpdateService>();
        _updateService.Name.Returns(Constants.Name);
        _webhookSecretProvider = Substitute.For<IWebhookSecretProvider>();
        _webhookSecretProvider.GetSecret(Constants.Name).Returns(WebhookSecret);
        _notificationService = Substitute.For<INotificationService>();
        _logger = Substitute.For<ILogger<UpdateController>>();

        // Resolve the real guard through the core registrations, driven by a manual clock.
        _timeProvider = new ManualTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddCoreServices(new ConfigurationBuilder().Build(), Substitute.For<IHostEnvironment>());
        _serviceProvider = services.BuildServiceProvider();
        _deliveryGuard = _serviceProvider.GetRequiredService<IUpdateDeliveryGuard>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
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
            _deliveryGuard,
            _notificationService,
            _logger)
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

    [Test]
    public async Task Post_WhenProcessingThrows_ReturnsOkLogsErrorAndAlertsWithMessageContext()
    {
        _updateService.CanProcessUpdates.Returns(true);
        var message = TelegramMessageFactory.CreateMessage(1, 2, text: "secret user text");
        var update = TelegramMessageFactory.CreateUpdate(123, message: message);
        _updateService.ProcessUpdate(update).ThrowsAsync(new InvalidOperationException("boom"));
        var controller = CreateController(WebhookSecret);

        var result = await controller.Post(update);

        Assert.That(result, Is.TypeOf<OkResult>());
        var errorLog = GetLogCalls(LogLevel.Error).Single();
        var loggedText = errorLog.GetArguments()[2]!.ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(errorLog.GetArguments()[3], Is.TypeOf<InvalidOperationException>());
            Assert.That(loggedText, Does.Contain(Constants.Name).And.Contain("123").And.Contain("Message"));
            Assert.That(loggedText, Does.Not.Contain("secret user text"));
        }
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(text => text.Contains("123")), Constants.Name, message);
    }

    [Test]
    public async Task Post_WhenProcessingFailsRepeatedly_AlertsAtMostOncePerMinute()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(1));
        _timeProvider.Advance(TimeSpan.FromSeconds(59));
        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(2));

        await _notificationService.Received(1)
            .SendErrorNotification(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Message?>());
        Assert.That(GetLogCalls(LogLevel.Error), Has.Count.EqualTo(2));

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(3));

        await _notificationService.Received(2)
            .SendErrorNotification(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Message?>());
    }

    [Test]
    public async Task Post_WithDuplicateUpdateId_ReturnsOkAndProcessesOnce()
    {
        _updateService.CanProcessUpdates.Returns(true);

        var first = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        var second = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.TypeOf<OkResult>());
            Assert.That(second, Is.TypeOf<OkResult>());
        }
        await _updateService.Received(1).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
    }

    [Test]
    public async Task Post_WhenFailedUpdateIsRedelivered_DoesNotProcessItAgain()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        await _updateService.Received(1).ProcessUpdate(Arg.Any<Update>());
    }

    [Test]
    public async Task Post_AfterServiceUnavailable_ProcessesRedeliveredUpdate()
    {
        _updateService.CanProcessUpdates.Returns(false);
        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        _updateService.CanProcessUpdates.Returns(true);
        var result = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<OkResult>());
        await _updateService.Received(1).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
    }

    [Test]
    public async Task Post_AfterUnauthorized_ProcessesRedeliveredUpdate()
    {
        _updateService.CanProcessUpdates.Returns(true);
        await CreateController("wrong_secret").Post(TelegramMessageFactory.CreateUpdate(123));

        var result = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<OkResult>());
        await _updateService.Received(1).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
    }

    [Test]
    public async Task Post_WhenProcessingFailsTransiently_ReturnsServiceUnavailableAndProcessesRedelivery()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>())
            .Returns(_ => throw new TimeoutException("mongo down"), _ => Task.CompletedTask);

        var first = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        var second = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((StatusCodeResult)first).StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(second, Is.TypeOf<OkResult>());
            Assert.That(GetLogCalls(LogLevel.Warning), Has.Count.EqualTo(1));
            Assert.That(GetLogCalls(LogLevel.Error), Is.Empty);
        }
        await _updateService.Received(2).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(text => text.Contains("123") && text.Contains("attempt 1 of 5")),
            Constants.Name,
            Arg.Any<Message?>());
    }

    [Test]
    public async Task Post_WhenTransientFailureReachesMax_ReturnsOkAlertsDroppedAndSkipsRedelivery()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new HttpRequestException("telegram down"));

        var results = new List<IActionResult>();
        for (var i = 0; i < UpdateController.MaxTransientAttempts; i++)
        {
            results.Add(await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123)));
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }
        var redelivered = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results.Take(4).Select(r => ((StatusCodeResult)r).StatusCode),
                Is.All.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(results[4], Is.TypeOf<OkResult>());
            Assert.That(redelivered, Is.TypeOf<OkResult>());
            Assert.That(GetLogCalls(LogLevel.Error), Has.Count.EqualTo(1));
        }
        await _updateService.Received(UpdateController.MaxTransientAttempts).ProcessUpdate(Arg.Any<Update>());
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(text => text.Contains("Dropped") && text.Contains("123") && text.Contains("after 5")),
            Constants.Name,
            Arg.Any<Message?>());
    }

    [Test]
    public async Task Post_WhenSucceedingAfterTransientFailures_ClearsFailureCount()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>())
            .Returns(
                _ => throw new IOException("reset"),
                _ => throw new IOException("reset"),
                _ => Task.CompletedTask);

        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        var success = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        Assert.That(success, Is.TypeOf<OkResult>());
        _deliveryGuard.TryReleaseForRetry(Constants.Name, 123, UpdateController.MaxTransientAttempts, out var attempts);
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task Post_WhenTelegramRejectsRequest_TreatsFailureAsPermanent()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>())
            .ThrowsAsync(new Telegram.Bot.Exceptions.ApiRequestException("Forbidden", 403));

        var first = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));
        var second = await CreateController(WebhookSecret).Post(TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.TypeOf<OkResult>());
            Assert.That(second, Is.TypeOf<OkResult>());
        }
        await _updateService.Received(1).ProcessUpdate(Arg.Any<Update>());
    }

    private List<NSubstitute.Core.ICall> GetLogCalls(LogLevel level)
    {
        return _logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                           && (LogLevel)call.GetArguments()[0]! == level)
            .ToList();
    }
}
