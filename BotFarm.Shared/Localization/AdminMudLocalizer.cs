using System.Globalization;
using BotFarm.Core.Localization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace BotFarm.Shared.Localization;

/// <summary>
/// Supplies MudBlazor's built-in texts from the current UI culture's <c>BotFarm.Shared</c> translation file.
/// </summary>
/// <remarks>
/// Keys use MudBlazor's own names, such as <c>MudTablePager_NextPage</c>. A key missing from the current culture's
/// file is reported as not found, without falling back to <c>en-US</c>, so MudBlazor shows its built-in English text.
/// </remarks>
internal sealed class AdminMudLocalizer(JsonAdminStringLocalizerFactory factory) : MudLocalizer
{
    private const string SharedSet = "BotFarm.Shared";

    /// <inheritdoc />
    public override LocalizedString this[string key] => Find(key);

    /// <inheritdoc />
    public override LocalizedString this[string key, params object[] arguments]
    {
        get
        {
            var value = Find(key);
            return value.ResourceNotFound
                ? value
                : new LocalizedString(key, string.Format(CultureInfo.CurrentCulture, value.Value, arguments));
        }
    }

    private LocalizedString Find(string key) =>
        factory.GetValue(SharedSet, CultureInfo.CurrentUICulture.Name, key) is { } value
            ? new LocalizedString(key, value)
            : new LocalizedString(key, key, resourceNotFound: true);
}
