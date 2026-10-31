using BotFarm.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BotFarm.Pages.Account;

/// <summary>
/// Handles the one-time creation of the initial dashboard admin account.
/// Creation requires the setup code announced in the startup log (or configured via <c>Setup:Code</c>).
/// </summary>
public class SetupModel : PageModel
{
    private const string InvalidSetupCodeMessage = "Invalid setup code.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly SetupCodeService _setupCodeService;
    private readonly ILogger<SetupModel> _logger;

    public SetupModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        SetupCodeService setupCodeService,
        ILogger<SetupModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _setupCodeService = setupCodeService;
        _logger = logger;
    }

    [BindProperty]
    public string SetupCode { get; set; } = string.Empty;

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
    /// Verifies the setup code, creates the initial admin account and signs it in.
    /// </summary>
    public async Task<IActionResult> OnPost()
    {
        if (_userManager.Users.Any())
        {
            return RedirectToPage("/Account/Login");
        }

        // Check the code before anything else so an unauthorized visitor learns nothing about the other fields.
        if (!_setupCodeService.Verify(SetupCode))
        {
            return InvalidSetupCode();
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

        var result = await _setupCodeService.CompleteSetupAsync(_userManager, SetupCode, UserName, Password, HttpContext?.RequestAborted ?? default);
        switch (result.Status)
        {
            case SetupCompletionStatus.AlreadyCompleted:
                return RedirectToPage("/Account/Login");
            case SetupCompletionStatus.InvalidCode:
                return InvalidSetupCode();
            case SetupCompletionStatus.Failed:
                ErrorMessage = string.Join(" ", result.Errors!.Select(e => e.Description));
                return Page();
        }

        await _signInManager.SignInAsync(result.User!, isPersistent: true);

        return LocalRedirect("~/");
    }

    private PageResult InvalidSetupCode()
    {
        // Never log the submitted value: it may be a near-miss of the real code.
        _logger.LogWarning("Rejected initial admin setup attempt with an invalid setup code.");
        ErrorMessage = InvalidSetupCodeMessage;
        return Page();
    }
}
