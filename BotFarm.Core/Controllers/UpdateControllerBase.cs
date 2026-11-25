using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Helpers;
using BotFarm.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
    /// and the bot is ready to process it. A bot without a token answers 503 Service Unavailable, like a disabled bot.
    /// </summary>
    /// <remarks>
    /// An update that was already accepted for this bot recently is acknowledged without being processed again.
    /// When processing fails, the error is logged and admins are alerted (at most once a minute per bot).
    /// After a temporary failure, such as an unreachable database or network, the request answers
    /// 503 Service Unavailable so Telegram delivers the update again later; after
    /// <see cref="MaxTransientAttempts"/> such failures the update is dropped. Any other failure is
    /// acknowledged so Telegram moves on to the next update.
    /// Every processed update, whether its handling succeeds or fails, is counted in <paramref name="metrics"/> by
    /// its kind, with the time from just after the duplicate check until handling ended. Each delivery that is
    /// rejected, skipped as a duplicate, fails, is retried or is dropped is also counted by that outcome. Recording
    /// never changes the response.
    /// </remarks>
    /// <param name="update">The update Telegram delivered.</param>
    /// <param name="metrics">Records the bot's update metrics.</param>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update, [FromServices] UpdateMetrics metrics)
    {
        bool hasValidSecretToken;
        try
        {
            hasValidSecretToken = HasValidSecretToken();
        }
        catch (BotNotConfiguredException)
        {
            // Without a token there is no secret to check, and the bot is gated like a disabled one.
            RecordOutcome(metrics, UpdateOutcome.Rejected);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!hasValidSecretToken)
        {
            // Never log the received or expected secret.
            _logger.LogWarning(
                "{BotName} rejected a webhook request with a missing or invalid secret token.",
                _updateService.Name);

            RecordOutcome(metrics, UpdateOutcome.Rejected);
            return Unauthorized();
        }

        if (!_updateService.CanProcessUpdates)
        {
            RecordOutcome(metrics, UpdateOutcome.Rejected);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!_deliveryGuard.TryAcceptUpdate(_updateService.Name, update.Id))
        {
            _logger.LogInformation(
                "{BotName} skipped update {UpdateId} because it was already accepted.",
                _updateService.Name,
                update.Id);

            RecordOutcome(metrics, UpdateOutcome.Duplicate);
            return Ok();
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            try
            {
                await _updateService.ProcessUpdate(update);
            }
            finally
            {
                RecordProcessed(metrics, update, Stopwatch.GetElapsedTime(startedAt));
            }
        }
        catch (Exception ex) when (TransientErrors.IsTransient(ex))
        {
            RecordOutcome(metrics, UpdateOutcome.Failed);

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

                RecordOutcome(metrics, UpdateOutcome.Retried);
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

            RecordOutcome(metrics, UpdateOutcome.Dropped);
            await TrySendErrorAlert(
                update,
                $"Dropped update {update.Id} of type {update.Type} after {attempts} failed attempts.");

            return Ok();
        }
        catch (Exception ex)
        {
            RecordOutcome(metrics, UpdateOutcome.Failed);

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

    /// <summary>
    /// Records one processed delivery of <paramref name="update"/> in <paramref name="metrics"/>, logging instead of
    /// throwing when that fails, so recording never changes the response or hides a processing error.
    /// </summary>
    private void RecordProcessed(UpdateMetrics metrics, Update update, TimeSpan duration)
    {
        try
        {
            metrics.RecordProcessed(_updateService.Name, _updateService.Describe(update), duration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{BotName} could not record the metrics of update {UpdateId}.",
                _updateService.Name,
                update.Id);
        }
    }

    /// <summary>
    /// Records one delivery that ended with <paramref name="outcome"/> in <paramref name="metrics"/>, logging instead
    /// of throwing when that fails, so recording never changes the response or hides a processing error.
    /// </summary>
    private void RecordOutcome(UpdateMetrics metrics, UpdateOutcome outcome)
    {
        try
        {
            metrics.RecordOutcome(_updateService.Name, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{BotName} could not record the {Outcome} outcome of a webhook request.",
                _updateService.Name,
                outcome);
        }
    }

    private async Task TrySendErrorAlert(Update update, string text)
    {
        if (!_deliveryGuard.TryAcquireAlertSlot(_updateService.Name))
        {
            return;
        }

        await _notificationService.SendErrorNotification(
            text,
            _updateService.Name,
            update.Message ?? update.EditedMessage ?? update.CallbackQuery?.Message);
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
