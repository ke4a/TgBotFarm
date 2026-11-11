using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using TestBot.Pages;

namespace TestBot.UnitTests.Pages;

/// <summary>
/// Renders <see cref="TestBotDashboard"/> inside a host with the layout's outlets and checks that its own texts
/// come from the <c>TestBot</c> admin translation set.
/// </summary>
[TestFixture]
public class TestBotDashboardTests : AdminUiTestFixture
{
    private BunitContext _context = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        // The cards need MongoDB and Telegram services; this page only places them.
        _context.ComponentFactories.AddStub<DashboardBackups>();
        _context.ComponentFactories.AddStub<DashboardChats>();
        _context.ComponentFactories.AddStub<DashboardWebhook>();
        _context.ComponentFactories.AddStub<DashboardStats>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [TestCase("en-US", "TestBot - BotFarm", "TestBot dashboard", "TestBot logo", "This is a test bot")]
    [TestCase("ru-RU", "TestBot - BotFarm", "Панель управления TestBot", "Логотип TestBot", "Это тестовый бот")]
    public void Render_ShowsTitleHeadingLogoAndFooterFromTestBotSet(
        string culture,
        string title,
        string heading,
        string logoAlt,
        string footer)
    {
        // Arrange
        using var _ = new CultureScope(culture);

        // Act
        var cut = RenderInHost();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find("title").TextContent, Is.EqualTo(title));
            Assert.That(cut.Find("h1.mud-sr-only").TextContent, Is.EqualTo(heading));
            Assert.That(cut.Find("#logo img").GetAttribute("alt"), Is.EqualTo(logoAlt));
            Assert.That(cut.Find("#footer").TextContent.Trim(), Is.EqualTo(footer));
            Assert.That(cut.Find("#title").TextContent.Trim(), Is.EqualTo(Constants.Name));
        }
    }

    [Test]
    public void TestOutput_ContainsTestBotAdminTranslationSet()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "AdminLanguages", "TestBot");

        // Act
        var exists = Directory.Exists(path);

        // Assert
        Assert.That(exists, Is.True, $"'{path}' does not exist.");
    }

    // The page writes its title through <PageTitle> and its logo, name and footer through <SectionContent>, which
    // render only where a HeadOutlet and SectionOutlets exist, as they do in MainLayout and App.
    private IRenderedComponent<ContainerFragment> RenderInHost() => _context.Render(builder =>
    {
        builder.OpenComponent<HeadOutlet>(0);
        builder.CloseComponent();
        builder.AddContent(1, Outlet("logo", "LogoSection"));
        builder.AddContent(2, Outlet("title", "TitleSection"));
        builder.OpenComponent<TestBotDashboard>(3);
        builder.CloseComponent();
        builder.AddContent(4, Outlet("footer", "FooterSection"));
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
