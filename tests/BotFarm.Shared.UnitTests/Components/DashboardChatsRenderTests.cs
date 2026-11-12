using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="DashboardChats"/> with chats from a mocked bot and checks how chat names are shown.
/// </summary>
[TestFixture]
public class DashboardChatsRenderTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";
    private const string EmptyBotName = "EmptyBot";
    private const long LongNameChatId = 1;
    private const long ShortNameChatId = 2;
    private const string LongName = "The Very Long Name Of A Telegram Group Chat About Everything";
    private const string ShortName = "Weekend Hikers";

    private BunitContext _context = default!;
    private IRenderedComponent<DashboardChats> _cut = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();

        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.GetAllChatIds().Returns([LongNameChatId, ShortNameChatId]);

        var client = TelegramBotClientFactory.CreateSubstitute();
        client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = call.Arg<GetChatRequest>().ChatId.Identifier!.Value;
                return new ChatFullInfo { Id = id, Title = id == LongNameChatId ? LongName : ShortName };
            });
        var botService = Substitute.For<IBotService>();
        botService.Client.Returns(client);

        var botRegistry = Substitute.For<IBotRegistry>();
        botRegistry.GetService<IDatabaseService>(TestBotName).Returns(databaseService);
        botRegistry.GetService<IBotService>(TestBotName).Returns(botService);
        var emptyDatabaseService = Substitute.For<IDatabaseService>();
        emptyDatabaseService.GetAllChatIds().Returns([]);
        botRegistry.GetService<IDatabaseService>(EmptyBotName).Returns(emptyDatabaseService);
        botRegistry.GetService<IBotService>(EmptyBotName).Returns(botService);
        _context.Services.AddSingleton(botRegistry);
        _context.Services.AddSingleton(Substitute.For<IBroadcastService>());

        _context.Render<MudPopoverProvider>();
        _cut = _context.Render<DashboardChats>(parameters => parameters
            .Add(x => x.BotName, TestBotName)
            .Add(x => x.Title, "Chats"));
        _cut.WaitForNUnitAssertion(() => Assert.That(_cut.FindAll(".chat-name"), Has.Count.EqualTo(2)));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Render_LongName_IsCutAt45CharactersWithEllipsis()
    {
        var shown = _cut.FindAll(".chat-name")[0].TextContent;

        Assert.That(shown, Is.EqualTo(LongName[..45].TrimEnd() + "…"));
    }

    [Test]
    public void Render_ShortName_IsShownInFull()
    {
        var shown = _cut.FindAll(".chat-name")[1].TextContent;

        Assert.That(shown, Is.EqualTo(ShortName));
    }

    [Test]
    public void Render_OnlyLongName_HasTooltip()
    {
        var nameCells = _cut.FindAll("td[data-label=Name]");

        Assert.Multiple(() =>
        {
            Assert.That(nameCells[0].QuerySelector(".mud-tooltip-root"), Is.Not.Null);
            Assert.That(nameCells[1].QuerySelector(".mud-tooltip-root"), Is.Null);
        });
    }

    [Test]
    public void CopyName_OnLongName_CopiesFullName()
    {
        _cut.FindAll("td[data-label=Name] button[aria-label='Copy name']")[0].Click();

        var copied = _context.JSInterop.Invocations
            .Single(i => i.Identifier == "navigator.clipboard.writeText")
            .Arguments.Single();
        Assert.That(copied, Is.EqualTo(LongName));
    }

    [Test]
    public void Render_InRussian_ShowsRussianTitleColumnsAndPager()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");

        // Act
        var cut = _context.Render<DashboardChats>(parameters => parameters.Add(x => x.BotName, TestBotName));
        cut.WaitForNUnitAssertion(() => Assert.That(cut.FindAll(".chat-name"), Has.Count.EqualTo(2)));

        // Assert
        var headers = cut.FindAll("th").Select(th => th.TextContent.Trim()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find(".card-header h6").TextContent.Trim(), Is.EqualTo("Чаты"));
            Assert.That(headers, Is.EqualTo(new[] { "#", "Название", "ID", "Действия" }));
            Assert.That(cut.FindAll("td[data-label='Название']"), Has.Count.EqualTo(2));
            Assert.That(cut.Markup, Does.Contain("Строк на странице:"));
            Assert.That(cut.Markup, Does.Contain("1-2 из 2"));
            Assert.That(cut.FindAll("button[aria-label='Копировать название']"), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Render_InRussianWithoutChats_ShowsRussianEmptyState()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");

        // Act
        var cut = _context.Render<DashboardChats>(parameters => parameters.Add(x => x.BotName, EmptyBotName));

        // Assert
        cut.WaitForNUnitAssertion(() => Assert.That(cut.Markup, Does.Contain("Чаты не найдены.")));
    }
}
