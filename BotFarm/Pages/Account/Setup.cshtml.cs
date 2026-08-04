using BotFarm.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BotFarm.Pages.Account;

/// <summary>
/// Handles the one-time creation of the initial dashboard admin account.
/// </summary>
public class SetupModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public SetupModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Shows setup only while no admin users exist yet.
    /// </summary>
    public IActionResult OnGet()
    {
        // Setup is only for the very first run; once an admin exists, send visitors to the login page.
        if (_userManager.Users.Any())
        {
            return RedirectToPage("/Account/Login");
        }

        return Page();
    }

    /// <summary>
    /// Creates the initial admin account and signs it in.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (_userManager.Users.Any())
        {
            return RedirectToPage("/Account/Login");
        }

        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            return Page();
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return Page();
        }

        var user = new ApplicationUser(UserName);
        var result = await _userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: true);

        return LocalRedirect("~/");
    }
}
