using BotFarm.Localization;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Identity;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Checks that the Identity errors the setup page can show follow the admin UI language and keep Identity's codes.
/// </summary>
[TestFixture]
public class LocalizedIdentityErrorDescriberTests : AdminUiTestFixture
{
    private static readonly IdentityErrorDescriber BaseDescriber = new();

    private static IEnumerable<TestCaseData> Errors()
    {
        yield return Case("PasswordTooShort", d => d.PasswordTooShort(8),
            "Пароль должен содержать не менее 8 символов.");
        yield return Case("PasswordRequiresNonAlphanumeric", d => d.PasswordRequiresNonAlphanumeric(),
            "Пароль должен содержать хотя бы один символ, не являющийся буквой или цифрой.");
        yield return Case("PasswordRequiresUpper", d => d.PasswordRequiresUpper(),
            "Пароль должен содержать хотя бы одну заглавную букву ('A'-'Z').");
        yield return Case("PasswordRequiresLower", d => d.PasswordRequiresLower(),
            "Пароль должен содержать хотя бы одну строчную букву ('a'-'z').");
        yield return Case("PasswordRequiresDigit", d => d.PasswordRequiresDigit(),
            "Пароль должен содержать хотя бы одну цифру ('0'-'9').");
        yield return Case("DuplicateUserName", d => d.DuplicateUserName("a"),
            "Имя пользователя «a» уже занято.");
        yield return Case("InvalidUserName", d => d.InvalidUserName("a"),
            "Имя пользователя «a» недопустимо: оно может содержать только буквы и цифры.");
    }

    private static TestCaseData Case(string name, Func<IdentityErrorDescriber, IdentityError> describe, string russian) =>
        new TestCaseData(describe, russian).SetArgDisplayNames(name);

    private static LocalizedIdentityErrorDescriber CreateDescriber() =>
        new(AdminLocalizationTestExtensions.CreateAdminLocalizer<LocalizedIdentityErrorDescriber>());

    [TestCaseSource(nameof(Errors))]
    public void Describe_Russian_ReturnsRussianTextWithBaseCode(
        Func<IdentityErrorDescriber, IdentityError> describe, string russian)
    {
        using var _ = new CultureScope("ru-RU");

        var error = describe(CreateDescriber());

        Assert.That(error.Description, Is.EqualTo(russian));
        Assert.That(error.Code, Is.EqualTo(describe(BaseDescriber).Code));
    }

    [TestCaseSource(nameof(Errors))]
    public void Describe_English_ReturnsIdentityEnglishTextWithBaseCode(
        Func<IdentityErrorDescriber, IdentityError> describe, string _)
    {
        var error = describe(CreateDescriber());
        var expected = describe(BaseDescriber);

        Assert.That(error.Description, Is.EqualTo(expected.Description));
        Assert.That(error.Code, Is.EqualTo(expected.Code));
    }
}
