using System.Globalization;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace BotFarm.Core.UnitTests.Localization;

/// <summary>
/// Checks which supported admin UI language <see cref="AcceptLanguageMatchProvider"/> picks from the
/// <c>Accept-Language</c> header.
/// </summary>
[TestFixture]
public class AcceptLanguageMatchProviderTests
{
    private AcceptLanguageMatchProvider _provider = default!;

    [SetUp]
    public void SetUp()
    {
        _provider = new AcceptLanguageMatchProvider(new StubCatalog("en-US", "ru-RU", "uk-UA"));
    }

    [TestCase("uk-UA", "uk-UA")]
    [TestCase("UK-ua", "uk-UA")]
    [TestCase("ru-BY", "ru-RU")]
    [TestCase("uk", "uk-UA")]
    [TestCase("en-GB", "en-US")]
    [TestCase("de-DE,ru;q=0.8", "ru-RU")]
    [TestCase("ru;q=0.5,uk;q=0.9", "uk-UA")]
    [TestCase("ru;q=0.9,uk;q=0.9", "ru-RU")]
    [TestCase("uk;q=0,ru;q=0.1", "ru-RU")]
    [TestCase("*,ru;q=0.5", "ru-RU")]
    [TestCase("x-klingon,ru", "ru-RU")]
    [TestCase("de,fr,it,es,pt,uk", "uk-UA")]
    public async Task SupportedMatch_ReturnsTheSupportedCulture(string header, string expected)
    {
        // Act
        var result = await DetermineAsync(header);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Cultures.Select(culture => culture.Value), Is.EqualTo(new[] { expected }));
            Assert.That(result.UICultures.Select(culture => culture.Value), Is.EqualTo(new[] { expected }));
        }
    }

    [TestCase("de,fr")]
    [TestCase("*")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("ru;q=abc,,;;==")]
    [TestCase("\"ru\"")]
    [TestCase("uk;q=0")]
    public async Task NoSupportedMatch_ReturnsNoResult(string? header)
    {
        // Act
        var result = await DetermineAsync(header);

        // Assert
        Assert.That(result, Is.Null);
    }

    private Task<ProviderCultureResult?> DetermineAsync(string? header)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers.AcceptLanguage = header;
        }

        return _provider.DetermineProviderCultureResult(context);
    }

    private sealed class StubCatalog(params string[] cultures) : IAdminLanguageCatalog
    {
        public IReadOnlyList<AdminLanguage> Languages { get; } =
            cultures.Select(name => new AdminLanguage(CultureInfo.GetCultureInfo(name), name)).ToArray();

        public AdminLanguage Default => Languages[0];

        public AdminLanguage? Find(string? culture) =>
            Languages.FirstOrDefault(language =>
                string.Equals(language.Culture.Name, culture, StringComparison.OrdinalIgnoreCase));
    }
}
