using Microsoft.AspNetCore.Identity;
using MongoDB.Driver.Linq;

namespace BotFarm.Authentication;

/// <summary>
/// Queries over the admin accounts in the identity store.
/// </summary>
public static class UserManagerExtensions
{
    /// <summary>
    /// Checks whether any admin account exists, without blocking a thread while MongoDB answers.
    /// </summary>
    public static async Task<bool> HasAnyUserAsync(
        this UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var users = userManager.Users;

        // MongoDB's async LINQ methods only accept MongoDB queries; other sources, such as in-memory lists,
        // are answered synchronously.
        return users.Provider is IMongoQueryProvider
            ? await users.AnyAsync(cancellationToken)
            : users.Any();
    }
}
