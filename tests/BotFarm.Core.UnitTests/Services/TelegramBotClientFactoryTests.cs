using ClientFactory = BotFarm.Core.Services.TelegramBotClientFactory;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class TelegramBotClientFactoryTests
{
    private const string Token = "123456:TEST-TOKEN";

    private ClientFactory _factory = default!;

    [SetUp]
    public void SetUp()
    {
        _factory = new ClientFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public void Handler_RecyclesConnectionsAndKeepsNoCookies()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_factory.Handler.PooledConnectionIdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(_factory.Handler.PooledConnectionLifetime, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(_factory.Handler.UseCookies, Is.False);
        }
    }

    [Test]
    public void Create_GivesEachClientItsOwnTimeout()
    {
        var withDefault = _factory.Create(Token);
        var withCustom = _factory.Create(Token, TimeSpan.FromSeconds(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withDefault.Timeout, Is.EqualTo(ClientFactory.DefaultTimeout));
            Assert.That(withCustom.Timeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
    }

    [Test]
    public void Dispose_DisposesTheSharedHandler()
    {
        _factory.Create(Token);

        _factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _factory.Handler.PooledConnectionLifetime = TimeSpan.FromMinutes(1));
    }
}
