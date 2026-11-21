using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotWebhookInitializerServiceTests
{
    private ILogger<BotWebhookInitializerService> _logger;
    private ILocalizationService _localization;
    private INotificationService _notificationService;
    private ServiceProvider? _serviceProvider;
    private List<ICommandHandler> _commandHandlers;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BotWebhookInitializerService>>();
        _localization = Substitute.For<ILocalizationService>();
        _localization.GetAvailableLanguages(Arg.Any<string>()).Returns(["en-US"]);
        _localization.GetLocalizedString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => call.ArgAt<string>(1) + " text");
        _notificationService = Substitute.For<INotificationService>();
        _commandHandlers = [];
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    /// <summary>Creates the service with the handlers in <see cref="_commandHandlers"/> registered for TestBot.</summary>
    private BotWebhookInitializerService CreateSut(IConfiguration configuration, IEnumerable<IWebhookUrlResolver> resolvers)
    {
        var services = new ServiceCollection();
        foreach (var handler in _commandHandlers)
        {
            services.AddKeyedSingleton("TestBot", handler);
        }

        _serviceProvider = services.BuildServiceProvider();
        return new BotWebhookInitializerService(configuration, resolvers,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(), _localization, _notificationService, _logger);
    }

    private static IWebhookUrlResolver CreateResolver()
    {
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve(Arg.Any<string>()).Returns(true);
        resolver.Resolve(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("https://example.com");
        return resolver;
    }

    private static (IBotService Bot, TelegramBotClient Client) CreateBot()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var client = TestKit.TelegramBotClientFactory.CreateSubstitute();
        bot.Client.Returns(client);
        return (bot, client);
    }

    private static IConfiguration BuildConfiguration(string? webHookUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(webHookUrl is null
                ? []
                : new Dictionary<string, string?> { ["WebHookUrl"] = webHookUrl })
            .Build();

    [Test]
    public async Task DisableAsync_PausesBot()
    {
        var bot = Substitute.For<IBotService>();
        bot.Pause().Returns(true);
        var sut = CreateSut(BuildConfiguration("https://example.com"), []);

        var result = await sut.DisableAsync(bot);

        Assert.That(result, Is.True);
        await bot.Received(1).Pause();
    }

    [Test]
    public async Task EnableAsync_InitializesBeforeResolvingAndSettingWebhook()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve("https://example.com").Returns(true);
        resolver.Resolve("https://example.com", Arg.Any<CancellationToken>()).Returns("https://example.com");
        var calls = new List<string>();
        bot.Initialize().Returns(_ =>
        {
            calls.Add("initialize");
            return Task.CompletedTask;
        });
        resolver.Resolve("https://example.com", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("resolve");
            return Task.FromResult("https://example.com");
        });
        bot.InitializeWebHook(Arg.Any<string>()).Returns(_ =>
        {
            calls.Add("webhook");
            return Task.CompletedTask;
        });
        var sut = CreateSut(BuildConfiguration("https://example.com"), [resolver]);

        await sut.EnableAsync(bot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(new[] { "initialize", "resolve", "webhook" }));
            await bot.Received(1).InitializeWebHook("https://example.com/api/TestBot/update");
        }
    }

    [Test]
    public async Task DisableAsync_ReturnsFailureWhenPauseFails()
    {
        var bot = Substitute.For<IBotService>();
        bot.Pause().Returns(false);
        var sut = CreateSut(BuildConfiguration(null), []);

        var result = await sut.DisableAsync(bot);

        Assert.That(result, Is.False);
    }

    [Test]
    public void EnableAsync_WithNoMatchingResolver_ThrowsInvalidOperationException()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var sut = CreateSut(BuildConfiguration("unknown-provider"), []);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnableAsync(bot));
    }

    [Test]
    public async Task EnableAsync_UsesFirstMatchingResolverInRegistrationOrder()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var nonMatchingResolver = Substitute.For<IWebhookUrlResolver>();
        nonMatchingResolver.CanResolve(Arg.Any<string>()).Returns(false);
        var matchingResolver = Substitute.For<IWebhookUrlResolver>();
        matchingResolver.CanResolve(Arg.Any<string>()).Returns(true);
        matchingResolver.Resolve(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://resolved.example.com");
        var sut = CreateSut(BuildConfiguration("ngrok"), [nonMatchingResolver, matchingResolver]);

        await sut.EnableAsync(bot);

        using (Assert.EnterMultipleScope())
        {
            await nonMatchingResolver.DidNotReceive().Resolve(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await bot.Received(1).InitializeWebHook("https://resolved.example.com/api/TestBot/update");
        }
    }

    [Test]
    public void EnableAsync_WithMissingWebHookUrlConfiguration_TreatsItAsEmptyString()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve(string.Empty).Returns(false);
        var sut = CreateSut(BuildConfiguration(null), [resolver]);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnableAsync(bot));
        resolver.Received(1).CanResolve(string.Empty);
        bot.DidNotReceiveWithAnyArgs().InitializeWebHook(default!);
    }

    [Test]
    public async Task EnableAsync_PassesCancellationTokenToEveryStep()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve(Arg.Any<string>()).Returns(true);
        resolver.Resolve(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("https://example.com");
        var sut = CreateSut(BuildConfiguration("https://example.com"), [resolver]);

        await sut.EnableAsync(bot, token);

        await bot.Received(1).Initialize(token);
        await resolver.Received(1).Resolve("https://example.com", token);
        await bot.Received(1).InitializeWebHook("https://example.com/api/TestBot/update", token);
    }

    [Test]
    public async Task EnableAsync_RegistersTheCommandMenusAfterSettingTheWebhook()
    {
        var (bot, client) = CreateBot();
        var handler = Substitute.For<ICommandHandler>();
        handler.Command.Returns("/clearchatdata");
        handler.DescriptionKey.Returns("ClearDescription");
        handler.Visibility.Returns(CommandVisibility.GroupAdmins);
        _commandHandlers.Add(handler);
        var calls = new List<string>();
        bot.InitializeWebHook(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("webhook");
            return Task.CompletedTask;
        });
        client.SendRequest(Arg.Any<SetMyCommandsRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("commands");
            return Task.FromResult(true);
        });
        var sut = CreateSut(BuildConfiguration("https://example.com"), [CreateResolver()]);

        await sut.EnableAsync(bot);

        var requests = client.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<SetMyCommandsRequest>()
            .ToList();
        var adminRequest = requests.Single(request =>
            request.Scope?.Type == BotCommandScopeType.AllChatAdministrators && request.LanguageCode == "en");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls.First(), Is.EqualTo("webhook"));
            Assert.That(requests, Has.Count.EqualTo(6));
            Assert.That(adminRequest.Commands.Select(command => (command.Command, command.Description)), Is.EqualTo(new[]
            {
                ("changelanguage", "ChangeLanguageCommandDescription text"),
                ("clearchatdata", "ClearDescription text")
            }));
            await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!);
        }
    }

    [Test]
    public async Task EnableAsync_DeletesEmptyCommandMenus()
    {
        var (bot, client) = CreateBot();
        _localization.GetLocalizedString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(string.Empty);
        var sut = CreateSut(BuildConfiguration("https://example.com"), [CreateResolver()]);

        await sut.EnableAsync(bot);

        using (Assert.EnterMultipleScope())
        {
            await client.Received(6).SendRequest(Arg.Any<DeleteMyCommandsRequest>(), Arg.Any<CancellationToken>());
            await client.DidNotReceive().SendRequest(Arg.Any<SetMyCommandsRequest>(), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task EnableAsync_WhenRegisteringCommandsFails_SetsTheWebhookAndReportsTheError()
    {
        var (bot, client) = CreateBot();
        client.SendRequest(Arg.Any<SetMyCommandsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: BOT_COMMAND_INVALID", 400));
        var sut = CreateSut(BuildConfiguration("https://example.com"), [CreateResolver()]);

        await sut.EnableAsync(bot);

        using (Assert.EnterMultipleScope())
        {
            await bot.Received(1).InitializeWebHook("https://example.com/api/TestBot/update", Arg.Any<CancellationToken>());
            await _notificationService.Received(1).SendErrorNotification(
                Arg.Is<string>(text => text.Contains("TestBot") && text.Contains("BOT_COMMAND_INVALID")),
                "TestBot");
        }
    }

    [Test]
    public async Task EnableAsync_WhenTheCommandMenuIsInvalid_SetsTheWebhookAndReportsTheError()
    {
        var (bot, client) = CreateBot();
        var handler = Substitute.For<ICommandHandler>();
        handler.Command.Returns("/Invalid-Name");
        handler.DescriptionKey.Returns("InvalidDescription");
        _commandHandlers.Add(handler);
        var sut = CreateSut(BuildConfiguration("https://example.com"), [CreateResolver()]);

        await sut.EnableAsync(bot);

        using (Assert.EnterMultipleScope())
        {
            await bot.Received(1).InitializeWebHook("https://example.com/api/TestBot/update", Arg.Any<CancellationToken>());
            await client.DidNotReceive().SendRequest(Arg.Any<SetMyCommandsRequest>(), Arg.Any<CancellationToken>());
            await _notificationService.Received(1).SendErrorNotification(
                Arg.Is<string>(text => text.Contains("/Invalid-Name")), "TestBot");
        }
    }

    [Test]
    public void EnableAsync_WhenCancelledWhileRegisteringCommands_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        var (bot, client) = CreateBot();
        client.SendRequest(Arg.Any<SetMyCommandsRequest>(), Arg.Any<CancellationToken>()).Returns<bool>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var sut = CreateSut(BuildConfiguration("https://example.com"), [CreateResolver()]);

        Assert.CatchAsync<OperationCanceledException>(() => sut.EnableAsync(bot, cancellation.Token));
        _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!);
    }
}
