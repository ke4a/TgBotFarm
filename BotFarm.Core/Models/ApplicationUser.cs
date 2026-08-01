using AspNetCore.Identity.MongoDbCore.Models;

namespace BotFarm.Core.Models;

/// <summary>
/// Identity user persisted in the "AspNetUsers" Mongo collection. Backs the admin dashboard login.
/// </summary>
public class ApplicationUser : MongoIdentityUser
{
    public ApplicationUser() : base()
    {
    }

    public ApplicationUser(string userName) : base(userName)
    {
    }
}
