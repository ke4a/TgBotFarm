using System.Text.RegularExpressions;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Telegram.Bot.Types;

namespace BotFarm.Core.Services;

/// <summary>
/// Builds the command menus a bot registers with Telegram's <c>setMyCommands</c>, from the shared <c>/start</c> and
/// <c>/changelanguage</c> commands and the bot's <see cref="ICommandHandler"/> instances.
/// </summary>
/// <remarks>
/// For the default language and each language the bot has a language file for, there are three lists:
/// <list type="bullet">
/// <item><see cref="BotCommandScopeDefault"/>, seen in private chats: every command;</item>
/// <item><see cref="BotCommandScopeAllGroupChats"/>, seen by group members: <see cref="CommandVisibility.Everyone"/>
/// commands;</item>
/// <item><see cref="BotCommandScopeAllChatAdministrators"/>, seen by group administrators:
/// <see cref="CommandVisibility.Everyone"/> and <see cref="CommandVisibility.GroupAdmins"/> commands, since
/// Telegram shows administrators only this list.</item>
/// </list>
/// A command whose description is missing in a language is left out of that language's lists.
/// </remarks>
public static partial class BotCommandMenu
{
    /// <summary>The most commands Telegram accepts in one list.</summary>
    public const int MaxCommands = 100;

    /// <summary>The longest command description Telegram accepts.</summary>
    public const int MaxDescriptionLength = 256;

    /// <summary>Language file key of the <c>/start</c> description.</summary>
    public const string StartDescriptionKey = "StartCommandDescription";

    /// <summary>Language file key of the <c>/changelanguage</c> description.</summary>
    public const string ChangeLanguageDescriptionKey = "ChangeLanguageCommandDescription";

    private static readonly MenuEntry[] SharedCommands =
    [
        new(Constants.Commands.Start, StartDescriptionKey, CommandVisibility.PrivateChats),
        new(Constants.Commands.ChangeLanguage, ChangeLanguageDescriptionKey, CommandVisibility.Everyone)
    ];

    /// <summary>
    /// One <c>setMyCommands</c> call: the commands shown in <paramref name="Scope"/> to users whose Telegram
    /// language is <paramref name="LanguageCode"/>, or to users of any other language when it is null.
    /// </summary>
    public sealed record CommandList(BotCommandScope Scope, string? LanguageCode, IReadOnlyList<BotCommand> Commands);

    /// <summary>
    /// Builds every command list of the bot named <paramref name="botName"/>. The list without a language code uses
    /// <see cref="Constants.DefaultLanguage"/> texts; each other language is registered under its two-letter code,
    /// e.g. <c>uk</c> for <c>uk-UA</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A command in the menu breaks a Telegram limit: its name is not 1-32 lowercase letters, digits or underscores
    /// after the <c>/</c>, its description is longer than <see cref="MaxDescriptionLength"/>, it appears twice, or
    /// a list has more than <see cref="MaxCommands"/> commands.
    /// </exception>
    public static IReadOnlyList<CommandList> Build(
        string botName,
        IEnumerable<ICommandHandler> handlers,
        ILocalizationService localizationService)
    {
        var entries = SharedCommands
            .Concat(handlers.Select(handler => new MenuEntry(handler.Command, handler.DescriptionKey, handler.Visibility)))
            .ToList();

        var languages = localizationService.GetAvailableLanguages(botName)
            .Select(locale => (Code: (string?)ToLanguageCode(locale), Locale: locale))
            .Prepend((Code: null, Locale: Constants.DefaultLanguage))
            .DistinctBy(language => language.Code);

        var lists = new List<CommandList>();
        foreach (var (code, locale) in languages)
        {
            var menu = new List<(CommandVisibility Visibility, BotCommand Command)>();
            foreach (var entry in entries)
            {
                var description = entry.DescriptionKey is null
                    ? string.Empty
                    : localizationService.GetLocalizedString(botName, entry.DescriptionKey, locale).Trim();
                if (description.Length > 0)
                {
                    menu.Add((entry.Visibility, ToBotCommand(botName, locale, entry.Command, description)));
                }
            }

            lists.Add(ToList(botName, new BotCommandScopeDefault(), code, menu.Select(item => item.Command)));
            lists.Add(ToList(botName, new BotCommandScopeAllGroupChats(), code,
                menu.Where(item => item.Visibility == CommandVisibility.Everyone).Select(item => item.Command)));
            lists.Add(ToList(botName, new BotCommandScopeAllChatAdministrators(), code,
                menu.Where(item => item.Visibility is CommandVisibility.Everyone or CommandVisibility.GroupAdmins)
                    .Select(item => item.Command)));
        }

        return lists;
    }

    private static string ToLanguageCode(string locale)
    {
        return locale.Split('-')[0].ToLowerInvariant();
    }

    private static BotCommand ToBotCommand(string botName, string locale, string command, string description)
    {
        if (!command.StartsWith('/') || !CommandNamePattern().IsMatch(command[1..]))
        {
            throw new InvalidOperationException(
                $"Bot '{botName}': command '{command}' can't be in the command menu. Telegram needs a '/' followed by " +
                "1-32 lowercase letters, digits or underscores.");
        }

        if (description.Length > MaxDescriptionLength)
        {
            throw new InvalidOperationException(
                $"Bot '{botName}': the {locale} description of command '{command}' has {description.Length} " +
                $"characters. Telegram allows at most {MaxDescriptionLength}.");
        }

        return new BotCommand(command[1..], description);
    }

    private static CommandList ToList(string botName, BotCommandScope scope, string? code, IEnumerable<BotCommand> commands)
    {
        var list = commands.ToList();
        var language = code ?? "default";

        var duplicate = list.GroupBy(command => command.Command).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Bot '{botName}': command '/{duplicate.Key}' appears more than once in the {language} " +
                $"{scope.Type} command menu.");
        }

        if (list.Count > MaxCommands)
        {
            throw new InvalidOperationException(
                $"Bot '{botName}': the {language} {scope.Type} command menu has {list.Count} commands. " +
                $"Telegram allows at most {MaxCommands}.");
        }

        return new CommandList(scope, code, list);
    }

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex CommandNamePattern();

    private sealed record MenuEntry(string Command, string? DescriptionKey, CommandVisibility Visibility);
}
