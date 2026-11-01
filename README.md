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
8. Save & Open downloads the original video into the chosen Windows save folder, then launches the installed Windows default video player. Large videos are **not** downloaded automatically.
9. Bundles a custom application icon for taskbar and Explorer.
10. Remembers the selected Windows save folder between launches. On systems with an E: drive and no saved preference, defaults to `E:\TECNO Gallery Bridge`; otherwise uses the user's Videos folder. Temporary photo previews are stored under `.tecno-preview-cache` in the chosen save folder rather than the C: temporary directory.

## Known limitations / next milestones

- **Not yet hardware-tested:** requires Windows and a physical TECNO phone; builds and MTP API compatibility must be verified there.
- Visible videos up to 15 MB are temporarily cached under the chosen save folder and sent to Windows Shell's thumbnail provider. Thumbnail extraction depends on the installed Windows codec/thumbnail handler; if Windows cannot provide one, the play icon remains. Videos larger than 15 MB are not automatically downloaded. When a large video is explicitly saved to PC, the app attempts to generate its thumbnail from that local file without re-downloading it.
- No bundled video codec engine. This lightweight build relies on the user's installed Windows player. If a video cannot play, open the save folder and try a compatible player such as VLC installed separately.
- Photo thumbnails load on demand while scrolling (limited to 6 MB photos and four outstanding requests); select a photo to load its larger preview. A failed photo thumbnail stores a tooltip error for diagnosis.
- Recording-folder naming differs across TECNO/HiOS versions: inspect actual folders and tune filter.
- Transfer progress is based on the bytes the MTP library writes; some devices may report size inaccurately. Cancellation is not yet implemented.
- Large recursive galleries may take time to index; implement caching, pagination and thumbnails next.
- `.partial` files are left when transfer fails; a cleanup/retry UI is planned.
- Scan performance depends on how many folders HiOS exposes. Inaccessible files are skipped individually; Android data/obb and thumbnail-cache folders are ignored.
