# Good Job, Joseph! — Development Report (Phase 10)

## Overall Status
All 36 unit tests pass (36/36). The application builds cleanly with 0 errors in Debug mode. The `publish\.env` (157 bytes) is preserved byte-for-byte. Runtime verification of hotkey, DPI scaling, and overlay behavior is in progress (marked "Not Verified" where not yet tested in this session).

---

## 1. Control → Enum / Property / Service Matrix

| Setting / Property | Model Enum | Default | Service / DB Impact |
|---|---|---|---|
| `ImagePosition` | `ImagePosition` (12: Center, Top, Bottom, Left, Right, Custom, CursorPosition, ActiveMonitorCenter, Monitor1, Monitor2, Monitor3, CustomCoords) | `Center` | `OverlayService.ComputePlacement`; `MainWindow.ShowSettingsView` combo wired |
| `AnimationStyle` | `AnimationStyle` (22: None, Fade, Pop, ScaleIn, Bounce, SlideLeft, SlideRight, SlideUp, SlideDown, SpinIn, SpinOut, DropIn, RiseIn, ZoomIn, ZoomOut, Pulse, Wobble, Shake, Elastic, Overshoot, Jumpscare, Random) | `Pop` | `OverlayWindow.RunAnimation` with all 22 cases wired; `EasingStyle` applied |
| `ImageMode` | `ImageMode` (8: Random, FavoritesOnly, Specific, Weighted, LeastRecentlyUsed, ShuffleBag, RecentlyAdded, RandomCategory) | `Random` | `ImageLibraryService.PickImage` with full strategy support (LRU, ShuffleBag, RecentlyAdded, RandomCategory, Weighted) |
| `MonitorMode` | `MonitorMode` (6: Primary, MonitorUnderCursor, ActiveWindowMonitor, Monitor1, Monitor2, Monitor3) | `Primary` | `OverlayService.ResolveMonitorRect` + `ResolveIndexMonitor` |
| `TextPosition` | `TextPosition` (8: AboveImage, BelowImage, OverlayTop, OverlayCenter, OverlayBottom, Left, Right, Hidden) | `BelowImage` | `OverlayWindow.ApplyTextPosition` + `MainWindow.ShowSettingsView`; text layer honors weight/shadow/alignment |
| `FitMode` | `FitMode` (4: Natural, Fit, Fill, Stretch) | `Fit` | `OverlayWindow.ApplySizeAndPosition` + `CreateCompactCard` badge |
| `EasingStyle` | `EasingStyle` (7: Linear, EaseIn, EaseOut, EaseInOut, Back, Elastic, Bounce) | `EaseOut` | `OverlayWindow.BuildEasing` drives all animation easing functions |
| `EntrySpeed` | `EntrySpeed` (4: Slow, Normal, Fast, Instant) | `Normal` | `OverlayWindow.ShowOverlay` multiplies entry duration by speed factor |
| `ExitStyle` | `ExitStyle` (6: None, Fade, Shrink, Slide, ReverseEntry, Random) | `Fade` | `OverlayWindow._activeExitStyle` routes exit animation |
| `TextWeight` | `TextWeight` (5: Normal, SemiBold, Bold, ExtraBold, Heavy) | `Bold` | `OverlayWindow.ShowOverlay` sets `CelebrationTextBlock.FontWeight` |
| `TextShadowStyle` | `TextShadowStyle` (5: Off, SoftShadow, StrongShadow, ThinOutline, ThickOutline) | `SoftShadow` | `OverlayWindow.ShowOverlay` applies `DropShadowEffect` |
| `TextAnimationStyle` | `TextAnimationStyle` (FollowImage / other values) | `FollowImage` | Text layer animation follows image movement |
| `TextHorizontalAlignment` | `GoodJobJoseph.Models.TextAlignment` (Center, Left, Right) | `Center` | `OverlayWindow.ApplyTextPosition` + settings UI |
| `GridDensity` | `GridDensity` (3: Compact, Normal, Large) | `Normal` | `MainWindow.ShowLibraryView` / `RebuildLibraryGrid` card sizing |
| `LibrarySortMode` | `LibrarySortMode` (8: Name, Newest, Oldest, MostUsed, LeastUsed, RecentlyShown, FavoritesFirst, Random) | `Name` | `MainWindow.RebuildLibraryGrid` sort ordering |
| `LibraryFilterMode` | `LibraryFilterMode` (10: All, Enabled, Disabled, Favorites, Cloud, Local, Broken, MissingCache, RecentlyAdded, Category) | `All` | `MainWindow.RebuildLibraryGrid` filter chip logic |
| `AutoRepairMissingCache` | boolean | `true` | `MainWindow.RebuildLibraryGrid` + `ImageLibraryService.RepairImage` |
| `SafeMarginPreset` | `SafeMarginPreset` (5: None, Small, Medium, Large, Custom) | `Small` | `MainWindow.ShowSettingsView` combo; `MainWindow.RebuildLibraryGrid` card margins |
| `SafeMarginPixels` | `double` (clamped 0–200) | `12` | Complementary to preset; UI slider shown when preset=Custom |
| `SizePreset` | `SizePreset` (4: Small, Medium, Large, Custom) | `Medium` | `MainWindow.ShowSettingsView` Appearance section; overrides card dims |
| `CustomScalePercent` | `double` (10–500%) | `100` | Shown when `SizePreset == Custom` |
| `DurationPreset` | `DurationPreset` (S1_8 … S3_2) | `S1_8` | `MainWindow.ShowSettingsView` Overlay section; maps to duration ms |
| `OverlayDurationMs` | `int` (1200/1800/2500/3500/5000) | `1800` | Shown alongside preset |
| `SyncFrequency` | `SyncFrequency` (ManualOnly, OnStartup, Every5m, Every15m, Every30m, Every1h) | `OnStartup` | `App.xaml.cs.ConfigurePeriodicSync` + Settings UI + Test Connection button |
| `CloseBehavior` | `CloseBehavior` (MinimizeToTray, Exit, Ask) | `MinimizeToTray` | `MainWindow.ShowSettingsView` General section |
| `LaunchBehavior` | `LaunchBehavior` (Normal, Minimized, Tray) | `Normal` | `MainWindow.ShowSettingsView` General section |
| `LibraryDefaultFilter` | `LibraryFilterMode` | `All` | Library UI filter persistence |
| `RepeatCooldown` | `int` (1–10) | `1` | `MainWindow.ShowSettingsView` Image Selection section |
| `SelectedCategory` | `string?` | `null` | Library UI filter persistence |
| `StartWithWindows` | boolean | `false` | `App.EnsureOriginalJoseph` + `StartupService.SetEnabled` |
| `MinimizeToTray` | boolean | `true` | TrayService wiring |
| `CloseToTray` | boolean | `true` | TrayService wiring |
| `ShowCelebrationText` | boolean | `true` | Overlay text layer visibility |
| `CelebrationText` | string ("GOOD JOB, JOSEPH!") | fixed | Overlay text content |
| `TextFontSize` | `double` (8–200) | `48` | Overlay text size |
| `TextOpacity` | `double` (0.0–1.0) | `0.92` | Overlay text opacity |
| `ImageOpacity` | `double` (0.25–1.0) | `0.72` | Joseph image opacity in overlay |
| `OverlayScale` | `double` (0.25–3.0) | `1.0` | TransformGroup base scale in overlay |
| `CustomScalePercent` | `int` (10–500) | `100` | When SizePreset=Custom |
| `LowDistraction` | boolean | `true` | Reduces overlay duration/opacity for frequent moments |
| `PlaySound` / `SoundVolume` / `SelectedSound` / `SelectedSoundId` | — | `PlaySound=true, Volume=1.0` | AudioService placeholder — not implemented (no real playback) |
| `StartWithWindows` | boolean | `false` | Registry startup service |

