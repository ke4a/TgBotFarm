using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Edits a bot's settings. The token and webhook secret inputs are write-only: they start empty, an empty input keeps
/// the stored value, and they are cleared after a save. A token of a different Telegram bot is saved only after the
/// administrator confirms the account change. A stored secret that could not be decrypted is required: the dialog
/// does not send a change without a new token, or without a new or derived webhook secret, when those are unreadable.
/// The emoji is chosen from an emoji picker or cleared, never typed. Closes with the saved
/// <see cref="BotSettingsView"/> as its result.
/// </summary>
public partial class BotSettingsDialog : DashboardComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/BotFarm.Shared/Components/BotSettingsDialog.razor.js";

    private static readonly DialogOptions DialogOptions = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
    };

    /// <summary>
    /// Keeps browsers and password managers from suggesting, correcting or saving the token and webhook secret, which
    /// are plain text inputs, so no browser offers to save them as passwords.
    /// </summary>
    protected static Dictionary<string, object?> SecretInputAttributes => new()
    {
        ["autocomplete"] = "off",
        ["spellcheck"] = "false",
        ["autocapitalize"] = "off",
        ["autocorrect"] = "off",
        ["data-1p-ignore"] = "true",
        ["data-lpignore"] = "true",
        ["data-bwignore"] = "true"
    };

    private string? _token;
    private long? _adminChatId;
    private string? _webhookSecret;
    private bool _useDerivedWebhookSecret;
    private string? _emoji;
    private bool _emojiPickerOpen;
    private bool _emojiPickerMounted;
    private ElementReference _emojiPickerHost;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BotSettingsDialog>? _selfReference;
    private bool _saving;
    private BotSettingsSaveResult.NeedsAccountChangeConfirmation? _accountChange;
    private List<string> _problems = [];

    [CascadingParameter]
    protected IMudDialogInstance MudDialog { get; set; } = default!;

    /// <summary>
    /// The bot's settings when the dialog opens; the non-secret values prefill the inputs.
    /// </summary>
    [Parameter, EditorRequired]
    public BotSettingsView View { get; set; } = new();

    [Inject] protected IBotSettingsService SettingsService { get; set; } = default!;

    /// <summary>
    /// Opens the dialog for the bot's current settings.
    /// </summary>
    /// <param name="dialogService">Shows the dialog.</param>
    /// <param name="settingsService">Supplies the bot's current settings.</param>
    /// <param name="localizer">A localizer of the BotFarm.Shared texts, for the dialog title.</param>
    /// <param name="botName">The bot whose settings are edited.</param>
    /// <returns><see langword="true"/> when the settings were saved.</returns>
    public static async Task<bool> ShowAsync(
        IDialogService dialogService,
        IBotSettingsService settingsService,
        IStringLocalizer localizer,
        string botName)
    {
        var parameters = new DialogParameters<BotSettingsDialog>
        {
            { x => x.BotName, botName },
            { x => x.View, settingsService.GetView(botName) },
        };

        var dialog = await dialogService.ShowAsync<BotSettingsDialog>(
            localizer["BotSettingsDialog.Title", botName],
            parameters,
            DialogOptions);
        var result = await dialog.Result;
        return result is { Canceled: false };
    }
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] protected ILogger<BotSettingsDialog> Logger { get; set; } = default!;
    [Inject] protected IStringLocalizer<BotSettingsDialog> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    /// <summary>
    /// Whether the stored token could not be decrypted, so a new one must be entered.
    /// </summary>
    protected bool TokenRequired => View.UnreadableSettings.Contains(nameof(BotConfig.Token));

    /// <summary>
    /// Whether the stored webhook secret could not be decrypted, so a new one or the derived one must be chosen.
    /// </summary>
    protected bool WebhookSecretRequired => View.UnreadableSettings.Contains(nameof(BotConfig.WebhookSecret));

    /// <summary>
    /// Help text under the token input: asks for the token again when the stored one could not be decrypted, says
    /// an empty input keeps a stored token, and otherwise describes the token.
    /// </summary>
    protected string TokenHelp => TokenRequired
        ? L["BotSettingsDialog.Token.RequiredHelp"]
        : View.HasToken ? L["BotSettingsDialog.Token.KeepHelp"] : L["BotSettingsDialog.Token.Help"];

    /// <summary>
    /// Help text under the webhook secret input: asks for a new or the derived secret when the stored one could not
    /// be decrypted, says an empty input keeps a stored custom secret, and otherwise says an empty input uses the
    /// derived secret.
    /// </summary>
    protected string WebhookSecretHelp => WebhookSecretRequired
        ? L["BotSettingsDialog.WebhookSecret.RequiredHelp"]
        : View.HasCustomWebhookSecret ? L["BotSettingsDialog.WebhookSecret.KeepHelp"] : L["BotSettingsDialog.WebhookSecret.Help"];

    protected override void OnInitialized()
    {
        _token = null;
        _webhookSecret = null;
        _useDerivedWebhookSecret = false;
        _adminChatId = View.AdminChatId;
        _emoji = View.Emoji;
    }

    private void SetUseDerivedWebhookSecret(bool useDerived)
    {
        _useDerivedWebhookSecret = useDerived;
        if (useDerived)
        {
            _webhookSecret = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_emojiPickerOpen || _emojiPickerMounted)
        {
            return;
        }

        _emojiPickerMounted = true;
        try
        {
            _module ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _selfReference ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("mount", _emojiPickerHost, _selfReference);
        }
        catch (JSException exception)
        {
            Logger.LogError(exception, "The emoji picker of {BotName} could not be loaded.", BotName);
            CloseEmojiPicker();
            Snackbar.Add(L["BotSettingsDialog.Emoji.PickerFailed"], Severity.Error);
            StateHasChanged();
        }
    }

    /// <summary>
    /// Takes the emoji the administrator picked and closes the picker.
    /// </summary>
    [JSInvokable]
    public void PickEmoji(string emoji)
    {
        _emoji = emoji;
        CloseEmojiPicker();
        StateHasChanged();
    }

    protected void ToggleEmojiPicker()
    {
        if (_emojiPickerOpen)
        {
            CloseEmojiPicker();
            return;
        }

        _emojiPickerOpen = true;
    }

    protected void ClearEmoji()
    {
        _emoji = null;
        CloseEmojiPicker();
    }

    private void CloseEmojiPicker()
    {
        _emojiPickerOpen = false;
        _emojiPickerMounted = false;
    }

    protected Task Save() => SaveAsync(confirmAccountChange: false);

    private Task ConfirmAccountChange() => SaveAsync(confirmAccountChange: true);

    private void CancelAccountChange() => _accountChange = null;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync(bool confirmAccountChange)
    {
        if (_saving)
        {
            return;
        }

        _problems = GetMissingRequiredValues();
        if (_problems.Count > 0)
        {
            return;
        }

        _saving = true;
        try
        {
            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var change = new BotSettingsChange
            {
                Token = _token,
                AdminChatId = _adminChatId,
                WebhookSecret = _useDerivedWebhookSecret ? null : _webhookSecret,
                UseDerivedWebhookSecret = _useDerivedWebhookSecret,
                Emoji = _emoji,
                ConfirmAccountChange = confirmAccountChange,
                ChangedBy = authenticationState.User.Identity?.Name ?? string.Empty
            };

            switch (await SettingsService.SaveAsync(BotName, change))
            {
                case BotSettingsSaveResult.Saved saved:
                    ClearSecrets();
                    ShowSaved(saved);
                    MudDialog.Close(DialogResult.Ok(saved.View));
                    break;
                case BotSettingsSaveResult.NeedsAccountChangeConfirmation accountChange:
                    _accountChange = accountChange;
                    break;
                case BotSettingsSaveResult.Rejected rejected:
                    _accountChange = null;
                    _problems = rejected.Rejections
                        .Select(rejection => L[$"BotSettingsDialog.Rejection.{rejection}"].Value)
                        .ToList();
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Saving the settings of {BotName} failed.", BotName);
            _accountChange = null;
            _problems = [L["BotSettingsDialog.Error"]];
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// One message per unreadable secret the inputs do not replace.
    /// </summary>
    private List<string> GetMissingRequiredValues()
    {
        var problems = new List<string>();
        if (TokenRequired && string.IsNullOrEmpty(_token))
        {
            problems.Add(L["BotSettingsDialog.Required.Token"]);
        }

        if (WebhookSecretRequired && !_useDerivedWebhookSecret && string.IsNullOrEmpty(_webhookSecret))
        {
            problems.Add(L["BotSettingsDialog.Required.WebhookSecret"]);
        }

        return problems;
    }

    private void ShowSaved(BotSettingsSaveResult.Saved saved)
    {
        if (saved.Status.Status == BotRuntimeState.Error)
        {
            Snackbar.Add(L["BotSettingsDialog.Saved.ApplyFailed", BotName], Severity.Warning);
        }
        else if (saved.Status.Status == BotRuntimeState.Unknown)
        {
            Snackbar.Add(L["BotSettingsDialog.Saved.Unconfirmed", BotName], Severity.Warning);
        }
        else if (!string.IsNullOrEmpty(saved.VerifiedTelegramUsername))
        {
            Snackbar.Add(L["BotSettingsDialog.Saved.Verified", BotName, saved.VerifiedTelegramUsername], Severity.Success);
        }
        else
        {
            Snackbar.Add(L["BotSettingsDialog.Saved", BotName], Severity.Success);
        }
    }

    private void ClearSecrets()
    {
        _token = null;
        _webhookSecret = null;
        _useDerivedWebhookSecret = false;
    }

    public async ValueTask DisposeAsync()
    {
        _selfReference?.Dispose();
        if (_module == null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and the module with it.
        }
    }
}
