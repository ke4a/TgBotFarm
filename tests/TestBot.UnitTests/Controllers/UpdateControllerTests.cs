using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Core.Telemetry;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics.Metrics;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;
using TestBot.Controllers;
using TestBot.Handlers.Commands;
using TestBot.Services;

namespace TestBot.UnitTests.Controllers;

[TestFixture]
public class UpdateControllerTests
{
    private const string WebhookSecret = "expected_webhook-secret";
    private const string BotUsername = "test_farm_bot";
    private const long ChatId = 12345;
    private const long UserId = 67890;

    private IUpdateService _updateService = null!;
    private IWebhookSecretProvider _webhookSecretProvider = null!;
    private INotificationService _notificationService = null!;
    private ILogger<UpdateController> _logger = null!;
    private ManualTimeProvider _timeProvider = null!;
    private ServiceProvider _serviceProvider = null!;
    private IUpdateDeliveryGuard _deliveryGuard = null!;
    private UpdateMetrics _metrics = null!;
    private MetricCollector<long> _updates = null!;
    private MetricCollector<double> _duration = null!;
    private MetricCollector<long> _outcomes = null!;
    private MetricCollector<long> _commands = null!;
    private IMeterFactory _meterFactory = null!;

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

        // Record into the real instruments and read them back, as an exporter would.
        _meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
        _updates = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.updates");
        _duration = new MetricCollector<double>(_meterFactory, UpdateMetrics.MeterName, "botfarm.update.duration");
        _outcomes = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.update.outcomes");
        _commands = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.commands");
        _metrics = _serviceProvider.GetRequiredService<UpdateMetrics>();
    }

    [TearDown]
    public void TearDown()
    {
        _updates.Dispose();
        _duration.Dispose();
        _outcomes.Dispose();
        _commands.Dispose();
        _serviceProvider.Dispose();
    }

    private UpdateController CreateController(string? secretHeader, IUpdateService? updateService = null)
    {
        var httpContext = new DefaultHttpContext();
        if (secretHeader != null)
        {
            httpContext.Request.Headers[BotFarm.Core.Constants.SecretTokenHeaderName] = secretHeader;
        }

        return new UpdateController(
            updateService ?? _updateService,
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

        var result = await controller.Post(update, _metrics);

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

        var result = await controller.Post(update, _metrics);

        await _updateService.DidNotReceive().ProcessUpdate(Arg.Any<Telegram.Bot.Types.Update>());
        Assert.That(result, Is.TypeOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [Test]
    public async Task Post_WhenBotHasNoToken_ReturnsServiceUnavailableWithoutCallingHandler()
    {
        _webhookSecretProvider.GetSecret(Constants.Name)
            .Throws(new BotFarm.Core.Exceptions.BotNotConfiguredException(Constants.Name));
        _updateService.CanProcessUpdates.Returns(false);
        var controller = CreateController(WebhookSecret);

        var result = await controller.Post(TelegramMessageFactory.CreateUpdate(123), _metrics);

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

        var result = await controller.Post(update, _metrics);

        await _updateService.DidNotReceive().ProcessUpdate(Arg.Any<Telegram.Bot.Types.Update>());
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public async Task Post_WithWrongSecretToken_IsRejectedBeforeReadinessCheck()
    {
        _updateService.CanProcessUpdates.Returns(false);
        var controller = CreateController("wrong_secret");

        var result = await controller.Post(TelegramMessageFactory.CreateUpdate(123), _metrics);

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

        var result = await controller.Post(update, _metrics);

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

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(1));
        _timeProvider.Advance(TimeSpan.FromSeconds(59));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(2));

        await _notificationService.Received(1)
            .SendErrorNotification(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Message?>());
        Assert.That(GetLogCalls(LogLevel.Error), Has.Count.EqualTo(2));

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(3));

        await _notificationService.Received(2)
            .SendErrorNotification(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Message?>());
    }

    [Test]
    public async Task Post_WithDuplicateUpdateId_ReturnsOkAndProcessesOnce()
    {
        _updateService.CanProcessUpdates.Returns(true);

        var first = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        var second = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

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

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        await _updateService.Received(1).ProcessUpdate(Arg.Any<Update>());
    }

    [Test]
    public async Task Post_AfterServiceUnavailable_ProcessesRedeliveredUpdate()
    {
        _updateService.CanProcessUpdates.Returns(false);
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        _updateService.CanProcessUpdates.Returns(true);
        var result = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<OkResult>());
        await _updateService.Received(1).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
    }

    [Test]
    public async Task Post_AfterUnauthorized_ProcessesRedeliveredUpdate()
    {
        _updateService.CanProcessUpdates.Returns(true);
        await Post("wrong_secret", TelegramMessageFactory.CreateUpdate(123));

        var result = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<OkResult>());
        await _updateService.Received(1).ProcessUpdate(Arg.Is<Update>(u => u.Id == 123));
    }

    [Test]
    public async Task Post_WhenProcessingFailsTransiently_ReturnsServiceUnavailableAndProcessesRedelivery()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>())
            .Returns(_ => throw new TimeoutException("mongo down"), _ => Task.CompletedTask);

        var first = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        var second = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

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
            results.Add(await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123)));
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }
        var redelivered = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

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

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        var success = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

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

        var first = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        var second = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.TypeOf<OkResult>());
            Assert.That(second, Is.TypeOf<OkResult>());
        }
        await _updateService.Received(1).ProcessUpdate(Arg.Any<Update>());
    }

    [TestCase(UpdateKind.Command, "command")]
    [TestCase(UpdateKind.CallbackQuery, "callback_query")]
    [TestCase(UpdateKind.Other, "other")]
    public async Task Post_WhenProcessed_CountsTheUpdateByKindAndRecordsItsDuration(UpdateKind kind, string kindTag)
    {
        _updateService.CanProcessUpdates.Returns(true);
        var update = UpdateOfKind(kind);
        _updateService.Describe(update).Returns(new UpdateDescription(kind, kind == UpdateKind.Command ? "start" : null));

        await Post(WebhookSecret, update);

        var counted = _updates.GetMeasurementSnapshot().Single();
        var duration = _duration.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(counted.Value, Is.EqualTo(1));
            Assert.That(counted.Tags["bot"], Is.EqualTo(Constants.Name));
            Assert.That(counted.Tags["kind"], Is.EqualTo(kindTag));
            Assert.That(duration.Value, Is.GreaterThanOrEqualTo(0));
            Assert.That(duration.Tags["bot"], Is.EqualTo(Constants.Name));
        }
    }

    [Test]
    public async Task Post_WhenProcessedCommand_RecordsTheTimeProcessingTook()
    {
        _updateService.CanProcessUpdates.Returns(true);
        var update = UpdateOfKind(UpdateKind.Command);
        _updateService.ProcessUpdate(update).Returns(_ => Task.Delay(TimeSpan.FromMilliseconds(50)));

        await Post(WebhookSecret, update);

        Assert.That(_duration.GetMeasurementSnapshot().Single().Value, Is.GreaterThanOrEqualTo(0.04));
    }

    [TestCase(typeof(InvalidOperationException))]
    [TestCase(typeof(TimeoutException))]
    public async Task Post_WhenProcessingThrows_StillCountsTheUpdateAndRecordsItsDuration(Type exceptionType)
    {
        _updateService.CanProcessUpdates.Returns(true);
        var update = UpdateOfKind(UpdateKind.CallbackQuery);
        _updateService.Describe(update).Returns(new UpdateDescription(UpdateKind.CallbackQuery, null));
        _updateService.ProcessUpdate(update).ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "boom")!);

        await Post(WebhookSecret, update);

        var counted = _updates.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(counted.Value, Is.EqualTo(1));
            Assert.That(counted.Tags["kind"], Is.EqualTo("callback_query"));
            Assert.That(_duration.GetMeasurementSnapshot(), Has.Count.EqualTo(1));
        }
    }

    [TestCase(true, "wrong_secret")]
    [TestCase(true, null)]
    [TestCase(false, WebhookSecret)]
    public async Task Post_WhenRejected_RecordsNoThroughput(bool canProcessUpdates, string? secretHeader)
    {
        _updateService.CanProcessUpdates.Returns(canProcessUpdates);

        await Post(secretHeader, TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updates.GetMeasurementSnapshot(), Is.Empty);
            Assert.That(_duration.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [Test]
    public async Task Post_WithDuplicateUpdate_RecordsNoThroughputForTheRedelivery()
    {
        _updateService.CanProcessUpdates.Returns(true);
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        _updates.Clear();
        _duration.Clear();

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updates.GetMeasurementSnapshot(), Is.Empty);
            Assert.That(_duration.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [TestCase(true, "wrong_secret")]
    [TestCase(true, null)]
    [TestCase(false, WebhookSecret)]
    public async Task Post_WhenRejected_RecordsRejected(bool canProcessUpdates, string? secretHeader)
    {
        _updateService.CanProcessUpdates.Returns(canProcessUpdates);

        await Post(secretHeader, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long> { ["rejected"] = 1 }));
        Assert.That(_outcomes.GetMeasurementSnapshot().Select(m => m.Tags["bot"]), Is.All.EqualTo(Constants.Name));
    }

    [Test]
    public async Task Post_WithDuplicateUpdate_RecordsDuplicate()
    {
        _updateService.CanProcessUpdates.Returns(true);
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long> { ["duplicate"] = 1 }));
    }

    [Test]
    public async Task Post_WhenTransientFailureIsReleasedForRetry_RecordsFailedAndRetried()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new TimeoutException("mongo down"));

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long>
        {
            ["failed"] = 1,
            ["retried"] = 1
        }));
    }

    [Test]
    public async Task Post_WhenTransientFailureIsOnTheLastAttempt_RecordsFailedAndDropped()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new HttpRequestException("telegram down"));
        for (var i = 1; i < UpdateController.MaxTransientAttempts; i++)
        {
            await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }
        _outcomes.Clear();

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long>
        {
            ["failed"] = 1,
            ["dropped"] = 1
        }));
    }

    [Test]
    public async Task Post_WhenProcessingFailsPermanently_RecordsFailed()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long> { ["failed"] = 1 }));
    }

    [Test]
    public async Task Post_WhenProcessedSuccessfully_RecordsNoOutcome()
    {
        _updateService.CanProcessUpdates.Returns(true);

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(_outcomes.GetMeasurementSnapshot(), Is.Empty);
    }

    [Test]
    public async Task Post_WithTwoPermanentFailuresAndOneUpdateDroppedAfterItsLastRetry_CountsSevenFailedFourRetriedOneDropped()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Is<Update>(u => u.Id != 3)).ThrowsAsync(new InvalidOperationException("boom"));
        _updateService.ProcessUpdate(Arg.Is<Update>(u => u.Id == 3)).ThrowsAsync(new TimeoutException("mongo down"));

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(1));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(2));
        for (var i = 0; i < UpdateController.MaxTransientAttempts; i++)
        {
            await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(3));
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long>
        {
            ["failed"] = 7,
            ["retried"] = 4,
            ["dropped"] = 1
        }));
    }

    [Test]
    public async Task Post_WithDuplicatesOfFailedUpdates_CountsThemOnlyAsDuplicates()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.ProcessUpdate(Arg.Any<Update>()).ThrowsAsync(new InvalidOperationException("boom"));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        _outcomes.Clear();

        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));
        await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(OutcomeCounts(), Is.EquivalentTo(new Dictionary<string, long> { ["duplicate"] = 2 }));
    }

    [Test]
    public async Task Post_WhenDescribeThrows_ReturnsTheSameResult()
    {
        _updateService.CanProcessUpdates.Returns(true);
        _updateService.Describe(Arg.Any<Update>()).Throws(new InvalidOperationException("describe"));

        var result = await Post(WebhookSecret, TelegramMessageFactory.CreateUpdate(123));

        Assert.That(result, Is.TypeOf<OkResult>());
        await _updateService.Received(1).ProcessUpdate(Arg.Any<Update>());
    }

    [Test]
    public async Task Post_WithBuiltInRegisteredAndUnknownCommands_CountsEachUnderItsKey()
    {
        var service = CreateTestBotUpdateService();

        await Post(service, 1, CommandMessage("/start", ChatType.Private));
        await Post(service, 2, CommandMessage("/changelanguage", ChatType.Private));
        await Post(service, 3, CommandMessage(Constants.Commands.GetLastGif, ChatType.Private));
        await Post(service, 4, CommandMessage("/foo", ChatType.Private));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CommandCounts(), Is.EquivalentTo(new Dictionary<string, long>
            {
                ["start"] = 1,
                ["changelanguage"] = 1,
                ["getlastgif"] = 1,
                [BotFarm.Core.Constants.Telemetry.UnknownCommand] = 1
            }));
            Assert.That(_commands.GetMeasurementSnapshot().Select(m => m.Tags["bot"]), Is.All.EqualTo(Constants.Name));
            Assert.That(_outcomes.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [Test]
    public async Task Post_WithGroupCommandAddressedToThisBot_CountsItUnderItsKeyWithoutTheUsername()
    {
        var service = CreateTestBotUpdateService();

        await Post(service, 1, CommandMessage($"{Constants.Commands.GetLastGif}@{BotUsername}", ChatType.Group));

        Assert.That(CommandCounts(), Is.EquivalentTo(new Dictionary<string, long> { ["getlastgif"] = 1 }));
    }

    [Test]
    public async Task Post_WithGroupCommandAddressedToAnotherBot_CountsNoCommand()
    {
        var service = CreateTestBotUpdateService();

        await Post(service, 1, CommandMessage($"{Constants.Commands.GetLastGif}@another_bot", ChatType.Group));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_commands.GetMeasurementSnapshot(), Is.Empty);
            Assert.That(_updates.GetMeasurementSnapshot().Single().Tags["kind"], Is.EqualTo("other"));
        }
    }

    [Test]
    public async Task Post_WithUnknownCommand_ReportsNoMeasurementTaggedWithItsName()
    {
        var service = CreateTestBotUpdateService();
        var tagValues = new List<string?>();
        using var listener = ListenToBotFarmTags(tagValues);

        await Post(service, 1, CommandMessage("/foo", ChatType.Private));
        await Post(service, 2, CommandMessage($"/foo@{BotUsername}", ChatType.Group));
        await Post(service, 3, CommandMessage("/foo@another_bot", ChatType.Group));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tagValues, Is.Not.Empty);
            Assert.That(tagValues, Has.None.Contains("foo"));
        }
    }

    private Task<IActionResult> Post(string? secretHeader, Update update) =>
        CreateController(secretHeader).Post(update, _metrics);

    private Task<IActionResult> Post(IUpdateService updateService, int updateId, Message message) =>
        CreateController(WebhookSecret, updateService).Post(TelegramMessageFactory.CreateUpdate(updateId, message), _metrics);

    /// <summary>
    /// Creates TestBot's real update service with its GetLastGif command registered, signed in as
    /// <see cref="BotUsername"/>, so commands are described by the same routing that processes them.
    /// </summary>
    private TestBotUpdateService CreateTestBotUpdateService()
    {
        var botService = Substitute.For<IBotService>();
        botService.Client.Returns(TelegramBotClientFactory.CreateSubstitute());
        botService.Me.Returns(new User { Id = 54321, Username = BotUsername, IsBot = true });
        var botControlService = Substitute.For<IBotControlService>();
        botControlService.CanProcessUpdates(Constants.Name).Returns(true);
        var localizationService = Substitute.For<ILocalizationService>();
        localizationService.GetLocalizedString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("text");
        var databaseService = Substitute.For<ITestBotDatabaseService>();
        var identity = new BotIdentity(Constants.Name);

        var getLastGif = new GetLastGifCommandHandler(
            identity,
            botService,
            localizationService,
            databaseService,
            Substitute.For<ILogger<GetLastGifCommandHandler>>());

        return new TestBotUpdateService(
            identity,
            botService,
            botControlService,
            Substitute.For<ILogger<TestBotUpdateService>>(),
            databaseService,
            Substitute.For<ITestBotMarkupService>(),
            localizationService,
            Substitute.For<INotificationService>(),
            [getLastGif],
            []);
    }

    /// <summary>
    /// Creates a message in a chat of <paramref name="chatType"/> that is the bot command <paramref name="text"/>.
    /// </summary>
    private static Message CommandMessage(string text, ChatType chatType) => TelegramMessageFactory.CreateMessage(
        ChatId,
        UserId,
        text: text,
        chatType: chatType,
        entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = text.Length }]);

    /// <summary>
    /// Returns the recorded command counts by their <c>command</c> tag, leaving out commands counted zero times.
    /// </summary>
    private Dictionary<string, long> CommandCounts() => _commands.GetMeasurementSnapshot()
        .GroupBy(m => (string)m.Tags["command"]!)
        .ToDictionary(group => group.Key, group => group.Sum(m => m.Value))
        .Where(pair => pair.Value != 0)
        .ToDictionary();

    /// <summary>
    /// Starts listening to every instrument of the test's <c>BotFarm</c> meter and adds the value of each tag of
    /// each measurement to <paramref name="tagValues"/>.
    /// </summary>
    private MeterListener ListenToBotFarmTags(List<string?> tagValues)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == UpdateMetrics.MeterName && instrument.Meter.Scope == _meterFactory)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => AddTagValues(tags));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => AddTagValues(tags));
        listener.Start();
        return listener;

        void AddTagValues(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                tagValues.Add(tag.Value?.ToString());
            }
        }
    }

    /// <summary>
    /// Returns the recorded outcome counts by their <c>outcome</c> tag, leaving out outcomes counted zero times.
    /// </summary>
    private Dictionary<string, long> OutcomeCounts() => _outcomes.GetMeasurementSnapshot()
        .GroupBy(m => (string)m.Tags["outcome"]!)
        .ToDictionary(group => group.Key, group => group.Sum(m => m.Value))
        .Where(pair => pair.Value != 0)
        .ToDictionary();

    private static Update UpdateOfKind(UpdateKind kind) => kind switch
    {
        UpdateKind.Command => TelegramMessageFactory.CreateUpdate(123, TelegramMessageFactory.CreateMessage(1, 1,
            text: "/start", entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 6 }])),
        UpdateKind.CallbackQuery => TelegramMessageFactory.CreateUpdate(123,
            callbackQuery: TelegramMessageFactory.CreateCallbackQuery("callback-1", "data")),
        _ => TelegramMessageFactory.CreateUpdate(123, TelegramMessageFactory.CreateMessage(1, 1, text: "hello"))
    };

    private List<NSubstitute.Core.ICall> GetLogCalls(LogLevel level)
    {
        return _logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                           && (LogLevel)call.GetArguments()[0]! == level)
            .ToList();
    }
}
