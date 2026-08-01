using BotFarm.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BotFarm.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // No admin account yet -> send visitors through the one-time setup flow instead.
        if (!_userManager.Users.Any())
        {
            return RedirectToPage("/Account/Setup");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(UserName, Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) ? "~/" : ReturnUrl);
        }

        ErrorMessage = result.IsLockedOut
            ? "Account locked due to too many failed attempts. Try again later."
            : "Invalid username or password.";

        return Page();
    }
}
