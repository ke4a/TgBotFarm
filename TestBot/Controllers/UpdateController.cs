using BotFarm.Core.Abstractions;
using BotFarm.Core.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestBot.Controllers;

/// <summary>
/// Receives Telegram webhook updates for the reference TestBot implementation.
/// </summary>
[ApiController]
[Route("api/TestBot/[controller]")]
public class UpdateController : UpdateControllerBase
{
    /// <summary>
    /// Creates the controller bound to TestBot's keyed <see cref="IUpdateService"/>.
    /// </summary>
    public UpdateController(
        [FromKeyedServices(Constants.Name)] IUpdateService updateService,
        IWebhookSecretProvider webhookSecretProvider,
        IUpdateDeliveryGuard deliveryGuard,
        INotificationService notificationService,
        ILogger<UpdateController> logger)
        : base(updateService, webhookSecretProvider, deliveryGuard, notificationService, logger)
    {
    }
}