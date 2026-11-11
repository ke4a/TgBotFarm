using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="DashboardBackups"/> with one backup and checks its texts and date formatting per language.
/// </summary>
[TestFixture]
public class DashboardBackupsRenderTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";

    private BunitContext _context = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();

        var localBackupService = Substitute.For<ILocalBackupHelperService>();
        localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(
        [
            new BackupInfo
            {
                Name = "backup.zip",
                Size = 1536,
                Date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            },
        ]));
        _context.Services.AddSingleton(localBackupService);
        _context.Services.AddSingleton(Substitute.For<IBackupService>());
        _context.Render<MudPopoverProvider>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Render_InRussian_ShowsRussianMonthDecimalCommaAndTexts()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");

        // Act
        var cut = _context.Render<DashboardBackups>(parameters => parameters.Add(x => x.BotName, TestBotName));

        // Assert
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("td.cell-mono"), Has.Count.EqualTo(1)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find(".card-header h6").TextContent.Trim(), Is.EqualTo("Резервные копии"));
            Assert.That(cut.Find("td[data-label='Дата']").TextContent, Does.Match(@"^15 янв\S* 2026 "));
            Assert.That(cut.Find("td[data-label='Размер']").TextContent.Trim(), Is.EqualTo("1,5 КиБ"));
            Assert.That(cut.FindAll("button[aria-label='Восстановить']"), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Render_InEnglish_ShowsEnglishMonthAndTexts()
    {
        // Act
        var cut = _context.Render<DashboardBackups>(parameters => parameters.Add(x => x.BotName, TestBotName));

        // Assert
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("td.cell-mono"), Has.Count.EqualTo(1)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find(".card-header h6").TextContent.Trim(), Is.EqualTo("Backups"));
            Assert.That(cut.Find("td[data-label='Date']").TextContent, Does.StartWith("15 Jan 2026 "));
            Assert.That(cut.Find("td[data-label='Size']").TextContent.Trim(), Is.EqualTo("1.5 KiB"));
        }
    }
}
