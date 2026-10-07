# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Implementation plan

**Start with `docs/plans/README.md`.** It holds the phased plan this repo is being built from (API connection,
offline sync, real TV output and playback), the shared platform decisions and the API contract
(`docs/api-contract.md`). Where this file and the plan disagree, the plan wins.

## Project

Iris is a WinUI 3 desktop app (Windows App SDK 2.5, .NET 8, `net8.0-windows10.0.19041.0`, min OS 10.0.17763) packaged as a single-project MSIX. It is the Windows 11 port of an iPad (SwiftUI) mockup; the authoritative spec is `docs/IRIS_SPEC.md`. All visible text is Spanish; code is English. Unit tests cover the pure logic only (`Tests/`).

## Purpose

Church projection console: a control PC drives a TV/projector (second monitor) that shows only content — song lyrics, Bible verses, backgrounds, images, videos — while music plays in the room. Users sign in / create an account first.

## Rules

- **MVVM with CommunityToolkit.Mvvm** (`ObservableObject`, `[ObservableProperty]` partial properties, `[RelayCommand]`). Views hold no logic beyond wiring (focus, flyout hide, title-bar regions, keyboard accelerators); state and commands live in ViewModels.
- **Dark-only "vitral" design** (spec §3–4): `App.xaml` forces `RequestedTheme="Dark"`. Every color, size, radius and font comes from `DesignSystem/Themes/Tokens.xaml`; no literal values in feature views. Reuse the `Iris*` controls and styles (`Styles.xaml`).
- **No NavigationView**: the app is two screens (access ↔ live console) switched by `SessionStore`; secondary flows are in-window sheets (`IrisModal`) and flyouts.
- **All UI is hand-written XAML** (or code-built visuals inside `DesignSystem/Controls` and `ProjectionCanvas`) — no designer output.
- **After every change, run `dotnet build Iris.csproj -p:Platform=x64` and fix all errors before finishing.**
- Data goes through `Core/Services` interfaces. `Mock*` implementations (wired in `Shell/AppDependencies.Mock()`) stay for design work and tests; `Live*` implementations talk to the API through `Core/Networking`, a local SQLite copy and an outbox (see `docs/plans`). In development the API is the in-app fake (`DataMode.Fake`).
- Placeholder texts are public-domain hymns (19th-century translations) and RVR1909, or invented — never copyrighted lyrics.
- Features covered by `docs/plans` are approved; anything not covered by the spec or the plans needs approval first.

## Architecture

```
Shell/           AppDependencies (composition root), SessionStore, ProjectionDisplayService
MainWindow       Hosts AuthPage or LiveConsolePage; extended title bar + passthrough regions (ConfigureTitleBar)
ProjectionWindow Borderless full-screen TV window on the first non-primary monitor
Core/Models      Records: session, ServicePlan/ServiceItem/Slide, ProjectionFrame/ProjectionContent, Bible, Library
Core/Services    Interfaces + Mocks/ (SampleData holds all demo data, incl. the 66-book Bible index)
DesignSystem/    Themes/Tokens.xaml, Themes/Styles.xaml, IrisTheme/KindStyle/Glyphs, Motion, Controls/Iris*
Shared/Projection/ProjectionCanvas — the ONE renderer for thumbnails, EN VIVO and the TV (1600×900 stage in a Viewbox)
Features/        Auth, Home, LiveConsole, Bible, AddToService, Modules (+ Proyección), Services, People, Times, Connection
Scripts/MacCheck Checks from a Mac (Core tests, the app's C#, x:Bind paths); excluded from Iris.csproj
```

`LiveConsoleViewModel` owns the presentation rules (spec §7): the service list only *opens* items; clicking a card/media stage *presents*. Every state change goes through `Changed()`, which refreshes derived properties and calls `IDisplayOutputService.Present(LiveFrame)`.

Desktop layout (docs/plans/11): the console has a toolbar under the top bar and, from `IrisConsoleWideBreakpoint`, three columns (SERVICIO | workspace | EN VIVO + SIGUIENTE + mini player); narrower it falls back to the iPad's two. How the lyrics look (api-contract §6) lives in `Shared/Projection/ProjectionTypography`: every `ProjectionCanvas` follows it unless it gets its own `Typography` (the Proyección preview). Music and videos download only once added to a service (`IMediaCache.RequestAsync`); the console resolves each item's file by `ServiceItem.MediaId` when it plays or projects.

## Build & run

Tests: `dotnet test Tests/Iris.Tests.csproj` (xunit, plain `net8.0`). It links the UI-free sources (`Core/Formatting`, `Core/Models/Church.cs`, `Core/Timing`) instead of referencing the WinUI project; `Iris.csproj` excludes `Tests\**`. Add new pure-logic tests there.

A platform must always be specified; `AnyCPU` is not a valid configuration (platforms: `x86`, `x64`, `ARM64`).

```powershell
dotnet build Iris.csproj -p:Platform=x64             # Debug build
dotnet build Iris.csproj -c Release -p:Platform=x64  # Release (ReadyToRun + trimming enabled on publish)
dotnet publish Iris.csproj -c Release -p:Platform=x64  # uses Properties/PublishProfiles/win-x64.pubxml
```

Run from Visual Studio with **Iris (Package)**. The `bin/.../Iris.exe` of a normal build is the packaged app and crashes if launched directly (Windows App Runtime not registered). For a quick unpackaged run, build a separate copy:
`dotnet build Iris.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutDir=<dir>\ -p:IntermediateOutputPath=<dir>\obj\`

## Things to know

- WinUI clamps `CornerRadius` per axis: capsules need an exact half-height radius (`IrisRadiusCapsule*` tokens), never 999.
- `ColumnDefinition.Width`/`RowDefinition.Height` need `GridLength` tokens; an `x:Double` resource fails at runtime (XamlParseException), not at build.
- `PasswordBox` has no `PlaceholderForeground`; `IrisTextField` restyles TextBox/PasswordBox via lightweight-styling keys in its resources.
- Glyph constants in `DesignSystem/IrisTheme.cs` are stored as literal private-use characters; edit them with the Edit tool using `\uXXXX` escapes.
- `Package.appxmanifest` declares `systemai:systemAIModels` (on-device Windows AI) in addition to `runFullTrust`; those APIs need package identity and Copilot+ hardware.
- `Nullable` is enabled project-wide. New manifest image assets must also be added as `<Content>` items in `Iris.csproj`.
