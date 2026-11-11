using BotFarm.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BotFarm.UnitTests.Components;

/// <summary>
/// Renders the host document <see cref="App"/> with its child components stubbed and checks that the page
/// declares the current UI language.
/// </summary>
[TestFixture]
public class AppRenderTests : AdminUiTestFixture
{
    private BunitContext _context = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddSingleton(Substitute.For<IHttpContextAccessor>());
        _context.ComponentFactories.AddStub<Routes>();
        _context.ComponentFactories.AddStub<HeadOutlet>();
        _context.ComponentFactories.AddStub<ReconnectModal>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    [TestCase("uk-UA")]
    public void Render_DeclaresCurrentUiLanguage(string culture)
    {
        // Arrange
        using var _ = new CultureScope(culture);

        // Act
        var cut = _context.Render<App>();

        // Assert
        Assert.That(cut.Markup, Does.Contain($"<html lang=\"{culture}\">"));
    }
}
