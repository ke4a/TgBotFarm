using BotFarm.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class JsonLocalizationServiceTests
{
    private JsonLocalizationService _service;
    private IWebHostEnvironment _hostingEnvironment;
    private IFileProvider _fileProvider;

    [SetUp]
    public void SetUp()
    {
        _hostingEnvironment = Substitute.For<IWebHostEnvironment>();
        _fileProvider = Substitute.For<IFileProvider>();
        _hostingEnvironment.ContentRootFileProvider.Returns(_fileProvider);

        var directoryContents = Substitute.For<IDirectoryContents>();
        directoryContents.Exists.Returns(true);
        directoryContents.GetEnumerator().Returns(new List<IFileInfo>
        {
            new TestFileInfo { Name = "Bot1", IsDirectory = true, PhysicalPath = "Languages/Bot1" },
            new TestFileInfo { Name = "Bot2", IsDirectory = true, PhysicalPath = "Languages/Bot2" }
        }.GetEnumerator());

        _fileProvider.GetDirectoryContents("Languages").Returns(directoryContents);

        Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
        Directory.CreateDirectory("Languages/Bot1");
        Directory.CreateDirectory("Languages/Bot2");

        File.WriteAllText("Languages/Bot1/en.json", "{\"hello\": \"Hello\"}");
        File.WriteAllText("Languages/Bot1/es.json", "{\"hello\": \"Hola\"}");
        File.WriteAllText("Languages/Bot2/en.json", "{\"hello\": \"Hello\"}");

        _service = new JsonLocalizationService();
    }

    [Test]
    public void GetAvailableLanguages_ShouldReturnCorrectLanguages()
    {
        var languages = _service.GetAvailableLanguages("Bot1").ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(languages, Has.Count.EqualTo(2));
            Assert.That(languages, Does.Contain("en"));
            Assert.That(languages, Does.Contain("es"));
        }
    }

    [Test]
    public void GetLocalizedString_ShouldReturnCorrectString()
    {
        var localizedString = _service.GetLocalizedString("Bot1", "hello", "en");
        Assert.That(localizedString, Is.EqualTo("Hello"));

        localizedString = _service.GetLocalizedString("Bot1", "hello", "es");
        Assert.That(localizedString, Is.EqualTo("Hola"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete("Languages", true);
    }

    private class TestFileInfo : IFileInfo
    {
        public bool Exists { get; set; } = true;
        public long Length { get; set; }
        public string PhysicalPath { get; set; }
        public string Name { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public bool IsDirectory { get; set; }
        public Stream CreateReadStream() => new MemoryStream();
    }
}