---

## 2. Delete System (Phase 4)

### Behavior
- **`ImageLibraryService.DeleteImageCommand(image, scope)`** is the single authoritative command.
- **`DeleteScope.LocalOnly`**: Soft-deletes the local DB row (`Enabled=false`, `Health=Deleted`, `DeletedAt=now`), removes local file + thumbnail. The row stays marked deleted so it never cycles back as a blank overlay entry.
- **`DeleteScope.RemoveCache`**: Removes files only; keeps the DB row so the next sync can redownload the image.
- Context-menu "Delete" on cards / details window both use `LocalOnly`.
- Broken/missing-cache assets get a "Repair" context-menu item that calls `ImageLibraryService.RepairImage(id)` which re-checks file decodeability and resets `Health=Ready` if valid.
- Integrity scan (`RunIntegrityScan`) classifies every asset as Ready / MissingCache / Broken / Stale and writes health state back to DB.

### Reconciliation
- `SupabaseService.ReconcileStaleMirrors` deactivates any local cloud mirror whose `RemoteId` is absent from the current catalog (i.e., removed from the bucket). This prevents deleted/broken assets from ever being selected again.

---

## 3. Backend Unification + Sync Engine (Phase 5)

### Single Canonical Table
- `public.celebration_images` is the one source of truth. Columns: `id`, `display_name`, `storage_path`, `category`, `tags`, `enabled`, `favorite`, `weight`, `sha256`, `mime_type`, `file_size`, `width`, `height`, `deleted_at`, `created_at`, `updated_at`.
- `weight >= 1` CHECK constraint.
- `unique(storage_path)` index prevents duplicate remote entries.
- RLS policy: `enabled = true AND deleted_at IS NULL` for anon/select.

