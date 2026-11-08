using BotFarm.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BotFarm.Pages.Account;

/// <summary>
/// Signs the current dashboard user out and returns them to the login page.
/// </summary>
/// <remarks>
/// Sign-out runs only on an antiforgery-validated POST so that links or embedded resources
/// pointing at this page cannot end the session.
/// </remarks>
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    /// <summary>
    /// Sends visitors to the dashboard home without touching the session; anonymous visitors
    /// are then challenged to the login page.
    /// </summary>
    public IActionResult OnGet()
    {
        return LocalRedirect("~/");
    }

    /// <summary>
    /// Ends the current authenticated session.
    /// </summary>
    public async Task<IActionResult> OnPost()
    {
        await _signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
