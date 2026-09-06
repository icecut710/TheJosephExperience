# Good job, Joseph!

An app that will give you the pat on the back you always deserved. Many features, many laughs and many opportunities.

Run it in the background while you play games. When something good happens,
press your global hotkey (default **F8**) and **Joseph** appears on screen —
dramatically — for a moment, and then vanishes.

It is polished, native Windows desktop software that does an absurdly simple
thing: it makes Joseph appear on your screen when you want him to.

## What it is

- A **WPF** desktop app (.NET 8) for Windows
- A **global hotkey** (default F8) that works over any application, including
  fullscreen games
- A **transparent, click-through, focus-free overlay** that renders a Joseph
  image with configurable animation, scale, text, and placement
- An **image library** backed by **SQLite** with thumbnails, tags, categories,
  favorites, weights, bulk import, and duplicate detection
- A celebration pipeline with randomized/weighted Joseph selection, animation
  engine, optional sound, history/stats, system tray, and start-with-Windows

The humor is the contrast: a whole database, animation engine, config system,
and global hotkey system — all dedicated to showing Joseph when something good
happens.

## IMPORTANT SECURITY NOTE

**This is NOT a game mod or cheat.** It does not:

- inject DLLs or code into any process
- read or write game memory
- hook DirectX / Vulkan / OpenGL
- patch processes or interact with protected game internals
- bypass or hide from anti-cheat
- automatically detect kills by reading game state

It is a standalone Windows desktop overlay that you activate manually with a
global hotkey. Like pressing a button to show a celebration graphic. Nothing
more.

## Requirements

- Windows 10 or Windows 11 (x64)
- .NET 8 Desktop Runtime (for the framework-dependent build) — or nothing at
  all if you use the self-contained single-file build
- A Joseph image or three (the original is bundled automatically)

No internet connection is required. There is no account, no cloud, no telemetry.

## How to build

Prerequisites: .NET 8 SDK.

```
dotnet build GoodJobJoseph/GoodJobJoseph.csproj -c Debug
```

## How to run

```
dotnet run --project GoodJobJoseph
```

Or run the built executable:

```
GoodJobJoseph/bin/Debug/net8.0-windows/GoodJobJoseph.exe
```

On first launch the app:

1. Creates its data directories under `%LOCALAPPDATA%\GoodJobJoseph\`
2. Initializes the SQLite database
3. Imports the bundled **Original Joseph** image (Classic category, favorited)
4. Registers the default **F8** hotkey
5. Opens the main dashboard

Click **TEST JOSEPH** to see it in action immediately.

## How to publish (Release, self-contained, single-file, x64)

```
dotnet publish GoodJobJoseph/GoodJobJoseph.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The publish output will be in `publish\` and the main executable is
**`GoodJobJoseph.exe`**.

## Default F8 hotkey

- Press **F8** from anywhere to deploy Joseph.
- To rebind, open **Settings → Hotkey → Rebind** and press the new combination.
- Supported modifiers: Ctrl, Alt, Shift, Win. Plain F-keys without a modifier
  are allowed; other plain keys require a modifier to avoid conflicts.

## How the image library works

- **Import Joseph** adds a single image; **Bulk Import…** adds many files or a
  whole folder with progress.
- Each imported file is **SHA-256 hashed**; duplicates are skipped automatically.
- Files are **copied** into `%LOCALAPPDATA%\GoodJobJoseph\images\` using a
  collision-safe UUID name, so the original source can be deleted afterward.
- Every image has: a display name, category, tags, enabled/favorite flags, a
  weight (used by weighted-random mode), and a usage counter.
- Random selection only ever picks **enabled** images. Favorites-only mode uses
  favorites only. Avoid-immediate-repeats is on by default.
- Each deployment increments the image's `times_shown` and records a history row.

## Where local data is stored

Everything lives under:

```
%LOCALAPPDATA%\GoodJobJoseph\
    goodjob.db          SQLite database (images, metadata, history)
    settings.json       UI/app preferences
    images\             imported Joseph image files (uuid.ext)
    sounds\             (optional) your sound files
    cache\thumbnails\   generated thumbnails
    logs\               rolling log files (bounded)
```

## Optional: cloud sync with Supabase

The app can pull a **read-only remote library** of Josephs from a Supabase
project, keeping you in sync across machines.

- Requires a Supabase project. Use `supabase/setup_good_job_joseph.sql` to
  create the `celebration_images` table, the Storage bucket, and row-level
  security. Upload your images to the `good-job-joseph-images` bucket.
- Configure the app by placing a `.env` next to the executable (or in
  `%LOCALAPPDATA%\GoodJobJoseph\.env`). See `supabase/.env.example`:

  ```
  SUPABASE_URL=https://your-project.supabase.co
  SUPABASE_ANON_KEY=your-anon-key
  SUPABASE_BUCKET=good-job-joseph-images
  ```

- On startup (and when you hit **Sync**) the app lists enabled remote rows,
  downloads only changed/missing images, verifies each file against its
  SHA-256, and upserts them into the local library. Files are cached under
  `%LOCALAPPDATA%\GoodJobJoseph\cache\images\`.
- The app shows a truthful status: **CONNECTED / SYNCING / OFFLINE / NOT
  CONFIGURED / ERROR**.
- If offline or unconfigured, the app **keeps working** with the bundled
  Original Joseph and any images already in your local library.
- Access is read-only via the **anon key** with RLS; the client never ships or
  uses a service-role secret.

## How to reset local data

Close the app, then delete the folder `%LOCALAPPDATA%\GoodJobJoseph\`.
The next launch recreates everything and re-imports the bundled Original Joseph.
(Your imported images are deleted too — back them up if you want to keep them.)

## Architecture overview

```
GoodJobJoseph/
  App.xaml(.cs)        Bootstrap, service wiring, lifecycle, dispatch
  Models/              CelebrationImage, AppSettings, CelebrationHistory
  Data/                DatabaseService (CRUD), DatabaseMigrations (schema v2)
  Services/
    HotkeyService      Win32 RegisterHotKey global hotkey
    OverlayService     Single overlay lifecycle, monitor resolution
    ImageLibraryService  import, bulk import, hashing, thumbnails, selection
    SupabaseService    read-only remote library sync (PostgREST + Storage)    SettingsService    settings.json persistence
    CelebrationService  the end-to-end trigger pipeline
    AudioService       WAV/MP3 playback (optional)
    TrayService        system tray + context menu
    StartupService     Start-with-Windows registry entry
  Views/
    MainWindow         Dashboard + Library + Settings + About navigation
    OverlayWindow      Transparent, click-through, topmost overlay
  Themes/DarkTheme.xaml  styling
  Utilities/           AppPaths, RollingFileLogger, ImageUtilities, NativeMethods
  Assets/              bundled Original Joseph image + app icon
```

Key design decisions:

- Services are small and focused; business logic is not buried in UI handlers.
- The overlay uses a **single managed window** with extended styles
  (`WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_TOPMOST`)
  so it never steals focus, is click-through, and never appears in the taskbar.
- **SQL** is always parameterized.
- The overlay rendering is decoupled from persistence via `ImageLibraryService`,
  leaving room for future remote/shared libraries without a rewrite.

## Tests

```
dotnet test GoodJobJoseph.Tests
```

Covers database CRUD, migrations, history/stats, duplicate-SHA detection, and
the randomization engine (disabled/favorites/repeat/weighted/specific/single).
All tests use temporary databases and never touch your real data.

## Version

**1.0.0**