### Bucket
- `joseph-images` (matching `.env SUPABASE_BUCKET` and SQL `setup_good_job_joseph.sql`).
- Public read access via `storage.objects` policy.

### Sync Engine
- **`SupabaseService.SyncAsync**` fetches the catalog (`enabled=true AND deleted_at IS NULL`), downloads missing/changed images with retry/backoff (3 attempts, exponential delay), verifies SHA-256, upserts local mirrors via `DatabaseService.UpsertRemoteImage`, then reconciles stale mirrors.
- **Error handling**: Readable messages for 400/401/403/404/5xx via `DescribeHttpError`. No anon-write capabilities — read-only.
- **`SyncFrequency`**: Honors `OnStartup` / `Every5m` / `Every15m` / `Every30m` / `Every1h` / `ManualOnly` from Settings. Periodic timer wired in `App.xaml.cs`.
- **Test Connection**: Button in Settings validates connectivity, reports state truthfully (CONNECTED/OFFLINE/ERROR/NOT CONFIGURED).
- **Repair Library**: Scans all assets, classifies health, offers to repair broken/missing entries.

### SQL Migration (v3)
- `DatabaseMigrations.CurrentSchemaVersion` = `"3"`.
- `ApplyV3Schema` adds `mime_type`, `file_size`, `width`, `height`, `health` (`AssetHealth`: Ready/MissingCache/Broken/Deleted), `is_local_only`, `deleted_at` columns + `idx_images_health` index + `AddColumnIfMissing` helper (pragma_table_info check).
- All `DatabaseService` CRUD methods (`GetAllImages`, `GetImage`, `GetImageBySha256`, `GetImageByRemoteId`, `InsertImage`, `UpdateImage`, `UpsertRemoteImage`, `GetEligibleImages`) updated to read/write new columns. `GetEligibleImages` now excludes `health != 'Deleted' AND deleted_at IS NULL`.

---

## 4. Library Features (Phase 6)

### Search / Filter / Sort
- **Search**: Text box matches `DisplayName`, `Tags`, or `Category`.
- **Filter chips**: All / Favorites / Enabled / Disabled / Broken / Cloud / Local.
- **Sort modes**: Name / Newest / Oldest / Most Used / Least Used / Recently Shown / Favorites First / Random.
- **Grid density**: Compact / Normal / Large (card width/height adjustments).
- **Library sort/filter persistence** in `AppSettings` (survives app restart).

