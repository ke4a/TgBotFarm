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
    public async Task DisableAsync_PausesBot()
    {
        var bot = Substitute.For<IBotService>();
        bot.Pause().Returns(true);
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [], _logger);

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
        var sut = new BotWebhookInitializerService(BuildConfiguration("https://example.com"), [resolver], _logger);

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
        var sut = new BotWebhookInitializerService(BuildConfiguration(null), [], _logger);

        var result = await sut.DisableAsync(bot);

        Assert.That(result, Is.False);
    }

    [Test]
    public void EnableAsync_WithNoMatchingResolver_ThrowsInvalidOperationException()
    {
        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("TestBot");
        var sut = new BotWebhookInitializerService(BuildConfiguration("unknown-provider"), [], _logger);

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
        var sut = new BotWebhookInitializerService(BuildConfiguration("ngrok"), [nonMatchingResolver, matchingResolver],
            _logger);

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
        var sut = new BotWebhookInitializerService(BuildConfiguration(null), [resolver], _logger);

        Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnableAsync(bot));
    }
}