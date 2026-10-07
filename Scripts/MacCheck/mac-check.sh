#!/bin/bash
# Checks the Windows app from a Mac, where WinUI cannot build or run (see README.md here):
#   1. unit tests of Core/ (Tests/Iris.Tests.csproj)
#   2. the whole app's C#, with stubs for what the XAML compiler would generate
#   3. every {x:Bind} path and {StaticResource} key of the XAML against the compiled types
# Needs the .NET 10 SDK on PATH (or DOTNET pointing at the dotnet executable).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_UI_LANGUAGE=en

echo "== 1/3 Pruebas de Core/"
"$DOTNET" test "$REPO/Tests/Iris.Tests.csproj" --nologo -v quiet

echo "== 2/3 C# de la app (sin XAML)"
python3 "$HERE/xamlgen.py"
"$DOTNET" build "$HERE/WinCheck/WinCheck.csproj" -p:Platform=x64 --nologo -v quiet \
  | grep -E "error|warning CS|Build succeeded" | sed "s#$REPO/##" | sort -u

echo "== 3/3 x:Bind y recursos del XAML"
"$DOTNET" build "$HERE/BindCheck/BindCheck.csproj" -o "$HERE/BindCheck/bin/out" --nologo -v quiet | grep -E " error " || true
"$DOTNET" "$HERE/BindCheck/bin/out/BindCheck.dll" "$REPO" \
  "$HERE/WinCheck/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/WinCheck.dll" \
  "$HERE/WinCheck/obj/refs.txt"
