using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace BotFarm.Authentication;

/// <summary>
/// Outcome of an attempt to create the initial admin account.
/// </summary>
public enum SetupCompletionStatus
{
    Succeeded,
    InvalidCode,
    AlreadyCompleted,
    Failed
}

/// <summary>
/// Result of <see cref="SetupCodeService.CompleteSetupAsync"/>.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="User">The created user when <paramref name="Status"/> is <see cref="SetupCompletionStatus.Succeeded"/>.</param>
/// <param name="Errors">Identity errors when <paramref name="Status"/> is <see cref="SetupCompletionStatus.Failed"/>.</param>
public sealed record SetupCompletionResult(
    SetupCompletionStatus Status,
    ApplicationUser? User = null,
    IReadOnlyList<IdentityError>? Errors = null);

/// <summary>
/// Holds the one-time code that authorizes creation of the first dashboard admin account.
/// The code lives in memory only: a restart produces a new one, and it is erased once setup completes.
/// </summary>
public class SetupCodeService
{
    /// <summary>
    /// Configuration key for an operator-provided setup code (env var <c>Setup__Code</c>).
    /// </summary>
    public const string ConfigurationKey = "Setup:Code";

    /// <summary>
    /// Minimum length accepted for a configured setup code.
    /// </summary>
    public const int MinimumCodeLength = 16;

    private const int GeneratedCodeBytes = 16;

    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private volatile string? _code;

    /// <summary>
    /// Creates the holder, taking the code from <see cref="ConfigurationKey"/> or generating a random one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured code is shorter than <see cref="MinimumCodeLength"/>.</exception>
    public SetupCodeService(IConfiguration configuration)
    {
        var configuredCode = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configuredCode))
        {
            // Never include the value itself in the message - it is a secret.
            if (configuredCode.Length < MinimumCodeLength)
            {
                throw new InvalidOperationException(
                    $"Configuration setting '{ConfigurationKey}' must be at least {MinimumCodeLength} characters long.");
            }

            _code = configuredCode;
            IsConfigured = true;
            return;
        }

        _code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(GeneratedCodeBytes));
    }

    /// <summary>
    /// True when the code came from configuration rather than being generated at startup.
    /// </summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// True while setup has not been completed by this process and the code can still be used.
    /// </summary>
    public bool IsPending => _code is not null;

    /// <summary>
    /// The generated code, for announcing it in the startup log. Null when the code is configured
    /// (its value must never be logged) or setup has already been completed.
    /// </summary>
    public string? GeneratedCode => IsConfigured ? null : _code;

    /// <summary>
    /// Checks <paramref name="candidate"/> against the current code in constant time.
    /// </summary>
    public bool Verify(string? candidate)
    {
        var code = _code;
        if (code is null || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(code));
    }

    /// <summary>
    /// Creates the initial admin account if <paramref name="code"/> is valid and no users exist yet.
    /// Attempts are serialized so concurrent requests cannot both create an admin; on success the code is erased.
    /// </summary>
    public async Task<SetupCompletionResult> CompleteSetupAsync(
        UserManager<ApplicationUser> userManager,
        string? code,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        await _setupLock.WaitAsync(cancellationToken);
        try
        {
            // Both checks must happen inside the lock: another request may have finished setup meanwhile.
            if (await userManager.HasAnyUserAsync(cancellationToken))
            {
                return new SetupCompletionResult(SetupCompletionStatus.AlreadyCompleted);
            }

            if (!Verify(code))
            {
                return new SetupCompletionResult(SetupCompletionStatus.InvalidCode);
            }

            var user = new ApplicationUser(userName);
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return new SetupCompletionResult(SetupCompletionStatus.Failed, Errors: result.Errors.ToList());
            }

            _code = null;
            return new SetupCompletionResult(SetupCompletionStatus.Succeeded, user);
        }
        finally
        {
            _setupLock.Release();
        }
    }
}