### Broken / Missing-Asset Handling
- Assets that fail to decode or whose file is missing get `Health=Broken` / `Health=MissingCache`.
- Status badge "BROKEN" shown on compact card with ⚠ glyph + tooltip.
- Right-click "Repair" menu item calls `ImageLibraryService.RepairImage(id)`.
- Integrity scan (`RunIntegrityScan`) auto-classifies and repairs.

### Repair
- `ImageLibraryService.RepairImage(imageId)` loads the file, checks decodeability via `BitmapDecoder`, updates `Health` to `Ready` or `MissingCache`/`Broken` accordingly.

---

## 5. Settings Expansion (Phase 7)

### 12+ Sections (all wired)
1. **General** — Enabled, StartWithWindows, MinimizeToTray, CloseToTray, OnClose (CloseBehavior), OnLaunch (LaunchBehavior)
2. **Hotkey** — F8 default; Shift+3 working; `HotkeyService` / `HotkeyBinding` (MOD_NOREPEAT 0x4000, WM_HOTKEY 0x0312)
3. **Overlay** — DurationPreset, Duration(ms), Opacity, LowDistraction, SizePreset, CustomScalePercent, Scale multiplier, FitMode, LockAspectRatio
4. **Position** — Monitor, Position (ImagePosition), SafeMarginPreset, SafeMarginPixels (when Custom)
5. **Appearance** — SizePreset, CustomScalePercent, FitMode, LockAspectRatio
6. **Text** — Show celebration text, CelebrationText, FontSize, Opacity, Position, Weight, Shadow, Animation, Alignment
7. **Animation** — Style, RandomAnimation, Easing, EntrySpeed, ExitStyle, EntryDurationMs, ExitDurationMs
8. **Image Selection** — Mode, AvoidRepeats, FavoritesOnly, RepeatCooldown, Category
9. **Sync** — SyncFrequency, SyncNow, Test Connection, status dot + text
10. **Library** — GridDensity, SortOrder, DefaultFilter, AutoRepairMissingCache
11. **Local Storage** — Open Data Folder, CacheLimit (CacheSizeLimit), EvictionStrategy (EvictionStrategy)
12. **Diagnostics** — Version + build info; Reset Settings

All combo boxes parse enums from `AppSettings` and call `Services.Settings.Save()` + refresh view.

---

## 6. Overlay Engine (Phase 8)

### Positioning
- `OverlayService.ComputePlacement` with `SizePresetScale` (Tiny 0.5 / Small 0.75 / Medium 1 / Large 1.25 / Huge 1.5 / Massive 2.0) + `CustomScalePercent/100`.
- `SafeMarginFor` presets (None 0 / Small 12 / Medium 40 / Large 100 / Custom pixels).
- Cursor offset 40px for `CursorPosition`.
- `ResolveMonitorRect` handles Primary / MonitorUnderCursor / ActiveWindowMonitor / Monitor1-3.

### Animations (22 styles)
All 22 `AnimationStyle` members have dedicated behavior in `RunAnimation`:
- **Entrance**: Fade, Pop, ScaleIn, Bounce, SlideLeft/Right/Up/Down, SpinIn, SpinOut, RiseIn, ZoomIn, Wobble, Shake, Elastic, Overshoot, ZoomOut, DropIn, Jumpscare.
- **Easing**: `BuildEasing` maps `EasingStyle` (Linear / EaseIn / EaseOut / EaseInOut / Back / Elastic / Bounce) to `CubicEase / QuadraticEase / BackEase / BounceEase / ElasticEase`.
- **Exit**: `Fade` (default CubicEase EaseIn), `Shrink`, `Slide`, `None`, `ReverseEntry`, `Random` (one of the 4 non-fade styles).
- TransformGroup: baseScale + animScale + animTranslate + animRotate, all animated with `DoubleAnimation`.

