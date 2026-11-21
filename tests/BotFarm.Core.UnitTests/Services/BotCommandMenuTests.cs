using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotCommandMenuTests
{
    private const string BotName = "TestBot";

    private ILocalizationService _localization = null!;
    private Dictionary<(string Key, string Language), string> _texts = null!;

    [SetUp]
    public void SetUp()
    {
        _texts = new Dictionary<(string Key, string Language), string>
        {
            [(BotCommandMenu.StartDescriptionKey, "en-US")] = "Start",
            [(BotCommandMenu.StartDescriptionKey, "uk-UA")] = "Почати",
            [(BotCommandMenu.ChangeLanguageDescriptionKey, "en-US")] = "Change language",
            [(BotCommandMenu.ChangeLanguageDescriptionKey, "uk-UA")] = "Змінити мову",
            [("ClearDescription", "en-US")] = "Clear chat data",
            [("ClearDescription", "uk-UA")] = "Видалити дані",
            [("GifDescription", "en-US")] = "Last GIF",
            [("GifDescription", "uk-UA")] = "Остання гіфка",
            [("SecretDescription", "en-US")] = "Secret"
        };
        _localization = Substitute.For<ILocalizationService>();
        _localization.GetAvailableLanguages(BotName).Returns(["en-US", "uk-UA"]);
        _localization.GetLocalizedString(BotName, Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => _texts.GetValueOrDefault((call.ArgAt<string>(1), call.ArgAt<string>(2)), string.Empty));
    }

    [Test]
    public void Build_RegistersEveryScopeForTheDefaultListAndEachLanguage()
    {
        var lists = BotCommandMenu.Build(BotName, [], _localization);

        Assert.That(lists.Select(list => (list.Scope.Type, list.LanguageCode)), Is.EqualTo(new[]
        {
            (BotCommandScopeType.Default, null),
            (BotCommandScopeType.AllGroupChats, null),
            (BotCommandScopeType.AllChatAdministrators, null),
            (BotCommandScopeType.Default, "en"),
            (BotCommandScopeType.AllGroupChats, "en"),
            (BotCommandScopeType.AllChatAdministrators, "en"),
            (BotCommandScopeType.Default, "uk"),
            (BotCommandScopeType.AllGroupChats, "uk"),
            (BotCommandScopeType.AllChatAdministrators, "uk")
        }));
    }

    [Test]
    public void Build_ShowsEachCommandInTheScopesOfItsVisibility()
    {
        ICommandHandler[] handlers =
        [
            CreateHandler("/getlastgif", "GifDescription", CommandVisibility.Everyone),
            CreateHandler("/clearchatdata", "ClearDescription", CommandVisibility.GroupAdmins),
            CreateHandler("/secret", "SecretDescription", CommandVisibility.PrivateChats)
        ];

        var lists = BotCommandMenu.Build(BotName, handlers, _localization);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Names(lists, BotCommandScopeType.Default, null),
                Is.EqualTo(new[] { "start", "changelanguage", "getlastgif", "clearchatdata", "secret" }));
            Assert.That(Names(lists, BotCommandScopeType.AllGroupChats, null),
                Is.EqualTo(new[] { "changelanguage", "getlastgif" }));
            Assert.That(Names(lists, BotCommandScopeType.AllChatAdministrators, null),
                Is.EqualTo(new[] { "changelanguage", "getlastgif", "clearchatdata" }));
        }
    }

    [Test]
    public void Build_UsesTheTextsOfEachLanguageAndTheDefaultLanguageForTheListWithoutCode()
    {
        var handlers = new[] { CreateHandler("/getlastgif", "GifDescription", CommandVisibility.Everyone) };

        var lists = BotCommandMenu.Build(BotName, handlers, _localization);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Descriptions(lists, BotCommandScopeType.AllGroupChats, null),
                Is.EqualTo(new[] { "Change language", "Last GIF" }));
            Assert.That(Descriptions(lists, BotCommandScopeType.AllGroupChats, "uk"),
                Is.EqualTo(new[] { "Змінити мову", "Остання гіфка" }));
        }
    }

    [Test]
    public void Build_LeavesOutCommandsWithoutDescription()
    {
        _texts.Remove((BotCommandMenu.StartDescriptionKey, "uk-UA"));
        _texts[("GifDescription", "en-US")] = "   ";
        ICommandHandler[] handlers =
        [
            CreateHandler("/nokey", null, CommandVisibility.Everyone),
            CreateHandler("/missingkey", "MissingDescription", CommandVisibility.Everyone),
            CreateHandler("/getlastgif", "GifDescription", CommandVisibility.Everyone)
        ];

        var lists = BotCommandMenu.Build(BotName, handlers, _localization);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Names(lists, BotCommandScopeType.Default, "en"), Is.EqualTo(new[] { "start", "changelanguage" }));
            Assert.That(Names(lists, BotCommandScopeType.Default, "uk"),
                Is.EqualTo(new[] { "changelanguage", "getlastgif" }));
        }
    }

    [Test]
    public void Build_WithTheSameLanguageCodeTwice_RegistersItOnce()
    {
        _localization.GetAvailableLanguages(BotName).Returns(["en-US", "en-GB"]);

        var lists = BotCommandMenu.Build(BotName, [], _localization);

        Assert.That(lists.Select(list => list.LanguageCode).Distinct(), Is.EqualTo(new[] { null, "en" }));
    }

    [TestCase("getlastgif")]
    [TestCase("/GetLastGif")]
    [TestCase("/get-last-gif")]
    [TestCase("/")]
    [TestCase("/abcdefghijklmnopqrstuvwxyz0123456")]
    public void Build_WithInvalidCommandName_Throws(string command)
    {
        var handlers = new[] { CreateHandler(command, "GifDescription", CommandVisibility.Everyone) };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotCommandMenu.Build(BotName, handlers, _localization));

        Assert.That(exception?.Message, Does.Contain($"'{command}'"));
    }

    [Test]
    public void Build_WithInvalidCommandNameButNoDescription_DoesNotThrow()
    {
        var handlers = new[] { CreateHandler("/Hidden-Command", null, CommandVisibility.Everyone) };

        Assert.DoesNotThrow(() => BotCommandMenu.Build(BotName, handlers, _localization));
    }

    [Test]
    public void Build_AcceptsLongestCommandNameAndDescription()
    {
        _texts[("GifDescription", "en-US")] = new string('d', BotCommandMenu.MaxDescriptionLength);
        var handlers = new[] { CreateHandler("/" + new string('a', 32), "GifDescription", CommandVisibility.Everyone) };

        Assert.DoesNotThrow(() => BotCommandMenu.Build(BotName, handlers, _localization));
    }

    [Test]
    public void Build_WithTooLongDescription_Throws()
    {
        _texts[("GifDescription", "uk-UA")] = new string('d', BotCommandMenu.MaxDescriptionLength + 1);
        var handlers = new[] { CreateHandler("/getlastgif", "GifDescription", CommandVisibility.Everyone) };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotCommandMenu.Build(BotName, handlers, _localization));

        Assert.That(exception?.Message, Does.Contain("uk-UA").And.Contain("/getlastgif"));
    }

    [Test]
    public void Build_WithDuplicateCommand_Throws()
    {
        var handlers = new[] { CreateHandler("/start", "GifDescription", CommandVisibility.Everyone) };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotCommandMenu.Build(BotName, handlers, _localization));

        Assert.That(exception?.Message, Does.Contain("/start"));
    }

    [Test]
    public void Build_WithMoreThanMaxCommandsInAList_Throws()
    {
        // With /start and /changelanguage, private chats get MaxCommands + 1 commands.
        var handlers = Enumerable.Range(0, BotCommandMenu.MaxCommands - 1)
            .Select(index => CreateHandler($"/command{index}", "GifDescription", CommandVisibility.GroupAdmins))
            .ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotCommandMenu.Build(BotName, handlers, _localization));

        Assert.That(exception?.Message, Does.Contain($"{BotCommandMenu.MaxCommands + 1} commands"));
    }

    [Test]
    public void Build_WithMaxCommandsInAList_DoesNotThrow()
    {
        var handlers = Enumerable.Range(0, BotCommandMenu.MaxCommands - 2)
            .Select(index => CreateHandler($"/command{index}", "GifDescription", CommandVisibility.Everyone))
            .ToArray();

        var lists = BotCommandMenu.Build(BotName, handlers, _localization);

        Assert.That(lists.Max(list => list.Commands.Count), Is.EqualTo(BotCommandMenu.MaxCommands));
    }

    [Test]
    public void DefaultInterfaceMembers_HideTheCommandFromTheMenuAndShowItToEveryone()
    {
        ICommandHandler handler = new HandlerWithoutMenuMembers();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.DescriptionKey, Is.Null);
            Assert.That(handler.Visibility, Is.EqualTo(CommandVisibility.Everyone));
        }
    }

    private static ICommandHandler CreateHandler(string command, string? descriptionKey, CommandVisibility visibility)
    {
        var handler = Substitute.For<ICommandHandler>();
        handler.Command.Returns(command);
        handler.DescriptionKey.Returns(descriptionKey);
        handler.Visibility.Returns(visibility);
        return handler;
    }

    private static IEnumerable<string> Names(
        IEnumerable<BotCommandMenu.CommandList> lists, BotCommandScopeType scope, string? languageCode) =>
        Find(lists, scope, languageCode).Commands.Select(command => command.Command);

    private static IEnumerable<string> Descriptions(
        IEnumerable<BotCommandMenu.CommandList> lists, BotCommandScopeType scope, string? languageCode) =>
        Find(lists, scope, languageCode).Commands.Select(command => command.Description);

    private static BotCommandMenu.CommandList Find(
        IEnumerable<BotCommandMenu.CommandList> lists, BotCommandScopeType scope, string? languageCode) =>
        lists.Single(list => list.Scope.Type == scope && list.LanguageCode == languageCode);

    private sealed class HandlerWithoutMenuMembers : ICommandHandler
    {
        public string Command => "/plain";

        public Task Handle(Message message, string language) => Task.CompletedTask;
    }
}
