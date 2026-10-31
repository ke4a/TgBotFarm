using BotFarm.Core.Abstractions;
using BotFarm.Core.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Telegram.Bot.Types;

namespace BotFarm.Core.Controllers;

/// <summary>
/// Provides the shared webhook endpoint behavior for bot update controllers.
/// </summary>
public abstract class UpdateControllerBase : ControllerBase
{
    /// <summary>
    /// The number of times an update that keeps failing with temporary errors is attempted before it is dropped.
    /// </summary>
    public const int MaxTransientAttempts = 5;

    private readonly IUpdateService _updateService;
    private readonly IWebhookSecretProvider _webhookSecretProvider;
    private readonly IUpdateDeliveryGuard _deliveryGuard;
    private readonly INotificationService _notificationService;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the controller with the bot-specific update service, the webhook secret provider,
    /// the guard that tracks accepted updates and alerts, and the service used to alert admins.
    /// </summary>
    protected UpdateControllerBase(
        IUpdateService updateService,
        IWebhookSecretProvider webhookSecretProvider,
        IUpdateDeliveryGuard deliveryGuard,
        INotificationService notificationService,
        ILogger logger)
    {
        _updateService = updateService;
        _webhookSecretProvider = webhookSecretProvider;
        _deliveryGuard = deliveryGuard;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Forwards a Telegram webhook update when the request carries the bot's webhook secret
    /// and the bot is ready to process it.
    /// </summary>
    /// <remarks>
    /// An update that was already accepted for this bot recently is acknowledged without being processed again.
    /// When processing fails, the error is logged and admins are alerted (at most once a minute per bot).
    /// After a temporary failure, such as an unreachable database or network, the request answers
    /// 503 Service Unavailable so Telegram delivers the update again later; after
    /// <see cref="MaxTransientAttempts"/> such failures the update is dropped. Any other failure is
    /// acknowledged so Telegram moves on to the next update.
    /// </remarks>
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

        if (!_deliveryGuard.TryAcceptUpdate(_updateService.Name, update.Id))
        {
            _logger.LogInformation(
                "{BotName} skipped update {UpdateId} because it was already accepted.",
                _updateService.Name,
                update.Id);

            return Ok();
        }

        try
        {
            await _updateService.ProcessUpdate(update);
        }
        catch (Exception ex) when (TransientErrors.IsTransient(ex))
        {
            // Temporary failures are likely to pass, so ask Telegram to deliver the update again,
            // but give up after a few attempts so one update cannot block the ones queued behind it.
            if (_deliveryGuard.TryReleaseForRetry(_updateService.Name, update.Id, MaxTransientAttempts, out var attempts))
            {
                _logger.LogWarning(
                    ex,
                    "{BotName} failed to process update {UpdateId} of type {UpdateType} on attempt {Attempt} of {MaxAttempts}; Telegram will deliver it again.",
                    _updateService.Name,
                    update.Id,
                    update.Type,
                    attempts,
                    MaxTransientAttempts);

                await TrySendErrorAlert(
                    update,
                    $"Failed to process update {update.Id} of type {update.Type} (attempt {attempts} of {MaxTransientAttempts}); Telegram will retry it.");

                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            _logger.LogError(
                ex,
                "{BotName} dropped update {UpdateId} of type {UpdateType} after {Attempts} failed attempts.",
                _updateService.Name,
                update.Id,
                update.Type,
                attempts);

            await TrySendErrorAlert(
                update,
                $"Dropped update {update.Id} of type {update.Type} after {attempts} failed attempts.");

            return Ok();
        }
        catch (Exception ex)
        {
            // Acknowledge the update anyway: an error that is not temporary would fail again on every
            // delivery and block the updates queued behind it.
            _logger.LogError(
                ex,
                "{BotName} failed to process update {UpdateId} of type {UpdateType}.",
                _updateService.Name,
                update.Id,
                update.Type);

            await TrySendErrorAlert(update, $"Failed to process update {update.Id} of type {update.Type}.");
        }

        _deliveryGuard.ClearFailures(_updateService.Name, update.Id);

        return Ok();
    }

    private async Task TrySendErrorAlert(Update update, string text)
    {
        if (!_deliveryGuard.TryAcquireAlertSlot(_updateService.Name))
        {
            return;
        }

        try
        {
            await _notificationService.SendErrorNotification(
                text,
                _updateService.Name,
                update.Message ?? update.EditedMessage ?? update.CallbackQuery?.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{BotName} could not send the error alert for update {UpdateId}.",
                _updateService.Name,
                update.Id);
        }
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
