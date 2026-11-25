using System.Security.Cryptography;
using BotFarm.Core.Services;
using Microsoft.AspNetCore.DataProtection;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotSettingsProtectorTests
{
    private const string Token = "123456:TEST-TOKEN";

    [Test]
    public void Unprotect_ValueProtectedForTheSameBot_ReturnsTheOriginal()
    {
        var protector = new BotSettingsProtector(new EphemeralDataProtectionProvider());

        var protectedValue = protector.Protect("TestBot", Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(protectedValue.Contains(Token, StringComparison.Ordinal), Is.False);
            Assert.That(protector.Unprotect("TestBot", protectedValue) == Token, Is.True);
        }
    }

    [Test]
    public void Unprotect_ValueProtectedForAnotherBot_Throws()
    {
        var protector = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        var protectedValue = protector.Protect("TestBot", Token);

        Assert.Catch<CryptographicException>(() => protector.Unprotect("OtherBot", protectedValue));
    }

    [Test]
    public void Unprotect_WithADifferentKeyRing_Throws()
    {
        var protectedValue = new BotSettingsProtector(new EphemeralDataProtectionProvider()).Protect("TestBot", Token);
        var otherKeyRing = new BotSettingsProtector(new EphemeralDataProtectionProvider());

        Assert.Catch<CryptographicException>(() => otherKeyRing.Unprotect("TestBot", protectedValue));
    }
}
