using BotFarm.Core.Abstractions;
using BotFarm.Core.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotTemplate.Controllers;

/// <summary>
/// Receives Telegram webhook updates for BotTemplate.
/// </summary>
[ApiController]
[Route("api/BotTemplate/[controller]")]
public class UpdateController : UpdateControllerBase
{
    /// <summary>
    /// Creates the controller bound to BotTemplate's keyed <see cref="IUpdateService"/>.
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
