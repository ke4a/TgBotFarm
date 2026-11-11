using BotFarm.Core.Localization;
using BotFarm.Shared.Localization;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFarm.Shared.UnitTests.Localization;

/// <summary>
/// Checks that MudBlazor's texts come from the current culture's <c>BotFarm.Shared</c> file only, so a missing
/// key leaves MudBlazor's built-in English in place.
/// </summary>
[TestFixture]
public class AdminMudLocalizerTests
{
    private const string NextPageKey = "MudTablePager_NextPage";

    [Test]
    public void Russian_KeyInRussianFile_ReturnsRussianValue()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");
        var localizer = CreateShippedLocalizer();

        // Act
        var value = localizer[NextPageKey];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.ResourceNotFound, Is.False);
            Assert.That(value.Value, Is.EqualTo("Следующая страница"));
        }
    }

    [Test]
    public void English_KeyInEnglishFile_ReturnsEnglishValue()
    {
        // Arrange
        using var _ = new CultureScope("en-US");
        var localizer = CreateShippedLocalizer();

        // Act
        var value = localizer[NextPageKey];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.ResourceNotFound, Is.False);
            Assert.That(value.Value, Is.EqualTo("Next page"));
        }
    }

    [Test]
    public void Russian_KeyOnlyInEnglishFile_IsNotFoundWithoutFallback()
    {
        // Arrange
        using var scope = new TempDirectoryScope();
        var setPath = Directory.CreateDirectory(scope.GetPath("BotFarm.Shared")).FullName;
        File.WriteAllText(Path.Combine(setPath, "en-US.json"), """{ "LanguageName": "English", "MudInput_Clear": "Clear" }""");
        File.WriteAllText(Path.Combine(setPath, "ru-RU.json"), """{ "LanguageName": "Русский" }""");
        var localizer = new AdminMudLocalizer(new JsonAdminStringLocalizerFactory(NullLoggerFactory.Instance, scope.RootPath));
        using var _ = new CultureScope("ru-RU");

        // Act
        var value = localizer["MudInput_Clear"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.ResourceNotFound, Is.True);
            Assert.That(value.Value, Is.EqualTo("MudInput_Clear"));
        }
    }

    [Test]
    public void Russian_UnknownKeyWithArguments_IsNotFound()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");
        var localizer = CreateShippedLocalizer();

        // Act
        var value = localizer["MudUnknown_Key", 1];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.ResourceNotFound, Is.True);
            Assert.That(value.Value, Is.EqualTo("MudUnknown_Key"));
        }
    }

    private static AdminMudLocalizer CreateShippedLocalizer() =>
        new(new JsonAdminStringLocalizerFactory(NullLoggerFactory.Instance));
}
