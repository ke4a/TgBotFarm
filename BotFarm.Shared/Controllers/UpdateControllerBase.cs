using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Controllers;

/// <summary>
/// Provides the shared webhook endpoint behavior for bot update controllers.
/// </summary>
public abstract class UpdateControllerBase : ControllerBase
{
    private readonly IUpdateService _updateService;
    private readonly IWebhookSecretProvider _webhookSecretProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the controller with the bot-specific update service and webhook secret provider.
    /// </summary>
    protected UpdateControllerBase(
        IUpdateService updateService,
        IWebhookSecretProvider webhookSecretProvider,
        ILogger logger)
    {
        _updateService = updateService;
        _webhookSecretProvider = webhookSecretProvider;
        _logger = logger;
    }

    /// <summary>
    /// Forwards a Telegram webhook update when the request carries the bot's webhook secret
    /// and the bot is ready to process it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        if (!HasValidSecretToken())
        {
            // Never log the received or expected secret.
            _logger.LogWarning(
                "{BotName} rejected a webhook request with a missing or invalid secret token.",
                _updateService.Name);

            return Unauthorized();
        }

        if (!_updateService.CanProcessUpdates)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        await _updateService.ProcessUpdate(update);

        return Ok();
    }

    private bool HasValidSecretToken()
    {
        if (!Request.Headers.TryGetValue(BotFarm.Core.Constants.SecretTokenHeaderName, out var headerValues)
            || headerValues.Count != 1
            || string.IsNullOrEmpty(headerValues[0]))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(_webhookSecretProvider.GetSecret(_updateService.Name));
        var actual = Encoding.UTF8.GetBytes(headerValues[0]!);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
