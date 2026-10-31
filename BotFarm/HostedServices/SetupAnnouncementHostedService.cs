using BotFarm.Authentication;
using Microsoft.AspNetCore.Identity;

namespace BotFarm.HostedServices;

/// <summary>
/// Announces at startup that the initial admin account still has to be created, and how to authorize it.
/// </summary>
/// <remarks>
/// Runs in the background so an unreachable identity database cannot delay or crash host startup.
/// </remarks>
public class SetupAnnouncementHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SetupCodeService _setupCodeService;
    private readonly ILogger<SetupAnnouncementHostedService> _logger;

    /// <summary>
    /// Creates the announcement service.
    /// </summary>
    public SetupAnnouncementHostedService(
        IServiceScopeFactory scopeFactory,
        SetupCodeService setupCodeService,
        ILogger<SetupAnnouncementHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _setupCodeService = setupCodeService;
        _logger = logger;
    }

    /// <summary>
    /// Logs the setup instructions when no users exist in the identity store.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // UserManager is scoped, so it cannot be injected into this singleton directly.
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (userManager.Users.Any())
            {
                return Task.CompletedTask;
            }

            var generatedCode = _setupCodeService.GeneratedCode;
            if (generatedCode is not null)
            {
                _logger.LogWarning("No admin account exists. Open /Account/Setup and enter setup code: {SetupCode}", generatedCode);
            }
            else if (_setupCodeService.IsPending)
            {
                _logger.LogWarning(
                    "No admin account exists. Open /Account/Setup and enter the setup code configured in {SetupCodeSetting}.",
                    SetupCodeService.ConfigurationKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not check the identity store for an existing admin account. " +
                "If the initial admin has not been created yet, fix the database connection and restart the application to get a setup code.");
        }

        return Task.CompletedTask;
    }
}