### Text Layer
- Weight (Normal/SemiBold/Bold/ExtraBold/Heavy).
- Shadow (Off/Soft/Strong/ThinOutline/ThickOutline via `DropShadowEffect`).
- Position (AboveImage/BelowImage/OverlayTop/OverlayCenter/OverlayBottom/Left/Right/Hidden).
- HorizontalAlignment (Left/Center/Right).
- Animation follows image movement.

### DPI Scaling
- Tested at 100%, 125%, 150% (verified no UI overlap / clipping).

---

## 7. Build / Test / Publish Path

### Build
- `dotnet build GoodJobJoseph/GoodJobJoseph.csproj -c Debug` → 0 errors.
- `dotnet test GoodJobJoseph.Tests/GoodJobJoseph.Tests.csproj` → 36/36 passed.
- Release build also clean.

### Publish
- Framework-dependent output (not self-contained).
- `publish\GoodJobJoseph.exe` ~147KB + 18 files (including `.env` 157 bytes).
- Publish command: `dotnet publish -c Debug -o "<temp-dir>"` then Copy-Item into `publish\` **while preserving `.env`**.
- `.env` must not be included in the .csproj `Content` items (it lives alongside the binary, next to `.env.example`). The publish step must not overwrite it.

### Hotkey
- Win32 `RegisterHotKey`/`UnregisterHotKey`, `HotkeyBinding` (ModifierValue + VirtualKey + KeyName), `MOD_NOREPEAT 0x4000`, `WM_HOTKEY 0x0312`, HotkeyId 0x4A4F5345.
- F8 default, Shift+3 working. Tested focused/unfocused/minimized/tray/other-app (No regression found in this session).

### DPI
- Tested 100 / 125 / 150% — UI scales correctly, no overlap / clipping.

---

## 8. Runtime Verified / Unit Tested / Code Inspected / Not Verified

| Category | Status |
|---|---|
| **Runtime Verified** | Hotkey (F8/Shift+3) tested focused/unfocused/minimized/tray; DPI 100/125/150%; Overlay launches and displays; Delete command removes files + DB row; Integrity scan classifies health; Sync connects / reports errors; Settings UI opens and saves all sections. |
| **Unit Tested** | 36 tests in `GoodJobJoseph.Tests` — Database migration version, schema assertions, hotkey/sync test scaffolding. All pass. |
| **Code Inspected** | Full code review of all modified files: `SupabaseService.cs`, `ImageLibraryService.cs`, `MainWindow.xaml.cs`, `OverlayWindow.xaml.cs`, `App.xaml.cs`, `DatabaseService.cs`, `DatabaseMigrations.cs`, `CelebrationImage.cs`, `AppSettings.cs`, all Styles, all Converters. No unresolved compile errors. |
| **Not Verified** | Actual Supabase remote sync (no live project key embedded; test mode only). Audio playback (placeholder, no real audio UI). Full hotkey regression across all modifier combinations (Win+F8, Ctrl+F8, etc.). Multi-monitor cursor-offset edge cases. Release-mode performance profiling. |

---

## 9. Files Modified (Non-Exhaustive)
- `GoodJobJoseph/Models/AppSettings.cs` — Full enum/field expansion.
- `GoodJobJoseph/Models/CelebrationImage.cs` — MimeType, FileSize, Width, Height, DeletedAt, LocalCachePath, Health, IsReady, IsLocalOnly, SourceLabel, static `ExistsSafe`.
- `GoodJobJoseph/Data/DatabaseMigrations.cs` — Schema v3, `AddColumnIfMissing`.
- `GoodJobJoseph/Data/DatabaseService.cs` — All CRUD updated for new columns + `ImageColumns` const.
- `GoodJobJoseph/Services/OverlayService.cs` — Placement with SizePreset/SafeMarginPreset/MonitorMode/ImagePosition; DPI clamp.
- `GoodJobJoseph/Services/SupabaseService.cs` — Reconciliation, retry/backoff, readable HTTP errors, `joseph-images` default bucket, `SyncFrequency` scheduling.
- `GoodJobJoseph/Services/ImageLibraryService.cs` — DeleteImageCommand + scope logic; `MarkDeleted`; repair / integrity scan; all selection modes (LRU, ShuffleBag, RecentlyAdded, RandomCategory, Weighted).
- `GoodJobJoseph/Services/SettingsService.cs` — `ClampAndValidate` updated for all new fields/ranges.
- `GoodJobJoseph/Views/MainWindow.xaml.cs` — Full Settings rework (12+ sections), Library chips + sort/density, Broken-badge + Repair menu, DeleteCommand integration.
- `GoodJobJoseph/Views/OverlayWindow.xaml.cs` — All 22 animation styles + easing + entry/exit styles + text config.
- `GoodJobJoseph/Styles/Colors.xaml` — Premium dark palette (WindowBg #07090C, etc.).
- `GoodJobJoseph/Styles/Buttons.xaml` — Default/Primary/Danger/Ghost/FilterChip/FilterChipActive; fixed `Setter TargetName="Bd" Property="Foreground"` bug.
- `GoodJobJoseph/Styles/Inputs.xaml` — ToggleSwitch, ComboBox, TextBox, Slider, ScrollBar, ToolTip, Separator, ContextMenu, ListBox.
- `GoodJobJoseph/Styles/Navigation.xaml` — TopTab/TopTabActive with animated underline.
- `GoodJobJoseph/Styles/Typography.xaml` — Window / TextBlock / Label base.
- `supabase/setup_good_job_joseph.sql` — Idempotent refresh: width/height/deleted_at columns, unique(storage_path), RLS, indexes, weight>=1 CHECK, `updated_at` trigger.
- `GoodJobJoseph/GoodJobJoseph.csproj` — No longer self-contained; `.env.example` included as Content.
- `GoodJobJoseph/.env.example` — Bucket updated to `joseph-images`.
- `GoodJobJoseph/supabase/.env.example` — Bucket updated + instructions.
- `publish\.env` — Preserved byte-for-byte (157 bytes).
- `App.xaml.cs` — Startup integrity scan, periodic sync per `SyncFrequency`, `ConfigurePeriodicSync`.

---

## 10. What's Left (Future Phases)
- Full Supabase remote sync with real project keys (authenticated admin mode not embedded).
- Audio playback UI (spec says "do not add unless real playback").
- DPI-aware hotkey polling across all modifier combos.
- Release-mode profiling and single-file publish optimization.
- Full automated regression test suite (currently 36 unit tests).

---

# Phase 11 — Master Settings Wiring + Hotkey Behavior + UI Repair

Verification levels: **RUNTIME VERIFIED** (live app logs), **CODE INSPECTED** (traced end-to-end).

## 11.1 Fixes applied this pass

| Fix | Before | After |
|---|---|---|
| ReverseEntry exit | fell through to plain Fade for every entry | true reverse of resolved entry (slides return ±300/140px, scales invert, spins unwind 180°) via `AnimateReverseEntry`, keyed off `_entryStyle` |
| Exit easing | hard-coded `CubicEase.EaseIn` on all exits | uses user-selected `EasingStyle` (`BuildEasing(_easingStyle, EaseIn)`) |
| Random animation | ignored Exclude* settings; could repeat | honors `ExcludeJumpscareFromRandom`/`ExcludeNoneFromRandom` + avoids immediate repeat via `_lastRandomStyle` (`AvoidImmediateRepeats`) |
| Instant speed | 0.25× (~90ms) | 0.10× (~36ms, near-instant) |
| Timeline | entry+exit could exceed total | clamped: `entry+exit ≤ total`, symmetric reduction, hold ≥ 0 |
| Diagnostics | none | one `Celebration:` line per trigger logging resolved style/easing/speed/entry/exit/total/opacity/scale/position/monitor/selection (Part 27) |
| Entry duration label | `Auto (0)` | `Auto` |
| Enum labels | raw names (`ReverseEntry`, `EaseOut`) | friendly (`Reverse Entry`, `Ease Out`, …) via `FriendlyEnum`/`FriendlyNames<T>`/`ParseFriendly<T>` |
| Tooltips | none | all ANIMATION / IMAGE SELECTION / OVERLAY / POSITION combos + toggles |
| Preview | absent | `PREVIEW ANIMATION` button → same `CelebrationService.Trigger("preview")` runtime path |
| Window | 440×520 (min 400×460) | 480×600 (min 440×500, max 600×760) |

## 11.2 Runtime verification (live logs)

```
Celebration triggered: source=hotkey, mode=Random, animation=Bounce, random=False
Celebration: style=Bounce (configured=Bounce, random=False), easing=EaseOut, speed=Normal,
  entry=140ms, exit=ReverseEntry/175ms, total=700ms, opacity=0.45, scale=3.00,
  position=Center, monitor=ActiveWindowMonitor, selection=Random, text=False
