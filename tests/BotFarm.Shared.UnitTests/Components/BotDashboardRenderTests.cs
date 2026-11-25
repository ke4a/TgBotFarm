using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using Bunit;
using Bunit.Rendering;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="BotDashboard"/> inside a host with the layout's outlets and checks where it places the
/// standard cards, the bot's extra content, the title, the logo and the footer.
/// </summary>
[TestFixture]
public class BotDashboardRenderTests
{
    private const string TestBotName = "TestBot";
    private const string MetricsUrl = "http://localhost:3000/d/botfarm/botfarm?var-bot=TestBot";

    private BunitContext _context = default!;
    private IMetricsLinks _metricsLinks = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        // The cards need MongoDB and Telegram services; the dashboard only places them.
        StubCard<DashboardBackups>("backups", p => p.Get(x => x.BotName));
        StubCard<DashboardChats>("chats", p => p.Get(x => x.BotName));
        StubCard<DashboardBotSettings>("settings", p => p.Get(x => x.BotName));
        StubCard<DashboardWebhook>("webhook", p => p.Get(x => x.BotName));
        StubCard<DashboardStats>("stats", p => p.Get(x => x.BotName));
        _context.ComponentFactories.AddStub<MetricsLink>(p => builder =>
            builder.AddMarkupContent(0, $"<a data-link=\"metrics\" href=\"{p.Get(x => x.Href)}\"></a>"));
        _metricsLinks = Substitute.For<IMetricsLinks>();
        _metricsLinks.ForBot(Arg.Any<string>()).Returns((string?)null);
        _context.Services.AddSingleton(_metricsLinks);
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
            Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "settings", "webhook", "stats" }));
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
            Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "settings", "webhook", "stats", "side-extra" }));
            Assert.That(cut.Find("#body > [data-card='full-width']"), Is.Not.Null);
        }
    }

    [Test]
    public void Render_WithDashboardAddress_ShowsTheBotsMetricsLinkInTheAppBarActions()
    {
        // Arrange
        _metricsLinks.ForBot(TestBotName).Returns(MetricsUrl);

        // Act
        var cut = RenderInHost();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll("[data-link='metrics']"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("#actions [data-link='metrics']").GetAttribute("href"), Is.EqualTo(MetricsUrl));
            Assert.That(cut.Find("#body").QuerySelectorAll("[data-link='metrics']"), Is.Empty);
            Assert.That(cut.FindAll(".mud-grid-item"), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Render_WithoutDashboardAddress_ShowsNoMetricsLink()
    {
        // Act
        var cut = RenderInHost();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll("[data-link='metrics']"), Is.Empty);
            Assert.That(cut.Find("#actions").InnerHtml.Trim(), Is.Empty);
            Assert.That(cut.FindAll(".mud-grid-item"), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Render_WithStats_ReplacesStandardStatsCard()
    {
        // Act
        var cut = RenderInHost((nameof(BotDashboard.Stats), Card("custom-stats")));

        // Assert
        Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "settings", "webhook", "custom-stats" }));
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

    [Test]
    public async Task SettingsSaved_FromEitherCard_RecreatesBothCardsWithoutRenderError()
    {
        // Arrange
        var cut = RenderInHost();
        var settings = cut.FindComponent<Stub<DashboardBotSettings>>().Instance;
        var webhook = cut.FindComponent<Stub<DashboardWebhook>>().Instance;

        // Act
        await cut.InvokeAsync(() => settings.Parameters.Get(x => x.OnSaved).InvokeAsync());
        await cut.InvokeAsync(() => webhook.Parameters.Get(x => x.OnSettingsSaved).InvokeAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindComponent<Stub<DashboardBotSettings>>().Instance, Is.Not.SameAs(settings));
            Assert.That(cut.FindComponent<Stub<DashboardWebhook>>().Instance, Is.Not.SameAs(webhook));
            Assert.That(CardsInColumn(cut, 1), Is.EqualTo(new[] { "settings", "webhook", "stats" }));
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

    // The dashboard writes its title, logo, app bar actions and footer through <SectionContent>, which renders only where
    // SectionOutlets exist, as they do in MainLayout.
    private IRenderedComponent<ContainerFragment> RenderInHost(params (string Name, object Value)[] parameters) =>
        _context.Render(builder =>
        {
            builder.AddContent(0, Outlet("logo", "LogoSection"));
            builder.AddContent(1, Outlet("title", "TitleSection"));
            builder.AddContent(8, Outlet("actions", "ActionsSection"));
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
