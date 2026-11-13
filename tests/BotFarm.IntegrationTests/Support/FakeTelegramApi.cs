using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BotFarm.Core.Abstractions;
using Telegram.Bot;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// Stands in for the Telegram Bot API: creates bot clients whose requests are recorded and answered here instead of
/// being sent to Telegram.
/// </summary>
public sealed class FakeTelegramApi : ITelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, FakeBot> _botsByToken = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<TelegramApiCall> _calls = new();

    /// <summary>
    /// Every Bot API call made so far by any bot, in the order they were made.
    /// </summary>
    public IReadOnlyList<TelegramApiCall> Calls => _calls.ToArray();

    /// <summary>
    /// Adds the bot that signs its requests with <paramref name="token"/>. Its Telegram user ID is the number before
    /// the token's colon.
    /// </summary>
    public void AddBot(string botName, string token)
    {
        var userId = long.Parse(token[..token.IndexOf(':')]);
        _botsByToken[token] = new FakeBot(botName, userId, UsernameOf(botName));
    }

    /// <summary>
    /// The Telegram username <c>getMe</c> returns for the bot, such as <c>testbot_bot</c> for TestBot.
    /// </summary>
    public static string UsernameOf(string botName) => $"{botName.ToLowerInvariant()}_bot";

    /// <inheritdoc />
    public TelegramBotClient Create(string token)
    {
        if (!_botsByToken.ContainsKey(token))
        {
            throw new InvalidOperationException("The app asked for a bot client with a token the test did not set.");
        }

        return new TelegramBotClient(new TelegramBotClientOptions(token), new HttpClient(new Handler(this, token)));
    }

    /// <summary>
    /// The calls <paramref name="botName"/> made to the Bot API method <paramref name="method"/>, such as
    /// <c>sendMessage</c>, optionally only those for the chat <paramref name="chatId"/>.
    /// </summary>
    public IReadOnlyList<TelegramApiCall> CallsTo(string botName, string method, long? chatId = null)
    {
        return Calls
            .Where(call => call.BotName == botName
                           && call.Method == method
                           && (chatId == null || call.ChatId == chatId))
            .ToList();
    }

    private static string Answer(FakeBot bot, string method, JsonObject parameters)
    {
        JsonNode result = method switch
        {
            "getMe" => new JsonObject
            {
                ["id"] = bot.UserId,
                ["is_bot"] = true,
                ["first_name"] = bot.Name,
                ["username"] = bot.Username
            },
            "getWebhookInfo" => new JsonObject
            {
                ["url"] = string.Empty,
                ["has_custom_certificate"] = false,
                ["pending_update_count"] = 0
            },
            _ when method.StartsWith("send", StringComparison.Ordinal) => new JsonObject
            {
                ["message_id"] = 1,
                ["date"] = 0,
                ["chat"] = new JsonObject
                {
                    ["id"] = parameters["chat_id"]?.DeepClone(),
                    ["type"] = "private"
                }
            },
            _ => true
        };

        return new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString();
    }

    private sealed record FakeBot(string Name, long UserId, string Username);

    private sealed class Handler(FakeTelegramApi api, string token) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bot = api._botsByToken[token];
            // Bot API requests go to /bot<token>/<method>.
            var method = request.RequestUri!.Segments[^1];
            var content = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var parameters = string.IsNullOrEmpty(content) ? new JsonObject() : JsonNode.Parse(content)!.AsObject();

            api._calls.Enqueue(new TelegramApiCall(bot.Name, method, parameters));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Answer(bot, method, parameters), Encoding.UTF8, "application/json")
            };
        }
    }
}

/// <summary>
/// One Bot API call: the bot that made it, the method name and the JSON parameters sent with it.
/// </summary>
public sealed record TelegramApiCall(string BotName, string Method, JsonObject Parameters)
{
    /// <summary>
    /// The <c>chat_id</c> parameter, when the call has a numeric one.
    /// </summary>
    public long? ChatId =>
        Parameters["chat_id"] is JsonValue value && value.TryGetValue(out long chatId) ? chatId : null;
}
