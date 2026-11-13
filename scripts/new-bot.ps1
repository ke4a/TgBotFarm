#Requires -Version 7
<#
.SYNOPSIS
Scaffolds a new bot from templates/BotFarm.Bot.

.DESCRIPTION
Creates the bot, its unit test project and its integration tests in
tests/BotFarm.IntegrationTests/Bots/<Name>, adds both projects to the solution, references the bot from
the host and adds its configuration section to the BotFarm/appsettings*.json files.

.PARAMETER Name
PascalCase C# identifier of the bot, e.g. WeatherBot.

.PARAMETER Emoji
Emoji shown for the bot in the dashboard.

.EXAMPLE
./scripts/new-bot.ps1 WeatherBot

.EXAMPLE
./scripts/new-bot.ps1 WeatherBot 🌦
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [string] $Name,

    [Parameter(Position = 1)]
    [string] $Emoji = '🤖'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Fail([string] $message) {
    $Host.UI.WriteErrorLine("Error: $message")
    exit 1
}

if ($Name -cnotmatch '^[A-Z][A-Za-z0-9]*$') {
    Fail 'Bot name must be PascalCase letters and digits, e.g. WeatherBot.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "'dotnet' is required but not installed."
}

$root = Split-Path -Parent $PSScriptRoot
$settingsFiles = Get-ChildItem -Path (Join-Path $root 'BotFarm') -Filter 'appsettings*.json'

foreach ($path in @(
        (Join-Path $root $Name),
        (Join-Path $root 'tests' "$Name.UnitTests"),
        (Join-Path $root 'tests' 'BotFarm.IntegrationTests' 'Bots' $Name))) {
    if (Test-Path $path) {
        Fail "$path already exists."
    }
}

foreach ($file in $settingsFiles) {
    $settings = Get-Content $file.FullName -Raw | ConvertFrom-Json
    if ($settings.Bots -and $settings.Bots.PSObject.Properties[$Name]) {
        Fail "$($file.Name) already has a Bots:$Name section."
    }
}

Push-Location $root
try {
    Write-Host '==> Installing the bot template'
    dotnet new install 'templates/BotFarm.Bot' --force | Out-Null

    Write-Host "==> Creating $Name, $Name.UnitTests and the $Name integration tests"
    dotnet new botfarm-bot --name $Name --output . | Out-Null

    Write-Host '==> Adding the projects to BotFarm.sln'
    dotnet sln BotFarm.sln add "$Name/$Name.csproj"
    dotnet sln BotFarm.sln add --solution-folder tests "tests/$Name.UnitTests/$Name.UnitTests.csproj"

    Write-Host "==> Referencing $Name from BotFarm"
    dotnet add BotFarm/BotFarm.csproj reference "$Name/$Name.csproj"

    Write-Host "==> Adding the Bots:$Name configuration section"
    foreach ($file in $settingsFiles) {
        $settings = Get-Content $file.FullName -Raw | ConvertFrom-Json
        if (-not $settings.Bots) {
            $settings | Add-Member -NotePropertyName Bots -NotePropertyValue ([pscustomobject]@{})
        }
        $settings.Bots | Add-Member -NotePropertyName $Name -NotePropertyValue ([pscustomobject][ordered]@{
            BotConfig = [pscustomobject][ordered]@{
                Enabled       = $false
                Emoji         = $Emoji
                Token         = '***'
                AdminChatId   = 0
                WebhookSecret = ''
            }
        })
        $settings | ConvertTo-Json -Depth 100 | Set-Content $file.FullName -Encoding utf8NoBOM
        Write-Host "    $($file.Name)"
    }

    Write-Host '==> Building'
    dotnet build BotFarm.sln --nologo -v quiet -clp:ErrorsOnly
}
finally {
    Pop-Location
}

$lowerName = $Name.ToLowerInvariant()
Write-Host @"

$Name is ready. Next steps:
  1. Set Bots:${Name}:BotConfig Token and AdminChatId (appsettings, user secrets or
     Bots__${Name}__BotConfig__Token) and set Enabled to true.
  2. Replace $Name/wwwroot/img/${lowerName}_logo.svg with the bot's logo.
  3. Add commands to Constants.Commands, implement ICommandHandler/ICallbackHandler classes and
     register them in Extensions/ServiceCollectionExtensions.cs. See TestBot for examples.
"@
