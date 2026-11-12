using BotFarm.Shared.Components;
using Bunit;
using Bunit.Rendering;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Sections;
using MudBlazor.Services;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="BotDashboard"/> inside a host with the layout's outlets and checks where it places the
/// standard cards, the bot's extra content, the title, the logo and the footer.
/// </summary>
[TestFixture]
public class BotDashboardRenderTests
{
    private const string TestBotName = "TestBot";

    private BunitContext _context = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        // The cards need MongoDB and Telegram services; the dashboard only places them.
        StubCard<DashboardBackups>("backups", p => p.Get(x => x.BotName));
        StubCard<DashboardChats>("chats", p => p.Get(x => x.BotName));
        StubCard<DashboardWebhook>("webhook", p => p.Get(x => x.BotName));
        StubCard<DashboardStats>("stats", p => p.Get(x => x.BotName));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Render_PlacesStandardCardsForTheBotInBothColumns()
    {
        // Act
        var cut = RenderInHost();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CardsInColumn(cut, 0), Is.EqualTo(new[] { "backups", "chats" }));
            Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "webhook", "stats" }));
            Assert.That(cut.FindAll("[data-card]").Select(c => c.GetAttribute("data-bot")),
                Is.All.EqualTo(TestBotName));
        }
    }

    [Test]
    public void Render_PlacesExtraContentAfterStandardCards()
    {
        // Act
        var cut = RenderInHost(
            (nameof(BotDashboard.MainExtra), Card("main-extra")),
            (nameof(BotDashboard.SideExtra), Card("side-extra")),
            (nameof(BotDashboard.ChildContent), Card("full-width")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CardsInColumn(cut, 0), Is.EqualTo(new[] { "backups", "chats", "main-extra" }));
            Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "webhook", "stats", "side-extra" }));
            Assert.That(cut.Find("#body > [data-card='full-width']"), Is.Not.Null);
        }
    }

    [Test]
    public void Render_WithStats_ReplacesStandardStatsCard()
    {
        // Act
        var cut = RenderInHost((nameof(BotDashboard.Stats), Card("custom-stats")));

        // Assert
        Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "webhook", "custom-stats" }));
    }

    [Test]
    public void Render_ShowsBotNameAsTitleAndLogoAltWithFooter()
    {
        // Act
        var cut = RenderInHost(
            (nameof(BotDashboard.LogoSrc), "img/testbot_logo.png"),
            (nameof(BotDashboard.Footer), (RenderFragment)(b => b.AddContent(0, "A test footer"))));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find("#title").TextContent.Trim(), Is.EqualTo(TestBotName));
            Assert.That(cut.Find("#logo img").GetAttribute("src"), Is.EqualTo("img/testbot_logo.png"));
            Assert.That(cut.Find("#logo img").GetAttribute("alt"), Is.EqualTo(TestBotName));
            Assert.That(cut.Find("#footer").TextContent.Trim(), Is.EqualTo("A test footer"));
        }
    }

    [Test]
    public void Render_WithoutLogoOrFooter_LeavesTheirOutletsEmpty()
    {
        // Act
        var cut = RenderInHost();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll("#logo img"), Is.Empty);
            Assert.That(cut.Find("#footer").TextContent.Trim(), Is.Empty);
        }
    }

    private void StubCard<TCard>(string name, Func<CapturedParameterView<TCard>, string> botName)
        where TCard : IComponent =>
        _context.ComponentFactories.AddStub<TCard>(p => builder =>
            builder.AddMarkupContent(0, $"<div data-card=\"{name}\" data-bot=\"{botName(p)}\"></div>"));

    private static RenderFragment Card(string name) => builder =>
        builder.AddMarkupContent(0, $"<div data-card=\"{name}\"></div>");

    private static string?[] CardsInColumn(IRenderedComponent<ContainerFragment> cut, int column) =>
        cut.FindAll(".mud-grid-item")[column]
            .QuerySelectorAll("[data-card]")
            .Select(c => c.GetAttribute("data-card"))
            .ToArray();

    // The dashboard writes its title, logo and footer through <SectionContent>, which renders only where
    // SectionOutlets exist, as they do in MainLayout.
    private IRenderedComponent<ContainerFragment> RenderInHost(params (string Name, object Value)[] parameters) =>
        _context.Render(builder =>
        {
            builder.AddContent(0, Outlet("logo", "LogoSection"));
            builder.AddContent(1, Outlet("title", "TitleSection"));
            builder.OpenElement(2, "div");
            builder.AddAttribute(3, "id", "body");
            builder.OpenComponent<BotDashboard>(4);
            builder.AddAttribute(5, nameof(BotDashboard.BotName), TestBotName);
            foreach (var (name, value) in parameters)
            {
                builder.AddAttribute(6, name, value);
            }
            builder.CloseComponent();
            builder.CloseElement();
            builder.AddContent(7, Outlet("footer", "FooterSection"));
        });

    private static RenderFragment Outlet(string id, string sectionName) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", id);
        builder.OpenComponent<SectionOutlet>(2);
        builder.AddAttribute(3, nameof(SectionOutlet.SectionName), sectionName);
        builder.CloseComponent();
        builder.CloseElement();
    };
}
