using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotWebhookInitializerServiceTests
{
    private ILogger<BotWebhookInitializerService> _logger;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BotWebhookInitializerService>>();
    }

    private static IConfiguration BuildConfiguration(string? webHookUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(webHookUrl is null
                ? []
                : new Dictionary<string, string?> { ["WebHookUrl"] = webHookUrl })
            .Build();

    [Test]
    public async Task InitializeAllAsync_WithDisabledBot_PausesAndSkipsWebhookResolution()
    {
        var bot = Substitute.For<IBotService>();
        bot.Enabled.Returns(false);
        var resolver = Substitute.For<IWebhookUrlResolver>();
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [bot], [resolver], _logger);

        await sut.InitializeAllAsync();

        using (Assert.EnterMultipleScope())
        {
            await bot.Received(1).Pause();
            await bot.DidNotReceive().Initialize();
            await bot.DidNotReceive().InitializeWebHook(Arg.Any<string>());
            resolver.DidNotReceive().CanResolve(Arg.Any<string>());
        }
    }

    [Test]
    public async Task InitializeAllAsync_WithEnabledBot_InitializesAndSetsWebhookFromResolvedUrl()
    {
        var bot = Substitute.For<IBotService>();
        bot.Enabled.Returns(true);
        bot.Name.Returns("TestBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve("https://example.com").Returns(true);
        resolver.ResolveAsync("https://example.com", Arg.Any<CancellationToken>()).Returns("https://example.com");
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [bot], [resolver], _logger);

        await sut.InitializeAllAsync();

        using (Assert.EnterMultipleScope())
        {
            await bot.Received(1).Initialize();
            await bot.DidNotReceive().Pause();
            await bot.Received(1).InitializeWebHook("https://example.com/api/TestBot/update");
        }
    }

    [Test]
    public async Task InitializeAllAsync_WithMultipleEnabledBots_ResolvesBaseUrlOnlyOnce()
    {
        var botA = Substitute.For<IBotService>();
        botA.Enabled.Returns(true);
        botA.Name.Returns("BotA");
        var botB = Substitute.For<IBotService>();
        botB.Enabled.Returns(true);
        botB.Name.Returns("BotB");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve("https://example.com").Returns(true);
        resolver.ResolveAsync("https://example.com", Arg.Any<CancellationToken>()).Returns("https://example.com");
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [botA, botB], [resolver], _logger);

        await sut.InitializeAllAsync();

        using (Assert.EnterMultipleScope())
        {
            await resolver.Received(1).ResolveAsync("https://example.com", Arg.Any<CancellationToken>());
            await botA.Received(1).InitializeWebHook("https://example.com/api/BotA/update");
            await botB.Received(1).InitializeWebHook("https://example.com/api/BotB/update");
        }
    }

    [Test]
    public async Task InitializeAllAsync_WithMixOfDisabledAndEnabledBots_OnlyPausesDisabledBot()
    {
        var disabledBot = Substitute.For<IBotService>();
        disabledBot.Enabled.Returns(false);
        var enabledBot = Substitute.For<IBotService>();
        enabledBot.Enabled.Returns(true);
        enabledBot.Name.Returns("EnabledBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve("https://example.com").Returns(true);
        resolver.ResolveAsync("https://example.com", Arg.Any<CancellationToken>()).Returns("https://example.com");
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [disabledBot, enabledBot], [resolver], _logger);

        await sut.InitializeAllAsync();

        using (Assert.EnterMultipleScope())
        {
            await disabledBot.Received(1).Pause();
            await enabledBot.Received(1).InitializeWebHook("https://example.com/api/EnabledBot/update");
        }
    }

    [Test]
    public void InitializeAllAsync_WithNoMatchingResolver_ThrowsInvalidOperationException()
    {
        var bot = Substitute.For<IBotService>();
        bot.Enabled.Returns(true);
        bot.Name.Returns("TestBot");
        var sut = new BotWebhookInitializerService(BuildConfiguration("unknown-provider"), [bot], [], _logger);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitializeAllAsync());
    }

    [Test]
    public async Task InitializeAllAsync_UsesFirstMatchingResolverInRegistrationOrder()
    {
        var bot = Substitute.For<IBotService>();
        bot.Enabled.Returns(true);
        bot.Name.Returns("TestBot");
        var nonMatchingResolver = Substitute.For<IWebhookUrlResolver>();
        nonMatchingResolver.CanResolve(Arg.Any<string>()).Returns(false);
        var matchingResolver = Substitute.For<IWebhookUrlResolver>();
        matchingResolver.CanResolve(Arg.Any<string>()).Returns(true);
        matchingResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("https://resolved.example.com");
        var sut = new BotWebhookInitializerService(BuildConfiguration("ngrok"), [bot], [nonMatchingResolver, matchingResolver], _logger);

        await sut.InitializeAllAsync();

        using (Assert.EnterMultipleScope())
        {
            await nonMatchingResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await bot.Received(1).InitializeWebHook("https://resolved.example.com/api/TestBot/update");
        }
    }

    [Test]
    public void InitializeAllAsync_WithMissingWebHookUrlConfiguration_TreatsItAsEmptyString()
    {
        var bot = Substitute.For<IBotService>();
        bot.Enabled.Returns(true);
        bot.Name.Returns("TestBot");
        var resolver = Substitute.For<IWebhookUrlResolver>();
        resolver.CanResolve(string.Empty).Returns(false);
        var sut = new BotWebhookInitializerService(BuildConfiguration(null), [bot], [resolver], _logger);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitializeAllAsync());
    }
}