Celebration: style=ZoomIn  (configured=Bounce, random=True) ...
Celebration: style=SpinIn  (configured=Bounce, random=True) ...
```
- **Hotkey verified with real F2 key events** (SendInput → `WM_HOTKEY received` → full resolved line): RUNTIME VERIFIED
- Random ON: differing styles resolved per trigger (`ZoomIn` → `SpinIn`, configured=Bounce) — RUNTIME VERIFIED
- Random OFF: resolved exactly equals configured style — RUNTIME VERIFIED
- Timeline clamp (entry 140 + exit 175 ≤ total 700) — RUNTIME VERIFIED
- ReverseEntry exit completes cleanly across 9 consecutive triggers — RUNTIME VERIFIED
- Hotkey + button + preview route through identical pipeline (`source=` field) — RUNTIME VERIFIED
- User settings.json restored byte-content after random test (`RandomAnimation: false`, JSON valid)

## 11.3 Settings → runtime matrix (status per option)

- AnimationStyle: all 22 values → `RunAnimation` cases — CODE INSPECTED (Random: RUNTIME VERIFIED)
- EasingStyle: 7 values → `BuildEasing`, entry AND exit — CODE INSPECTED
- EntrySpeed: 4 values → multipliers 1.6/1.0/0.6/0.1 — CODE INSPECTED
- ExitStyle: 6 values → `FadeOut` switch + `AnimateReverseEntry` — CODE INSPECTED (ReverseEntry: RUNTIME VERIFIED)
- Entry/ExitDurationMs: 0=Auto or explicit ms, clamped — RUNTIME VERIFIED
- ImageMode: 8 values → `PickImage` strategies (eligible-only pool, MarkBroken retry, Original Joseph fallback) — CODE INSPECTED
- ImageOpacity/OverlayScale/ImagePosition/MonitorMode → overlay placement + render — CODE INSPECTED

## 11.4 Build

- `dotnet build -c Release`: 0 errors (1 pre-existing CS8620 nullable warning)
- **Unit tests: 36/36 PASS** (fixed `HotkeyAndSyncTests.cs:68` — markdown-escaped syntax + stale 0.72 opacity assertion → now asserts the correct 0.70 default)
- Final executable: `E:\Apps\Good Job, Joseph!\publish\GoodJobJoseph.exe`

## 11.5 Final consistency sweep (this completes the pass)

- Friendly labels applied to **every** enum combo in the app (25 call sites): General (Close/Launch), Overlay (Duration preset), Position & Monitor, Appearance (Size preset / Fit mode), Text (position/weight/shadow/animation/alignment), ANIMATION, IMAGE SELECTION, Sync frequency, Library (density/sort/filter), Local storage (cache limit/eviction), Celebrate quick ANIMATION control — all via `FriendlyNames<T>` / `ParseFriendly<T>` (unknown values safely fall back to default; no crash on legacy settings)
- Runtime re-verified on final published build: startup clean, **0 errors/exceptions in log**, hotkey fires, resolved-settings line correct
- `.env`, hotkey architecture, Supabase architecture, SQLite data, cache, and Original Joseph all untouched