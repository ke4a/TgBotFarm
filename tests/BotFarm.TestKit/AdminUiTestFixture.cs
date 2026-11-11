using NUnit.Framework;

namespace BotFarm.TestKit;

/// <summary>
/// Base class for fixtures that render admin UI components. Runs every test with <c>en-US</c> as the current
/// culture and UI culture, so English text and formatting assertions hold on any machine.
/// </summary>
/// <remarks>
/// Derived fixtures register the localizer with
/// <see cref="AdminLocalizationTestExtensions.AddAdminLocalizationForTests"/>. A test that needs another language
/// switches it inside the test with <see cref="CultureScope"/>.
/// </remarks>
public abstract class AdminUiTestFixture
{
    private CultureScope? cultureScope;

    /// <summary>
    /// Switches to <c>en-US</c> before the derived fixture's own set-up runs.
    /// </summary>
    [SetUp]
    public void SetUpAdminUiCulture()
    {
        cultureScope = new CultureScope("en-US");
    }

    /// <summary>
    /// Restores the previous cultures after the derived fixture's own tear-down runs.
    /// </summary>
    [TearDown]
    public void TearDownAdminUiCulture()
    {
        cultureScope?.Dispose();
        cultureScope = null;
    }
}
