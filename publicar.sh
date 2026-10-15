#!/usr/bin/env bash
# Publishes Dashboard Metas from Linux, like publicar.cmd: one DashboardMetas.exe (win-x86, self-contained) plus
# appsettings.json. Result: publish/DashboardMetas. Needs the .NET 10 SDK and Node (tools/linux copies the app's
# version and icon into the exe, which the SDK only does on Windows).
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ -d tools/linux/node_modules ] || npm ci --prefix tools/linux --silent
dotnet test DashboardMetas.sln -c Release --nologo -v q -p:EnableWindowsTargeting=true
rm -rf publish/DashboardMetas
dotnet publish src/DashboardMetas.App -c Release -o publish/DashboardMetas --nologo -v q \
  -p:EnableWindowsTargeting=true -p:CustomAfterMicrosoftCommonTargets="$PWD/tools/linux/apphost.targets"
echo
echo "Listo: $PWD/publish/DashboardMetas"
