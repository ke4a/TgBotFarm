using Microsoft.AspNetCore.DataProtection;

namespace BotFarm.Core.Services;

/// <summary>
/// Protects and unprotects the secrets in a bot's stored settings with the host's data-protection key ring.
/// A value protected for one bot cannot be unprotected for another.
/// </summary>
internal sealed class BotSettingsProtector
{
    /// <summary>
    /// The purpose every bot settings protector is created with, followed by the bot name.
    /// </summary>
    public const string Purpose = "BotFarm.BotSettings.v1";

    private readonly IDataProtectionProvider _dataProtectionProvider;

    public BotSettingsProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <summary>
    /// Protects <paramref name="value"/> for the named bot.
    /// </summary>
    public string Protect(string botName, string value) => CreateProtector(botName).Protect(value);

    /// <summary>
    /// Unprotects a value protected for the named bot.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The value was protected for another bot, with a key the key ring no longer has, or was altered.
    /// </exception>
    public string Unprotect(string botName, string protectedValue) =>
        CreateProtector(botName).Unprotect(protectedValue);

    private IDataProtector CreateProtector(string botName) =>
        _dataProtectionProvider.CreateProtector(Purpose, botName);
}
