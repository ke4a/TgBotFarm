#!/usr/bin/env bash
# Scaffolds a new bot from templates/BotFarm.Bot: creates the bot, its unit test project and its integration tests
# in tests/BotFarm.IntegrationTests/Bots/<BotName>, adds both projects to the solution and references the bot from
# the host. The bot's settings are entered in the admin dashboard.
#
# Usage: scripts/new-bot.sh <BotName>
set -euo pipefail

usage() {
    echo "Usage: $0 <BotName>" >&2
    echo "  BotName  PascalCase C# identifier, e.g. WeatherBot" >&2
    exit 1
}

[[ $# -eq 1 ]] || usage

name="$1"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ! "$name" =~ ^[A-Z][A-Za-z0-9]*$ ]]; then
    echo "Error: bot name must be PascalCase letters and digits, e.g. WeatherBot." >&2
    exit 1
fi

command -v dotnet >/dev/null || { echo "Error: 'dotnet' is required but not installed." >&2; exit 1; }

for path in "$root/$name" "$root/tests/$name.UnitTests" "$root/tests/BotFarm.IntegrationTests/Bots/$name"; do
    [[ ! -e "$path" ]] || { echo "Error: $path already exists." >&2; exit 1; }
done

cd "$root"

echo "==> Installing the bot template"
dotnet new install "templates/BotFarm.Bot" --force >/dev/null

echo "==> Creating $name, $name.UnitTests and the $name integration tests"
dotnet new botfarm-bot --name "$name" --output . >/dev/null

echo "==> Adding the projects to BotFarm.sln"
dotnet sln BotFarm.sln add "$name/$name.csproj"
dotnet sln BotFarm.sln add --solution-folder tests "tests/$name.UnitTests/$name.UnitTests.csproj"

echo "==> Referencing $name from BotFarm"
dotnet add BotFarm/BotFarm.csproj reference "$name/$name.csproj"

echo "==> Building"
dotnet build BotFarm.sln --nologo -v quiet -clp:ErrorsOnly

cat <<DONE

$name is ready. Next steps:
  1. Start BotFarm, open $name's settings in the admin dashboard, enter its token, admin chat and
     emoji, save, and enable the bot with its switch.
  2. Replace $name/wwwroot/img/$(echo "$name" | tr '[:upper:]' '[:lower:]')_logo.svg with the bot's logo.
  3. Add commands to Constants.Commands, implement ICommandHandler/ICallbackHandler classes and
     register them in Extensions/ServiceCollectionExtensions.cs. See TestBot for examples.
DONE
