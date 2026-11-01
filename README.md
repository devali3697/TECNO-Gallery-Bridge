# TECNO Gallery Bridge — Windows MVP

Native WPF desktop application for browsing Android media over a USB MTP connection and copying selected recordings to a Windows folder. The original phone files are never deleted.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK (or Visual Studio with .NET desktop development workload)
- USB data cable; phone unlocked and set to **File Transfer (MTP)**

## Run on Windows

Double-click `BUILD-WINDOWS.bat` and open the `.exe` in the `publish` folder after a successful build. Alternatively, run `dotnet restore` and `dotnet run` in PowerShell.

If the EXE exits silently, run `RUN-DIAGNOSTIC.bat`. Startup exceptions are also displayed in a dialog and written to `%LOCALAPPDATA%\TECNO Gallery Bridge\startup-error.log`.

## Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Look in `bin\Release\net8.0-windows\win-x64\publish`.

## MVP behavior

1. Detects first available MTP device (multi-device picker comes later).
2. Lists photo/video filenames, file sizes and parent folders, including additional video/photo formats.
3. Filters videos and screen recordings heuristically by folder/file name; filename search and sort controls.
4. Saves selected files to Windows with collision-safe names and a `.partial` staging file, tracking bytes reported during transfer.
5. Checks transferred size when supplied by MTP and leaves phone originals untouched.
6. Displays media progressively while the device is scanning, updates the count after each small batch, and allows stopping the scan while keeping already-discovered files.
7. Provides photo preview when a photo is selected, plus lazy photo thumbnails for visible file rows. Preview uses a temporary local cache.
8. Video Play downloads the original file into the chosen Windows save folder first, then opens it in an embedded LibVLCSharp player (bundled VLC decoder). Large videos are **not** downloaded automatically. The Windows save folder is always available for external playback.
9. Bundles a custom application icon for taskbar and Explorer.

## Known limitations / next milestones

- **Not yet hardware-tested:** requires Windows and a physical TECNO phone; builds and MTP API compatibility must be verified there.
- Video thumbnails remain placeholder icons until a future thumbnail-extraction integration. Playing a video requires transferring the complete video to PC first; this can take time for 1–2 GB recordings.
- Video playback uses LibVLCSharp.WPF and VideoLAN.LibVLC.Windows (LGPL-licensed third-party components). Keep their license notices when redistributing the published application.
- Photo thumbnails load on demand while scrolling (limited to 12 MB photos); select a photo to load its larger preview.
- Recording-folder naming differs across TECNO/HiOS versions: inspect actual folders and tune filter.
- Transfer progress is based on the bytes the MTP library writes; some devices may report size inaccurately. Cancellation is not yet implemented.
- Large recursive galleries may take time to index; implement caching, pagination and thumbnails next.
- `.partial` files are left when transfer fails; a cleanup/retry UI is planned.
- Scan performance depends on how many folders HiOS exposes. Inaccessible files are skipped individually; Android data/obb and thumbnail-cache folders are ignored.
