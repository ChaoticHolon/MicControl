# MicControl fuzzing log

One section per round, newest last. Each round reads this log first, so it attacks what earlier rounds missed and what changed since the last one (the `fuzz-round` skill does this).

**Run the standing suite after a build** (from the repo root): `powershell -ExecutionPolicy Bypass -File tools\Fuzz.ps1` (`-Scale 5` for more cases, `-Seed N` to repeat a failing run). It runs the opt-in tests in `Sources/MicControl.Tests/Fuzz/` (`MICCONTROL_FUZZ=1`) and `tools/FuzzWav.cs`. On Linux: `MICCONTROL_FUZZ=1 dotnet test --project Sources/MicControl.Tests -- --filter-namespace MicControl.Tests.Fuzz` and `dotnet run tools/FuzzWav.cs`.

| Round | Date | Covered up to | Found |
| --- | --- | --- | --- |
| 1 | Oct 19 2026 | `e084b3a` | 3 crashes, 2 smaller issues |
| 2 | Oct 19 2026 | `a700932` | 3 crashes, 2 smaller issues |
| 3 | Oct 19 2026 | `c25f946` | 3 issues, 1 cap; 1 open (two keys of one hotkey) |

# Round 1: settings, imports, hotkeys, signatures

Date: Oct 19 2026 · Branch `fix/fuzzing-hardening` · PR [#39](https://github.com/ChaoticHolon/MicControl/pull/39) into `dev`

## Summary

I ran 7 stress iterations, under the limit of 15. They found 3 distinct crashes, which is where the plan's circuit breaker stops, plus 2 smaller robustness issues. All five are fixed, each one has a regression test, and the full suite passes on Linux: 272 tests, 267 passed, 5 skipped (the screenshot tests). The PR's CI covers Windows.

Everything ran in the cloud container (.NET 10.0.402 SDK, headless Avalonia). `%APPDATA%\MicControl\logs` only exists on Windows, so I took exceptions from the test output and from `Dispatcher.UIThread.UnhandledException`. Nothing needed a session on the developer's PC.

## What was tested

| Target | Method | Volume | Result |
|---|---|---|---|
| Settings JSON (`SettingsStore.Load`/`Save`, `Normalize`) | Seeded mutations of two seed files: hostile tokens swapped in for any value (null, ±1e308, 1e39, 2^31, -2^31-1, `[]`, `{}`, enum names in the wrong place, …), byte noise, truncation, 1-200 levels of nesting, lone-surrogate escapes. Each loaded file must also save and load again and meet range checks | 20,000 per run | **2 crashes**, plus out-of-range values |
| Locked or unreadable `settings.json` | File opened with `FileShare.None`; a folder where the file should be | 2 cases | **1 crash** |
| MicSwitch 1.x import (`LegacyConfigMigrator`) | Same mutations on a full 1.x config (all four config types, icons, bounds), saved as UTF-16 and UTF-8 | 5,000 per run | Whole import lost on one bad entry; huge numbers crashed the first save |
| Hotkey text (`HotkeyGesture`) | Random strings built from modifier names, key names, aliases, `+`, whitespace, control characters, non-ASCII and surrogates. Round trips through `ToString` and through the JSON file | 100,000 per run | No crashes. 2 round-trip bugs |
| Release signatures (`SshSignature`, `Ed25519`) | Bit flips, truncation, extensions and hostile length prefixes (0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, 0) on a real SSHSIG; garbage armour, keys and namespaces; 300 random raw Ed25519 keys and signatures | ~2,500 | **Clean**: never threw, never accepted a mutation |
| Update feed (`ReleaseFeed.Parse`, `FindSha512`, `TryParseVersion`) | Mutated GitHub release JSON; edge-case tags | 20,000 | **Clean**: only `JsonException`/`InvalidOperationException`, which `IsUpdateFailure` already catches |
| Headless UI | 8 threads posting 3,200 random steps to the UI thread (as the Windows hook and Core Audio callbacks do): hotkey presses and releases on the main key and every custom action, mode changes, device unplug/replug and list changes, device selection, page switches, mute toggles, custom hotkeys on/off, overlay visibility, mic volume. Main window and overlay open. 3 seeds | 9,600 steps | **Clean**. Push-to-talk always settles muted |
| Hand-edited settings opening every window | 16 out-of-range files: each opens the main window and overlay, visits every page, toggles, presses the hotkey and saves | 16 | **2 crashes** (the same as rows 1 and 3 above) |

## What crashed, and the fixes

1. **Infinity in settings made every save throw (crash).** A float too large for its type, such as `"notifications": {"volume": 1e39}` or the mic volume, reads as infinity. System.Text.Json then refuses to write infinity (`ArgumentException`), and `SettingsService.Flush` only catches IO errors, so the first save from the timer crashes the app. The same crash hit a MicSwitch import with `"Volume": 1e300` on its very first start (`Save(migrated)` in `Load`).
   *Fix:* `AppSettings.Normalize` clamps volumes to 0-1, overlay opacity to 0.2-1 and scale to 0.6-2.5 (new `OverlaySettings.Min/Max*` constants, now also used by the sliders' view model). A MicSwitch import is normalized before it's saved.
2. **Negative window size crashed the main window.** `window.bounds` with a negative width threw `ArgumentException: -800 is not a valid value for 'Width'` in the `MainWindow` constructor, so the app couldn't start.
   *Fix:* `Normalize` drops window, overlay and monitor bounds that are infinite or not positive, and the default placement is used instead.
3. **A locked `settings.json` crashed startup.** `SettingsStore.Load` didn't catch `IOException` or `UnauthorizedAccessException`, so a file held by a backup, sync or antivirus tool stopped the app. The `.corrupt` copy could fail the same way.
   *Fix:* the read is retried 5 times, 200 ms apart. If it's still locked, defaults load for the session with setup marked done (no wizard and no first-run autostart), `SettingsStore.LoadError` is set, `Save` leaves the file alone, and `App` logs a warning. The file is now read once and parsed from memory, which still skips a UTF-8 BOM (a test covers it). If the `.corrupt` copy can't be written, the original is protected the same way. A failed save of a MicSwitch import no longer crashes; the next change saves it.

Smaller issues, also fixed:

4. **MicSwitch import: one bad entry lost everything.** A non-object item, `MicrophoneLineId` given as a string, a `DeviceId` given as a number, or an icon that isn't valid base64 threw. `TryMigrate` caught the exception, so the user silently got no import at all. Entries, fields and icons are now skipped one at a time, and `UnauthorizedAccessException` (unreadable `config.cfg`) is caught as well.
5. **Hotkeys that didn't survive a save.** `"+None"` parsed as a key named "None", which prints as `None` and loads back as no hotkey. A key containing half a surrogate pair was saved as U+FFFD, a different key. `TryParse` now rejects a key named None and keys with control characters or lone surrogates.

Unknown enum values (`"muteMode": 99`, `"logLevel": 77`, a custom hotkey `"action": 1234`) didn't crash anything I could reach. `Normalize` resets them to defaults anyway (an unknown custom action drops that row), because `MuteModes.First(...)` in `MicrophoneStatusDetail` throws for an unknown mode.

## Tests kept and removed

Kept in the PR as regression tests:
- `SettingsTests`: locked file, BOM file, out-of-range values clamped and saveable, MicSwitch config with huge numbers, one malformed MicSwitch entry (4 cases).
- `HotkeyGestureTests`: new junk cases and a lone-surrogate case.
- `Ui/UiStressTests`: the multi-threaded stress loop (one seed, about 1 s) and the 16 hand-edited settings files.
- `TestHarness`: a new `settingsJson` parameter for starting from a file's exact text.

Removed from the repo: the 20,000-iteration fuzz harness. It adds about 20 s and was only needed to find these problems. A copy is in [docs/fuzzing/CoreFuzzTests.cs](/mnt/project-files/docs/fuzzing/CoreFuzzTests.cs) for re-running: copy it into `Sources/MicControl.Tests/Fuzz/` and run `dotnet test --project MicControl.Tests -- --filter-class MicControl.Tests.Fuzz.CoreFuzzTests`. All of its targets pass with the fixes.

## Remaining stability recommendations

- `SettingsService.Flush` still catches only IO errors. If a future setting adds a value JSON can't write, the save-timer crash comes back. Consider catching `ArgumentException`/`NotSupportedException` there too and logging them.
- These tests don't cover the Windows-only code: low-level hooks under real input floods, Core Audio device churn with COM exceptions, and `WindowGuard`. The fakes model the dispatcher marshalling but not COM failures. A short manual pass on the PC (plug and unplug a USB mic while spamming push-to-talk) would close that gap.
- `ReleaseFeed.Parse` is safe only because callers catch `InvalidOperationException`. Making it return `null` for a feed of the wrong shape would remove that dependency.
- The overlay's custom icon paths and user-added sounds aren't fuzzed: corrupt image or WAV files, or paths that point at folders. That's a good next target.

---

# Round 2: going deeper

Date: Oct 19 2026 · Branch `fix/fuzzing-round-2` · PR [#40](https://github.com/ChaoticHolon/MicControl/pull/40) into `dev`

## Strategy

Round 1 covered the settings, import, hotkey and signature parsers, and the Core parsers came out clean. So round 2 went after input that reaches the app **from files and from other programs**, which round 1 didn't touch:

- WAV files, through the real NAudio code that plays them (structure-aware mutations of the shipped cues).
- Custom icons, including decompression bombs, run under a memory cap like a PC with less free RAM.
- MicSwitch's sound folder.
- Combinations of an update feed and a signature, rather than each one separately.
- Setup and autostart command lines.
- Shutdown races.
- Round 1's new lock/retry path, run concurrently.
- A property check of every key Avalonia can report against the Windows virtual-key map.

The WAV fuzzer runs on Linux because `NAudio.Core` is managed code. It calls the player's real catch filter by reflection.

Round 2 used 8 iterations. Like round 1, it stopped at 3 distinct crashes, plus 2 smaller issues. All of them are fixed, and 290 tests run on Linux: 285 pass and 5 screenshot tests are skipped.

## What was tested

| Target | Method | Volume | Result |
|---|---|---|---|
| WAV playback (`WasapiSoundPlayer` → NAudio `WaveFileReader` → `SampleChannel`, read to the end) | Byte noise, truncation, hostile 32-bit fields (chunk sizes, rates), 16-bit header fields (format tag, channels, bits, block align), junk chunks with hostile sizes, RIFF size; each case had a 5 s hang timeout | 20,000 + 40,000 after the fix | **Crash**: 8 exception kinds escaped the catch filter. No hangs |
| Custom overlay icons | Garbage, empty, truncated, a directory, a bad ICO, a PNG named `.ico`, a 0 × 0 PNG, a 1.5 MB PNG of 40000 × 40000; startup, mute toggles and picking each one in the dialog, with 12 GB of address space | 8 + timing | **Crash** under a memory cap; a 15-30 s UI freeze without one |
| Image header reader (new) | 200,000 structure-aware mutations of PNG, GIF, BMP, JPEG and ICO headers | 200,000 | It found 2 bugs in my own first draft, both fixed before the commit: an ICO entry pointing at itself recursed forever, and a 4G × 4G size overflowed the pixel count |
| MicSwitch sound import | A locked sound file and a folder named `x.wav` in `Resources\Notifications` | 2 | **Crash** on first start |
| Update feed + signature | A signed `SHA512SUMS` replayed onto a newer tag; setup names that differ by case, prefix or suffix; an `https` asset followed by an `http` copy; `file:` and userinfo URLs; path traversal in an asset name; tags with leading zeros or 4 parts; `SHA512SUMS` edge cases (CRLF, BOM, tab, short hash, duplicate lines) | 25 | Secure: a replayed or misnamed checksum file never verifies anything. **A BOM** hid the setup's line |
| Setup and autostart command lines | Random combinations of every switch, `/x` forms, `--scope=` variants, empty or quote-only tokens and odd executable paths; parse then round-trip | 20,000 + 13 | Clean |
| Hotkey → Windows key map | Every `Avalonia.Input.Key` name, as `HotkeyBox` stores it, looked up in `VirtualKeys` | 260 names | **46 never fired**, including Japanese, Korean and Brazilian keys |
| Shutdown race | Hotkey presses, device changes and a running volume-repeat timer, all posted and then the view model disposed before they run | 150 events | Clean |
| Round 1's lock/retry path | One thread saving in a loop while another loads | 4 s | Not reachable in the app (see below) |

## What crashed, and the fixes

1. **A WAV NAudio can't play crashed the app from the Sounds page's Play button.** `WasapiSoundPlayer.Play` caught only COM, IO, InvalidOperation and Format errors. NAudio also throws `ArgumentException` ("Unsupported source encoding" for perfectly valid ADPCM and µ-law WAVs, and "Must read complete blocks"), `InvalidDataException` (bad chunk sizes), `DivideByZeroException` (block align 0), `OverflowException` and `ArgumentOutOfRangeException`. The Play button is an `AsyncRelayCommand`, so those reached the dispatcher and crashed the app. On mute and unmute the same failure was a silently missing cue. Such files reach the folder through the MicSwitch import, which copies them unchecked, or by hand.
   *Fix:* a single `IsUnplayable` filter, which 40,000 mutated files confirm catches everything, also used when adding a sound.
2. **A huge custom icon stopped every start.** Skia reports pixels it can't allocate as a plain `System.Exception`, which `LoadImage`'s filter didn't catch. It threw from the view model's constructor, and because the path is saved in settings, it happened on every launch. With enough memory it didn't crash, but each decode froze the UI for about 15 s and held gigabytes. Picking such an image froze the UI long enough to set off the `UiWatchdog`.
   *Fix:* the new Core `Imaging/ImageSize` reads the size from the header first and refuses anything over 64 megapixels, which still allows a 48 MP phone photo, or a size of 0. The allocation failure is caught too.
3. **A locked or unreadable MicSwitch sound stopped the first start.** `SettingsStore.CopyDirectory` ran before the import was saved, with no error handling, so the crash repeated on every start until the file came free.
   *Fix:* the copy goes one file at a time, skips files it can't copy and never fails the load.

Smaller issues, also fixed:

4. **Hotkeys on IME and regional keys never fired.** Japanese 変換/無変換 (`ImeConvert`/`ImeNonConvert`) are popular push-to-talk keys. They, the Korean and Kana mode keys, ABNT C1/C2 and the OEM/DBE keys under the names Avalonia's `ToString()` picks (for example `DbeNoRoman` for Attn) all mapped to virtual key 0. The 24 names still unmapped don't exist on PC keyboards: TV remote keys, Fn arrows and `System`.
5. **`SHA512SUMS` with a byte order mark.** The setup's line went unmatched, so the update would never be offered. The release workflow writes the file without a BOM, so this can't happen today, but a hand-made file from Windows PowerShell 5.1 would have one. It is now ignored, which is safe because the signature covers the exact bytes.

## Checked and found sound

- A signed checksums file from one release can't make another release verify. The setup's name comes from the tag, and the signed file has to list exactly that name.
- An `http` copy listed after the `https` setup cancels the offer instead of downgrading to it. `file:` URLs and asset names with a path in them are refused.
- Hotkey and device events queued behind shutdown don't throw.
- A 6000 × 4000 photo as the icon adds about 0.2 s to startup and about 1 ms per mute toggle.
- On Linux, a load racing a save in another thread can briefly see no file at all, and loads defaults. The app can't hit this: it loads settings once, at startup, in the single instance, and on Windows the same race shows up as a sharing violation that round 1's retry handles. I'm noting it for the Linux port.

## Files

- Kept in the PR: `ImageSizeTests`, new cases in `SettingsTests`, `ReleaseFeedTests` and `UiStressTests` (bad icons, dispose race).
- Archived outside the repo: [docs/fuzzing/Round2Tests.cs](/mnt/project-files/docs/fuzzing/Round2Tests.cs) (the full round-2 harness), and [docs/fuzzing/WavFuzz.Program.cs](/mnt/project-files/docs/fuzzing/WavFuzz.Program.cs) with [wavfuzz.csproj](/mnt/project-files/docs/fuzzing/wavfuzz.csproj) (the WAV and key-map fuzzer: build it with `dotnet build` and run the dll with a case count).

## Recommendations for round 3

- Adding a sound converts it through Media Foundation, and the Core Audio paths can throw COM errors. Both are Windows-only and need a session on the PC: fuzz MP3 and M4A files through `ConvertToWav`, and plug and unplug devices while sounds play.
- `MicSwitch`'s copied sounds are still played unchecked. A quick header check when importing or adding, like the one icons now get, would give a clear message instead of a silent failure.
- Settings export and import (a planned 1.x feature) and the local control API (OSC, named pipe) will be the next parsers to take outside input. Fuzz them as they're written.

# Round 3: state, the folder around the app, and its neighbours

## Strategy

Rounds 1 and 2 broke file parsers (settings, MicSwitch configs, WAVs, icons). Round 3 aimed elsewhere:

- **State over time**, not single inputs: a model-based fuzzer drove the hotkeys through random presses, releases, mode changes and switches turned on and off, then checked that push-to-talk ends muted and the volume stops changing.
- **The folder around the app**: what happens when the settings folder, or a folder inside it, can't be created.
- **Other programs' data the app acts on**: the MicSwitch autostart entries MicControl takes over, fuzzed against a reference of what the path really is.
- **Size and depth**: settings files with 100,000 nested brackets, 200,000 custom hotkeys, a 50 MB device id, extreme numbers.

To make the hotkey fuzzer honest, the test fake now behaves like the real Windows hook: a repeated press is ignored and a release only reaches the registration that saw the press. With the old fake, the stuck-live bug below could never show.

## What was tested

| Area | How | Cases | Result |
| --- | --- | --- | --- |
| MicSwitch autostart takeover (`AutostartCommand`) | Differential fuzz: random paths with ".exe" inside folder names, quoted and unquoted | 20,000 | 5,255 wrong (bug 1), 0 after the fix |
| Hotkey state machine | Model-based: 300 seeds × 40 steps of presses, releases, switches, modes | 300 sequences | 53 seeds left the mic live (bug 2), 0 after the fix |
| Held volume key | Hotkeys changed while held | 1 | Repeat timer kept running (bug 2), stops after the fix |
| Settings folder can't be created | A file where `logs` or `Icons` goes | 3 paths | Start, icon picker and Open logs folder crashed (bug 3), all handled after the fix |
| Setup command line (`SetupCommand`) | Random argument lists, round trip through `ToArguments` | 50,000 | Sound |
| Settings size and depth | Deep nesting, 200k hotkeys, 50 MB string, 1e308 numbers | 5 files | No crash; slowest load 2.5 s (50 MB) |
| Hotkey text | Random Unicode (combining marks, zero-width, CJK) | 200,000 | Sound: everything parsed round-trips |
| Many custom hotkeys in the UI | Opening the Hotkeys page | 100 to 5,000 rows | Slow (see below) |

## What crashed or misbehaved, and the fixes

1. **A working MicSwitch lost its autostart.** On first run MicControl takes over MicSwitch's autostart entry only when MicSwitch's program is gone. For an unquoted command, the path was cut at the first ".exe" anywhere, so `C:\Users\alex.exeter\AppData\Local\MicSwitch\MicSwitch.exe` was read as `C:\Users\alex.exe`. That doesn't exist, so the entry was judged stale and deleted, even though MicSwitch was installed. User names and folders like `alex.exeter`, `Tools.exe.d` or `.exe` all triggered it. Fix: ".exe" must end a name (followed by a space or the end).
2. **Push-to-talk stayed live after letting go.** Changing any hotkey setting (turning Custom hotkeys on or off, switching a row, editing a row) re-registers every hotkey. The new registration never saw the press, so it ignored the release. A keyboard key's auto-repeat hides this, but a mouse button doesn't repeat: with push-to-talk on a mouse side button, clicking a setting while holding it left the mic live until the next press. A held volume key on a mouse button kept the volume repeating the same way. Fix: re-applying the hotkeys now ends any hold (`MainViewModel.HotkeyRegistration`), so push-to-talk fails muted.
3. **A settings folder that can't be written stopped the app.** If the logs folder couldn't be created (a file in its place, or a roaming AppData redirected to a network share that's offline), the logger threw at startup and the app never started. Picking an overlay icon and About → Open logs folder crashed the same way when their folder couldn't be created. Fix: the logger starts anyway and drops lines until the file can be opened, and the other two show a message.

The circuit breaker stopped the round here (three distinct issues).

## Found but not changed

- **Two keys of one hotkey held together.** When the main key and the alternative key are both held, letting go of either one releases the hotkey. In push-to-talk that mutes early (safe); in push-to-mute it goes live while the other key is still down. The fix is in the Windows hook (`GlobalHotkeyService.Process`: only the key that pressed it should release it), which can't run on Linux, so it needs a test on the PC.
- **Very many custom hotkeys** (fixed after the round, at the developer's request). Each Hotkeys page row costs about 33 ms to build headlessly: 1,000 rows took 33 s and 5,000 took 163 s, long enough for the UI watchdog to restart the app. Custom hotkeys are now capped at 100 (`AppSettings.MaxCustomHotkeys`): a longer hand-edited list is cut on load, and Add is disabled at the cap with a note saying why. Test: `UiStressTests.CustomHotkeys_CappedOnLoadAndOnAdd`.
- **Exclusive hotkeys and Win+L.** Still not released by the missed key-up check, as documented: a swallowed key never shows as down.

## Files

- Fixes: `AutostartCommand.cs`, `MainViewModel.cs`, `MainViewModel.Overlay.cs`, `MainViewModel.AboutAndSetup.cs`, `FileLoggerProvider.cs`; CLAUDE.md's held-hotkey line.
- Kept tests: `AutostartCommandTests` (3 paths, one installed-MicSwitch case), `UiStressTests.PushToTalk_HeldOnAMouseButton_WhileHotkeysChange_EndsMuted`, `UiStressTests.SettingsFolderCantBeWritten_PickIconAndOpenLogs_ShowAMessage`, `FileLoggerTests.LogsFolderCantBeCreated_StillStarts_AndDropsLines`, and the stricter `FakeHotkeys`.
- Archived scratch fuzzers: `/mnt/project-files/docs/fuzzing/Round3Tests.cs`.
- Final run: 298 tests, 293 passed, 5 skipped (screenshots), 0 failed.

## Recommendations for round 4

- Fix and test the two-keys release on the PC, with a session on the developer's device, and fuzz `GlobalHotkeyService` with real `SendInput` events there.
- Still open from round 2: Media Foundation conversion of MP3/M4A files when adding a sound, and unplugging devices during playback, both on Windows.
