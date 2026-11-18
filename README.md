# SocialTap Xbox Activity (Windows)

A small taskbar app for Windows that shows the game you are playing on your Xbox-installed PC games as a
SocialTap-style activity card, and reports your idle status to SocialTap.

- **Playing:** game cover art, the game title, "Playing through Xbox", and how long you've been playing.
- **Not playing:** the Xbox logo with "Idle on Xbox".

## How game detection works

Detection uses only local data on this PC. It does not call Xbox Live or any third-party Xbox API.

- It looks for running processes that have a visible window and live in an Xbox app install folder
  (`C:\XboxGames\<Game>\Content\...`, on any drive).
- The title and cover art come from the game's own `appxmanifest.xml` (display name and largest logo).
- Store games under `C:\Program Files\WindowsApps` are **not** detected. That folder is access-restricted.
- The card cannot see console (Xbox Series X|S, One) play. "Idle on Xbox" is shown whenever no game
  is running on this PC, so it does not confirm that you are idle on a console.

## SocialTap

- Sign-in uses SocialTap's OAuth authorization code flow with PKCE. It opens SocialTap in your browser and
  receives the result on `http://127.0.0.1:53682/callback`. The app never sees your password.
- The access token is stored encrypted with Windows DPAPI for your user account only.
- Your idle time (time since last keyboard or mouse input) is sent to SocialTap every 60 seconds with the
  `presence.write` scope.
- Sign out revokes the authorization on SocialTap.

The client ID is public and is hardcoded in [src/Config.cs](src/Config.cs). It has no secret.

## Build

Requires the .NET 8 SDK on Windows.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

The result is `dist\SocialTapXboxActivity.exe`, a single self-contained file.

## Diagnostics

`SocialTapXboxActivity.exe --detect-to <file>` writes what the game detector currently sees
(`Title|ProcessName|CoverPath`, or `none`) to `<file>` and exits.
