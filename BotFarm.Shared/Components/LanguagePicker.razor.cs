using System.Globalization;
using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BotFarm.Shared.Components;

/// <summary>
/// App bar menu that lists the supported admin UI languages, marks the current one, and switches to the chosen
/// language by reloading the current page through the language switch endpoint.
/// </summary>
public partial class LanguagePicker : ComponentBase
{
    [Inject] private IAdminLanguageCatalog Catalog { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IStringLocalizer<LanguagePicker> L { get; set; } = default!;

    private static bool IsCurrent(AdminLanguage language) =>
        string.Equals(language.Culture.Name, CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Navigates, with a full page load, to <c>culture/set</c> for <paramref name="cultureName"/>, passing the
    /// current page as the URL to return to. The full load starts a new circuit in the chosen language.
    /// </summary>
    private void Choose(string cultureName)
    {
        var returnUrl = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        Navigation.NavigateTo(
            $"culture/set?culture={Uri.EscapeDataString(cultureName)}&returnUrl={Uri.EscapeDataString(returnUrl)}",
            forceLoad: true);
    }
}
