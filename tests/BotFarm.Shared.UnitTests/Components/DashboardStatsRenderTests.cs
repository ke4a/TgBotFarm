using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="DashboardStats"/> with mocked chat counts and checks the chats-by-language breakdown.
/// </summary>
[TestFixture]
public class DashboardStatsRenderTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";

    private BunitContext _context = default!;
    private IMongoDbDatabaseService _databaseService = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();

        _databaseService = Substitute.For<IMongoDbDatabaseService>();
        _databaseService.GetChatCount().Returns(0L);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        var botRegistry = Substitute.For<IBotRegistry>();
        botRegistry.GetService<IMongoDbDatabaseService>(TestBotName).Returns(_databaseService);
        _context.Services.AddSingleton(botRegistry);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void LanguageBreakdown_ListsLanguagesLargestFirstWithCountAndShare()
    {
        ReturnCounts(new ChatLanguageCount("en-US", 6), new ChatLanguageCount("uk-UA", 3), new ChatLanguageCount(null, 1));

        var rows = Rows(Render());

        Assert.That(rows, Is.EqualTo(new[]
        {
            "en-US 6 · 60%",
            "uk-UA 3 · 30%",
            "Not set 1 · 10%",
        }));
    }

    [Test]
    public void LanguageBreakdown_WithOneMoreLanguageThanTheLimit_NamesThemAll()
    {
        ReturnCounts(Languages(DashboardStats.MaxNamedLanguages + 1));

        var rows = Rows(Render());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(DashboardStats.MaxNamedLanguages + 1));
            Assert.That(rows, Has.None.StartsWith("Other ("));
        }
    }

    [Test]
    public void LanguageBreakdown_WithManyLanguages_FoldsTheSmallestIntoOneRow()
    {
        // 15 languages with 15, 14, ... 1 chats: 120 chats in all.
        ReturnCounts(Languages(15));

        var rows = Rows(Render());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(DashboardStats.MaxNamedLanguages + 1));
            Assert.That(rows[0], Is.EqualTo("l01 15 · 13%"));
            // The other ten languages have 10 + 9 + ... + 1 = 55 chats.
            Assert.That(rows[^1], Is.EqualTo("Other (10) 55 · 46%"));
        }
    }

    [Test]
    public void LanguageBreakdown_WithATinyShare_ShowsLessThanOnePercent()
    {
        ReturnCounts(new ChatLanguageCount("en-US", 999), new ChatLanguageCount("uk-UA", 1));

        var rows = Rows(Render());

        Assert.That(rows[1], Is.EqualTo("uk-UA 1 · <1%"));
    }

    [Test]
    public void LanguageBreakdown_WithNoChats_IsHidden()
    {
        ReturnCounts();

        var cut = Render();

        Assert.That(cut.FindAll(".stat-breakdown"), Is.Empty);
    }

    private IRenderedComponent<DashboardStats> Render()
    {
        return _context.Render<DashboardStats>(parameters => parameters.Add(p => p.BotName, TestBotName));
    }

    private void ReturnCounts(params ChatLanguageCount[] counts)
    {
        _databaseService.GetChatCountsByLanguage().Returns(counts);
    }

    /// <summary>
    /// Creates <paramref name="count"/> languages named <c>l01</c>, <c>l02</c>, ... with <paramref name="count"/>,
    /// <paramref name="count"/> - 1, ... 1 chats.
    /// </summary>
    private static ChatLanguageCount[] Languages(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new ChatLanguageCount($"l{i:00}", count - i + 1))
            .ToArray();
    }

    /// <summary>
    /// Reads each breakdown row's text with whitespace collapsed, e.g. <c>en-US 6 · 60%</c>.
    /// </summary>
    private static List<string> Rows(IRenderedComponent<DashboardStats> cut)
    {
        return cut.FindAll(".stat-breakdown > div")
            .Select(row => string.Join(' ', row.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
    }
}
