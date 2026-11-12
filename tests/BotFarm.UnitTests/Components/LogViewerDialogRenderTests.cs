using BotFarm.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace BotFarm.UnitTests.Components;

/// <summary>
/// Opens <see cref="LogViewerDialog"/> through MudBlazor's dialog provider, as the dashboard does, and checks
/// filtering, highlighting and live tail.
/// </summary>
[TestFixture]
public class LogViewerDialogRenderTests : AdminUiTestFixture
{
    private const string ModulePath = "./Components/LogViewerDialog.razor.js";
    private const string InfoLine = "2026-11-11 10:00:00.1234|0|INFO|BotFarm.Program|Started";
    private const string WarnLine = "2026-11-11 10:00:01.1234|0|WARN|BotFarm.Program|Slow response";
    private const string ErrorLine = "2026-11-11 10:00:02.1234|0|ERROR|BotFarm.Program|Failed";

    private BunitContext _context = default!;
    private BunitJSModuleInterop _module = default!;
    private TempDirectoryScope _scope = default!;
    private IRenderedComponent<MudDialogProvider> _provider = default!;
    private TimeSpan _originalPollInterval;
    private int _originalMaxRendered;

    [SetUp]
    public void SetUp()
    {
        _originalPollInterval = LogViewerDialog.PollInterval;
        _originalMaxRendered = LogViewerDialog.MaxRenderedEntries;
        LogViewerDialog.PollInterval = TimeSpan.FromMilliseconds(20);

        _scope = new TempDirectoryScope();
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        _module = _context.JSInterop.SetupModule(ModulePath);
        _provider = _context.Render<MudDialogProvider>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
        _scope.Dispose();
        LogViewerDialog.PollInterval = _originalPollInterval;
        LogViewerDialog.MaxRenderedEntries = _originalMaxRendered;
    }

    [Test]
    public async Task Open_HighlightsEntriesByLevel()
    {
        await OpenAsync("nlog-all.log", $"{InfoLine}\n{WarnLine}\n{ErrorLine}\n");

        Assert.That(EntryClasses(), Is.EqualTo(new[] { "log-entry--info", "log-entry--warn", "log-entry--error" }));
    }

    [Test]
    public async Task Filter_ErrorsOnly_HidesInfoAndWarnings()
    {
        await OpenAsync("log.1.txt", $"{InfoLine}\n{WarnLine}\n{ErrorLine}\n");

        _provider.FindAll(".mud-toggle-item").Single(item => item.TextContent.Contains("Error")).Click();

        Assert.That(EntryClasses(), Is.EqualTo(new[] { "log-entry--error" }));
        Assert.That(_provider.Find(".log-viewer-count").TextContent, Is.EqualTo("1 of 3 entries"));
    }

    [Test]
    public async Task ArchivedFile_HasNoLiveTailButton()
    {
        await OpenAsync("log.1.txt", $"{InfoLine}\n");

        Assert.That(_provider.FindAll(".log-viewer-tail"), Is.Empty);
    }

    [Test]
    public async Task LiveFile_ShowsAppendedEntriesAndPinsToBottom()
    {
        var path = await OpenAsync("nlog-all.log", $"{InfoLine}\n");

        await File.AppendAllTextAsync(path, $"{ErrorLine}\n");

        _provider.WaitForState(() => EntryClasses().Length == 2, TimeSpan.FromSeconds(5));
        Assert.That(EntryClasses(), Is.EqualTo(new[] { "log-entry--info", "log-entry--error" }));
        Assert.That(_module.VerifyInvoke("attach").Arguments[1], Is.EqualTo(true));
    }

    [Test]
    public async Task LiveFile_Paused_IgnoresAppendedEntries()
    {
        var path = await OpenAsync("nlog-all.log", $"{InfoLine}\n");

        _provider.Find(".log-viewer-tail").Click();
        Assert.That(_provider.Find(".log-viewer-tail").TextContent, Does.Contain("Paused"));

        await File.AppendAllTextAsync(path, $"{ErrorLine}\n");
        await Task.Delay(LogViewerDialog.PollInterval * 5);

        Assert.That(EntryClasses(), Is.EqualTo(new[] { "log-entry--info" }));
    }

    [Test]
    public async Task ManyEntries_RendersOnlyTheNewestAndSaysHowManyAreHidden()
    {
        LogViewerDialog.MaxRenderedEntries = 2;

        await OpenAsync("log.1.txt", $"{InfoLine}\n{WarnLine}\n{ErrorLine}\n");

        Assert.That(EntryClasses(), Is.EqualTo(new[] { "log-entry--warn", "log-entry--error" }));
        Assert.That(_provider.Find(".log-viewer-truncated").TextContent, Does.Contain("1 older entries"));
    }

    [Test]
    public async Task EmptyFile_SaysSo()
    {
        await OpenAsync("nlog-all.log", string.Empty);

        Assert.That(_provider.Find(".log-viewer").TextContent, Does.Contain("(File is empty)"));
    }

    private async Task<string> OpenAsync(string fileName, string content)
    {
        var path = _scope.GetPath(fileName);
        await File.WriteAllTextAsync(path, content);

        var parameters = new DialogParameters<LogViewerDialog> { { x => x.FilePath, path } };
        var dialogService = _context.Services.GetRequiredService<IDialogService>();
        await _provider.InvokeAsync(() => dialogService.ShowAsync<LogViewerDialog>(fileName, parameters));
        _provider.WaitForElement(".log-viewer[aria-busy='false']");
        return path;
    }

    private string[] EntryClasses() =>
        _provider.FindAll(".log-viewer .log-entry[class*='log-entry--']")
            .Select(entry => entry.ClassList.Single(name => name.StartsWith("log-entry--", StringComparison.Ordinal)))
            .ToArray();
}
