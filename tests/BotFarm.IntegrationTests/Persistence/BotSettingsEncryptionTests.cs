using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.IntegrationTests.Persistence;

/// <summary>
/// Saves bot settings through the provider with a real data protection key ring and inspects the stored document.
/// </summary>
[TestFixture]
public class BotSettingsEncryptionTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456:TEST-TOKEN-ENCRYPTION";
    private const string WebhookSecret = "test_encryption-webhook-secret";

    private string _databaseName = null!;
    private TempDirectoryScope _keys = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseName = $"botsettings_{Guid.NewGuid():N}";
        _keys = new TempDirectoryScope("botsettings-keys");
    }

    [TearDown]
    public async Task TearDown()
    {
        await MongoServer.Client.DropDatabaseAsync(_databaseName);
        _keys.Dispose();
    }

    [Test]
    public async Task SavedDocument_ContainsNeitherTheTokenNorTheWebhookSecret()
    {
        var database = MongoServer.Client.GetDatabase(_databaseName);
        var provider = CreateProvider(database);
        await provider.LoadAsync(BotName);

        await provider.PersistAsync(BotName, new BotSettingsChange
        {
            Token = Token,
            AdminChatId = 42,
            WebhookSecret = WebhookSecret,
            ChangedBy = "admin"
        });

        var raw = await database.GetCollection<BsonDocument>(MongoBotSettingsStore.SettingsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Empty)
            .SingleAsync();
        var json = raw.ToJson();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json.Contains(Token, StringComparison.Ordinal), Is.False, "The stored document contains the token.");
            Assert.That(json.Contains("TEST-TOKEN-ENCRYPTION", StringComparison.Ordinal), Is.False, "The stored document contains the token's secret part.");
            Assert.That(json.Contains(WebhookSecret, StringComparison.Ordinal), Is.False, "The stored document contains the webhook secret.");
            Assert.That(raw["TelegramBotId"].ToInt64(), Is.EqualTo(123456));
        }

        // A provider with the same key ring reads the secrets back.
        var reloaded = CreateProvider(database);
        Assert.That(await reloaded.LoadAsync(BotName), Is.EqualTo(BotSettingsState.Configured));
    }

    private BotSettingsProvider CreateProvider(IMongoDatabase database) =>
        new(
            new MongoBotSettingsStore(database),
            new BotSettingsProtector(DataProtectionProvider.Create(new DirectoryInfo(_keys.RootPath))),
            TimeProvider.System,
            NullLogger<BotSettingsProvider>.Instance);
}
