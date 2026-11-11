using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace BotFarm.Localization;

/// <summary>
/// Describes the Identity errors the admin password and user-name policy can produce in the admin UI language,
/// using the <c>Identity.*</c> keys of the <c>BotFarm</c> translation set. Error codes are Identity's own.
/// </summary>
public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    private readonly IStringLocalizer<LocalizedIdentityErrorDescriber> _localizer;

    public LocalizedIdentityErrorDescriber(IStringLocalizer<LocalizedIdentityErrorDescriber> localizer)
    {
        _localizer = localizer;
    }

    /// <inheritdoc />
    public override IdentityError PasswordTooShort(int length) =>
        Localize(base.PasswordTooShort(length), "Identity.PasswordTooShort", length);

    /// <inheritdoc />
    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Localize(base.PasswordRequiresNonAlphanumeric(), "Identity.PasswordRequiresNonAlphanumeric");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUpper() =>
        Localize(base.PasswordRequiresUpper(), "Identity.PasswordRequiresUpper");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresLower() =>
        Localize(base.PasswordRequiresLower(), "Identity.PasswordRequiresLower");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresDigit() =>
        Localize(base.PasswordRequiresDigit(), "Identity.PasswordRequiresDigit");

    /// <inheritdoc />
    public override IdentityError DuplicateUserName(string userName) =>
        Localize(base.DuplicateUserName(userName), "Identity.DuplicateUserName", userName);

    /// <inheritdoc />
    public override IdentityError InvalidUserName(string? userName) =>
        Localize(base.InvalidUserName(userName), "Identity.InvalidUserName", userName ?? string.Empty);

    private IdentityError Localize(IdentityError error, string key, params object[] arguments) =>
        new() { Code = error.Code, Description = _localizer[key, arguments] };
}
