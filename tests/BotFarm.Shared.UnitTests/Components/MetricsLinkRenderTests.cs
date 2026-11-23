using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using MudBlazor.Services;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="MetricsLink"/> and checks the link it shows, or that it shows nothing without an address.
/// </summary>
[TestFixture]
public class MetricsLinkRenderTests : AdminUiTestFixture
{
    private const string Url = "http://localhost:3000/d/botfarm/botfarm?var-bot=TestBot";

    private BunitContext _context = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [TestCase("en-US", "Metrics")]
    [TestCase("ru-RU", "Метрики")]
    [TestCase("uk-UA", "Метрики")]
    public void WithHref_RendersAnIconLinkWithATranslatedLabelThatOpensInANewTab(string culture, string text)
    {
        using var _ = new CultureScope(culture);

        var cut = _context.Render<MetricsLink>(parameters => parameters.Add(p => p.Href, Url));

        var link = cut.Find("a");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(link.GetAttribute("aria-label"), Is.EqualTo(text));
            Assert.That(link.GetAttribute("href"), Is.EqualTo(Url));
            Assert.That(link.GetAttribute("target"), Is.EqualTo("_blank"));
            Assert.That(link.GetAttribute("rel"), Is.EqualTo("noopener noreferrer"));
            Assert.That(link.QuerySelector("svg"), Is.Not.Null, "The link has no icon.");
        }
    }

    [Test]
    public void WithoutHref_RendersNothing()
    {
        var cut = _context.Render<MetricsLink>(parameters => parameters.Add(p => p.Href, null));

        Assert.That(cut.Markup.Trim(), Is.Empty);
    }
}
