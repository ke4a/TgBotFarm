using System.IO.Compression;
using BotFarm.Core;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Core.Telemetry;
using BotFarm.IntegrationTests.Support;
using BotFarm.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.IntegrationTests.Backups;

/// <summary>
/// Backs up a bot database on a real MongoDB server, changes it, and restores the backup.
/// </summary>
[TestFixture]
public class MongoDbBackupServiceTests
{
    private const string ItemsCollection = "Items";

    private TempDirectoryScope _dataDirectory = null!;
    private BotDatabaseScope<PlatformDatabaseService> _bot = null!;
    private MongoDbBackupService _backupService = null!;
    private AppDataPaths _dataPaths = null!;

    private PlatformDatabaseService DatabaseService => _bot.Service;

    private IMongoDatabase Database => _bot.Database;

    [SetUp]
    public void SetUp()
    {
        _dataDirectory = new TempDirectoryScope("BotFarmBackupTests");
        _bot = PlatformDatabaseService.CreateScope();

        var registry = Substitute.For<IBotRegistry>();
        registry.HasService<IBotService>(_bot.BotName).Returns(true);
        registry.GetService<IMongoDbDatabaseService>(_bot.BotName).Returns(DatabaseService);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AppDataPaths.ConfigurationKey] = _dataDirectory.RootPath
            })
            .Build();
        var dataPaths = new AppDataPaths(configuration);
        _dataPaths = dataPaths;
        var notificationService = Substitute.For<INotificationService>();

        _backupService = new MongoDbBackupService(
            registry,
            RunningActionsWithoutPausing(),
            NullLogger<MongoDbBackupService>.Instance,
            notificationService,
            new LocalBackupHelperService(
                NullLogger<LocalBackupHelperService>.Instance,
                notificationService,
                dataPaths),
            _bot.Cache,
            dataPaths,
            new BotEventMetrics(new ServiceCollection().AddMetrics().BuildServiceProvider()
                .GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
    }

    [TearDown]
    public async Task TearDown()
    {
        await SettingsCollection().DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", _bot.BotName));
        await _bot.DisposeAsync();
        _dataDirectory.Dispose();
    }

    [Test]
    public async Task RestoreBackup_BringsBackTheDocumentsIndexesAndChatSettingsSavedInTheBackup()
    {
        var items = Database.GetCollection<BsonDocument>(ItemsCollection);
        await items.InsertManyAsync([Item(1, "a"), Item(2, "b")]);
        await items.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("code"),
            new CreateIndexOptions { Name = "code_unique", Unique = true }));
        await DatabaseService.SetChatLanguage<ChatSettings>(-100, "ru-RU");

        var fileName = await BackupAsync();

        // Change everything the backup holds; the cached chat language changes along with the stored one.
        await items.DeleteOneAsync(new BsonDocument("_id", 2));
        await items.InsertOneAsync(Item(3, "c"));
        await items.Indexes.DropOneAsync("code_unique");
        await items.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("added"),
            new CreateIndexOptions { Name = "added_after_backup" }));
        await DatabaseService.SetChatLanguage<ChatSettings>(-100, "en-US");

        var result = await _backupService.RestoreBackup(fileName, _bot.BotName);

        var indexes = await (await items.Indexes.ListAsync()).ToListAsync();
        var codeIndex = indexes.SingleOrDefault(index => index["name"] == "code_unique");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, string.Join("; ", result.Errors));
            Assert.That(await ItemIds(), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(indexes.Select(index => index["name"].AsString), Is.EquivalentTo(new[] { "_id_", "code_unique" }));
            Assert.That(codeIndex?.GetValue("unique", false).ToBoolean(), Is.True);
            Assert.That(
                await DatabaseService.GetChatLanguage<ChatSettings>(-100),
                Is.EqualTo("ru-RU"),
                "The chat language should come from the restored data, not the cache.");
            Assert.That(await CollectionNames(), Is.EquivalentTo(new[] { ItemsCollection, nameof(ChatSettings) }));
        }

        // The restored unique index is in force.
        Assert.That(
            async () => await items.InsertOneAsync(Item(4, "a")),
            Throws.InstanceOf<MongoWriteException>());
    }

    [Test]
    public async Task RestoreBackup_OfAnEmptyCollection_EmptiesItAndKeepsItsIndexes()
    {
        await Database.CreateCollectionAsync(ItemsCollection);
        var items = Database.GetCollection<BsonDocument>(ItemsCollection);
        await items.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("code"),
            new CreateIndexOptions { Name = "code_1" }));

        var fileName = await BackupAsync();
        await items.InsertOneAsync(Item(1, "added after the backup"));

        var result = await _backupService.RestoreBackup(fileName, _bot.BotName);

        var indexes = await (await items.Indexes.ListAsync()).ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, string.Join("; ", result.Errors));
            Assert.That(await CollectionNames(), Is.EquivalentTo(new[] { ItemsCollection }));
            Assert.That(await ItemIds(), Is.Empty);
            Assert.That(indexes.Select(index => index["name"].AsString), Is.EquivalentTo(new[] { "_id_", "code_1" }));
        }
    }

    [Test]
    public async Task RestoreBackup_OfACollectionLargerThanOneBatch_RestoresEveryDocument()
    {
        const int documentCount = MongoDbBackupService.RestoreBatchSize * 2 + 7;
        var items = Database.GetCollection<BsonDocument>(ItemsCollection);
        await items.InsertManyAsync(Enumerable.Range(1, documentCount).Select(id => Item(id, $"code {id}")));

        var fileName = await BackupAsync();
        await Database.DropCollectionAsync(ItemsCollection);

        var result = await _backupService.RestoreBackup(fileName, _bot.BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, string.Join("; ", result.Errors));
            Assert.That(await ItemIds(), Is.EqualTo(Enumerable.Range(1, documentCount)));
        }
    }

    [Test]
    public async Task BackupAndRestore_LeaveTheBotsSettingsUnchangedAndTheArchiveHoldsNoSettings()
    {
        const string protectedToken = "PROTECTED-TOKEN-FOR-BACKUP-TEST";
        const string protectedWebhookSecret = "PROTECTED-SECRET-FOR-BACKUP-TEST";
        var store = new MongoBotSettingsStore(MongoServer.Client.GetDatabase(MongoBotSettingsStore.DatabaseName));
        await store.UpsertAsync(new BotSettings
        {
            BotName = _bot.BotName,
            ProtectedToken = protectedToken,
            TelegramBotId = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000),
            AdminChatId = 42,
            ProtectedWebhookSecret = protectedWebhookSecret,
            Emoji = "🤖",
            UpdatedAtUtc = new DateTime(2026, 11, 20, 9, 15, 0, DateTimeKind.Utc),
            UpdatedBy = "admin"
        });
        var before = await SettingsCollection().Find(Builders<BsonDocument>.Filter.Eq("_id", _bot.BotName)).SingleAsync();
        await Database.GetCollection<BsonDocument>(ItemsCollection).InsertOneAsync(Item(1, "a"));

        var fileName = await BackupAsync();
        var result = await _backupService.RestoreBackup(fileName, _bot.BotName);

        var after = await SettingsCollection().Find(Builders<BsonDocument>.Filter.Eq("_id", _bot.BotName)).SingleAsync();
        using var archive = ZipFile.OpenRead(Path.Combine(_dataPaths.Backups, _bot.BotName, fileName));
        var archiveContent = new List<string>();
        foreach (var entry in archive.Entries)
        {
            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            archiveContent.Add(System.Text.Encoding.UTF8.GetString(memory.ToArray()));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, string.Join("; ", result.Errors));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(
                archive.Entries.Select(entry => entry.Name),
                Has.None.Contains(MongoBotSettingsStore.SettingsCollectionName));
            Assert.That(archiveContent.Any(content => content.Contains(protectedToken, StringComparison.Ordinal)), Is.False);
            Assert.That(
                archiveContent.Any(content => content.Contains(protectedWebhookSecret, StringComparison.Ordinal)),
                Is.False);
        }
    }

    private static IMongoCollection<BsonDocument> SettingsCollection() =>
        MongoServer.Client.GetDatabase(MongoBotSettingsStore.DatabaseName)
            .GetCollection<BsonDocument>(MongoBotSettingsStore.SettingsCollectionName);

    private async Task<string> BackupAsync()
    {
        var result = await _backupService.BackupDatabase(_bot.BotName);
        Assert.That(result.IsSuccess, Is.True, string.Join("; ", result.Errors));

        return (string)result.Successes.Single().Metadata["fileName"];
    }

    private async Task<IReadOnlyList<int>> ItemIds()
    {
        var documents = await Database.GetCollection<BsonDocument>(ItemsCollection)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync();

        return documents.Select(document => document["_id"].AsInt32).ToList();
    }

    private async Task<IReadOnlyList<string>> CollectionNames()
    {
        return await (await Database.ListCollectionNamesAsync()).ToListAsync();
    }

    private static BsonDocument Item(int id, string code)
    {
        return new BsonDocument { { "_id", id }, { "code", code }, { "added", DateTime.UtcNow } };
    }

    /// <summary>
    /// A bot control service that runs maintenance actions straight away, as it does for a bot whose webhook is
    /// already paused.
    /// </summary>
    private static IBotControlService RunningActionsWithoutPausing()
    {
        var botControlService = Substitute.For<IBotControlService>();
        botControlService
            .RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<string>>>()(call.Arg<CancellationToken>()));
        botControlService
            .RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.Arg<CancellationToken>()));

        return botControlService;
    }
}
