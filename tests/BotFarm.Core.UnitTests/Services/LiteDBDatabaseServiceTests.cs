using BotFarm.Core.Services;
using BotFarm.Core.Services.Interfaces;
using LiteDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services
{
    [TestFixture]
    public class LiteDBDatabaseServiceTests
    {
        private TestableLiteDBDatabaseService _service;
        private ILogger<LiteDBDatabaseService> _logger;
        private IHostApplicationLifetime _appLifetime;
        private INotificationService _notificationService;
        private string _testDatabasePath;
        private string _testDatabaseName;

        [SetUp]
        public void SetUp()
        {
            _logger = Substitute.For<ILogger<LiteDBDatabaseService>>();
            _appLifetime = Substitute.For<IHostApplicationLifetime>();
            _notificationService = Substitute.For<INotificationService>();
            
            _testDatabaseName = "TestDatabase.db";
            _testDatabasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _testDatabaseName);
            
            // Clean up any existing test database
            if (File.Exists(_testDatabasePath))
            {
                File.Delete(_testDatabasePath);
            }

            _service = new TestableLiteDBDatabaseService(
                _logger,
                _appLifetime,
                _notificationService,
                _testDatabaseName);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.DisposeInstance();
            
            if (File.Exists(_testDatabasePath))
            {
                File.Delete(_testDatabasePath);
            }
        }

        [Test]
        public void Constructor_SetsPropertiesCorrectly()
        {
            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_service.Name, Is.EqualTo("TestService"));
                Assert.That(_service.DatabaseName, Is.EqualTo(_testDatabaseName));
                Assert.That(_service.GetInstance(), Is.Not.Null);
            });
        }

        [Test]
        public void GetCollectionNames_WithExistingCollections_ReturnsCollectionNames()
        {
            // Arrange
            var collection1 = _service.GetInstance().GetCollection<BsonDocument>("collection1");
            var collection2 = _service.GetInstance().GetCollection<BsonDocument>("collection2");

            collection1.Insert(new BsonDocument { ["_id"] = 1, ["name"] = "test1" });
            collection2.Insert(new BsonDocument { ["_id"] = 2, ["name"] = "test2" });

            // Act
            var collectionNames = _service.GetCollectionNames().ToList();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(collectionNames, Has.Count.EqualTo(2));
                Assert.That(collectionNames, Contains.Item("collection1"));
                Assert.That(collectionNames, Contains.Item("collection2"));
            });
        }

        [Test]
        public void GetCollectionNames_WithEmptyDatabase_ReturnsEmptyCollection()
        {
            // Act
            var collectionNames = _service.GetCollectionNames().ToList();

            // Assert
            Assert.That(collectionNames, Is.Empty);
        }

        [Test]
        public void GetCollectionData_WithExistingData_ReturnsDocuments()
        {
            // Arrange
            var collection = _service.GetInstance().GetCollection<BsonDocument>("testCollection");
            var doc1 = new BsonDocument { ["_id"] = 1, ["name"] = "test1", ["value"] = 100 };
            var doc2 = new BsonDocument { ["_id"] = 2, ["name"] = "test2", ["value"] = 200 };
            
            collection.Insert(doc1);
            collection.Insert(doc2);

            // Act
            var data = _service.GetCollectionData("testCollection").ToList();

            // Assert
            Assert.That(data, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(data.Any(d => d["name"] == "test1"), Is.True);
                Assert.That(data.Any(d => d["name"] == "test2"), Is.True);
                Assert.That(data.Any(d => d["value"] == 100), Is.True);
                Assert.That(data.Any(d => d["value"] == 200), Is.True);
            });
        }

        [Test]
        public void GetCollectionData_WithNonExistentCollection_ReturnsEmptyCollection()
        {
            // Act
            var data = _service.GetCollectionData("nonExistentCollection").ToList();

            // Assert
            Assert.That(data, Is.Empty);
        }

        [Test]
        public async Task Release_WithValidInstance_ReturnsTrue()
        {
            // Arrange
            Assert.That(_service.GetInstance(), Is.Not.Null);

            // Act
            var result = await _service.Release();

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public async Task Reconnect_WithInvalidDatabasePath_ReturnsFalse()
        {
            // Arrange
            _service.DisposeInstance();
            _service.SetInstance(null);
            _service.SetDatabaseName("invalid\\path\\with\\invalid\\characters?.db");

            // Act
            var result = await _service.Reconnect();

            // Assert
            Assert.That(result, Is.False);
            await _notificationService.Received(1).SendErrorNotification(
                Arg.Is<string>(s => s.Contains("Could not reconnect to database")), 
                "TestService");
            _appLifetime.Received(1).StopApplication();
        }

        [Test]
        public async Task Reconnect_AfterRelease_SuccessfullyReconnects()
        {
            // Arrange
            var collection = _service.GetInstance().GetCollection<BsonDocument>("testCollection");
            collection.Insert(new BsonDocument { ["_id"] = 1, ["name"] = "test" });
            
            // Act
            await _service.Release();
            var reconnectResult = await _service.Reconnect();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(reconnectResult, Is.True);
                Assert.That(_service.GetInstance(), Is.Not.Null);
            });

            // Verify data persisted
            var persistedData = _service.GetCollectionData("testCollection").ToList();
            Assert.Multiple(() =>
            {
                Assert.That(persistedData, Has.Count.EqualTo(1));
                Assert.That(persistedData.First()["name"].AsString, Is.EqualTo("test"));
            });
        }

        [Test]
        public void GetCollectionNames_AfterAddingAndRemovingCollections_ReturnsCorrectNames()
        {
            // Arrange
            var collection1 = _service.GetInstance().GetCollection<BsonDocument>("temp1");
            var collection2 = _service.GetInstance().GetCollection<BsonDocument>("temp2");
            var collection3 = _service.GetInstance().GetCollection<BsonDocument>("persistent");

            collection1.Insert(new BsonDocument { ["_id"] = 1 });
            collection2.Insert(new BsonDocument { ["_id"] = 2 });
            collection3.Insert(new BsonDocument { ["_id"] = 3 });
            
            // Act - Drop some collections
            _service.GetInstance().DropCollection("temp1");
            _service.GetInstance().DropCollection("temp2");

            var remainingCollections = _service.GetCollectionNames().ToList();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(remainingCollections, Has.Count.EqualTo(1));
                Assert.That(remainingCollections, Contains.Item("persistent"));
                Assert.That(remainingCollections, Does.Not.Contain("temp1"));
                Assert.That(remainingCollections, Does.Not.Contain("temp2"));
            });
        }

        private class TestableLiteDBDatabaseService : LiteDBDatabaseService
        {
            public TestableLiteDBDatabaseService(
                ILogger<LiteDBDatabaseService> logger,
                IHostApplicationLifetime appLifetime,
                INotificationService notificationService,
                string databaseName) : base(logger, appLifetime, notificationService)
            {
                Name = "TestService";
                DatabaseName = databaseName;
                Instance = new LiteDatabase(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DatabaseName));
            }

            // Test helper methods to manipulate internal state
            public LiteDatabase GetInstance()
            {
                return Instance;
            }

            public void SetInstance(LiteDatabase? instance)
            {
                Instance = instance;
            }

            public void SetDatabaseName(string databaseName)
            {
                DatabaseName = databaseName;
            }

            public void DisposeInstance()
            {
                Instance?.Dispose();
            }
        }
    }
}