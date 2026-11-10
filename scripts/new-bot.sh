#!/usr/bin/env bash
# Scaffolds a new bot from templates/BotFarm.Bot: creates the bot and its unit test project,
# adds both to the solution, references the bot from the host and adds its configuration section.
#
# Usage: scripts/new-bot.sh <BotName> [emoji]
set -euo pipefail

usage() {
    echo "Usage: $0 <BotName> [emoji]" >&2
    echo "  BotName  PascalCase C# identifier, e.g. WeatherBot" >&2
    echo "  emoji    Emoji shown for the bot in the dashboard (default: 🤖)" >&2
    exit 1
}

[[ $# -ge 1 && $# -le 2 ]] || usage

name="$1"
emoji="${2:-🤖}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ! "$name" =~ ^[A-Z][A-Za-z0-9]*$ ]]; then
    echo "Error: bot name must be PascalCase letters and digits, e.g. WeatherBot." >&2
    exit 1
fi

for tool in dotnet jq; do
    command -v "$tool" >/dev/null || { echo "Error: '$tool' is required but not installed." >&2; exit 1; }
done

for path in "$root/$name" "$root/tests/$name.UnitTests"; do
    [[ ! -e "$path" ]] || { echo "Error: $path already exists." >&2; exit 1; }
done

for settings in "$root"/BotFarm/appsettings*.json; do
    if jq -e --arg name "$name" '.Bots[$name] != null' "$settings" >/dev/null; then
        echo "Error: $(basename "$settings") already has a Bots:$name section." >&2
        exit 1
    fi
done

cd "$root"

echo "==> Installing the bot template"
dotnet new install "templates/BotFarm.Bot" --force >/dev/null

echo "==> Creating $name and $name.UnitTests"
dotnet new botfarm-bot --name "$name" --output . >/dev/null

echo "==> Adding the projects to BotFarm.sln"
dotnet sln BotFarm.sln add "$name/$name.csproj"
dotnet sln BotFarm.sln add --solution-folder tests "tests/$name.UnitTests/$name.UnitTests.csproj"

echo "==> Referencing $name from BotFarm"
dotnet add BotFarm/BotFarm.csproj reference "$name/$name.csproj"

echo "==> Adding the Bots:$name configuration section"
for settings in "$root"/BotFarm/appsettings*.json; do
    tmp="$(mktemp)"
    jq --arg name "$name" --arg emoji "$emoji" \
        '.Bots[$name] = { BotConfig: { Enabled: false, Emoji: $emoji, Token: "***", AdminChatId: 0, WebhookSecret: "" } }' \
        "$settings" > "$tmp"
    mv "$tmp" "$settings"
    echo "    $(basename "$settings")"
done

echo "==> Building"
dotnet build BotFarm.sln --nologo -v quiet -clp:ErrorsOnly

cat <<DONE

$name is ready. Next steps:
  1. Set Bots:$name:BotConfig Token and AdminChatId (appsettings, user secrets or
     Bots__${name}__BotConfig__Token) and set Enabled to true.
  2. Replace $name/wwwroot/img/$(echo "$name" | tr '[:upper:]' '[:lower:]')_logo.svg with the bot's logo.
  3. Add commands to Constants.Commands, implement ICommandHandler/ICallbackHandler classes and
     register them in Extensions/ServiceCollectionExtensions.cs. See TestBot for examples.
DONE
