using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="LanguagePicker"/>, opens its menu and checks the listed languages and the navigation a
/// choice starts.
/// </summary>
[TestFixture]
public class LanguagePickerTests : AdminUiTestFixture
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
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Open_ListsEveryLanguageAndMarksCurrentOne()
    {
        // Arrange
        using var _ = new CultureScope("uk-UA");
        var provider = _context.Render<MudPopoverProvider>();
        var cut = _context.Render<LanguagePicker>();

        // Act
        cut.Find("button").Click();

        // Assert
        provider.WaitForNUnitAssertion(() =>
        {
            var items = provider.FindAll(".mud-menu-item");
            var current = items.Where(item => item.GetAttribute("aria-current") == "true").ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(items.Select(item => item.TextContent.Trim()),
                    Is.EqualTo(new[] { "English", "Русский", "Українська" }));
                Assert.That(current, Has.Count.EqualTo(1));
                Assert.That(current[0].TextContent.Trim(), Is.EqualTo("Українська"));
            }
        });
    }

    [Test]
    public void Render_HasLocalizedLabel()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");

        // Act
        var cut = _context.Render<LanguagePicker>();

        // Assert
        Assert.That(cut.Find("button").GetAttribute("aria-label"), Is.EqualTo("Язык интерфейса"));
    }

    [Test]
    public void Choose_NavigatesToCultureEndpointWithCurrentPageAsReturnUrl()
    {
        // Arrange
        var navigation = _context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("http://localhost/testbot/dashboard?tab=1");
        var provider = _context.Render<MudPopoverProvider>();
        var cut = _context.Render<LanguagePicker>();
        cut.Find("button").Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".mud-menu-item"), Is.Not.Empty));

        // Act
        provider.FindAll(".mud-menu-item").Single(item => item.TextContent.Trim() == "Русский").Click();

        // Assert
        var last = navigation.History.First();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(last.Uri, Is.EqualTo("culture/set?culture=ru-RU&returnUrl=%2Ftestbot%2Fdashboard%3Ftab%3D1"));
            Assert.That(last.Options.ForceLoad, Is.True);
        }
    }
}
