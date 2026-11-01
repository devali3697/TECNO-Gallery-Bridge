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
6. Displays a visible indeterminate scan indicator and folder-by-folder scan status.
7. Provides photo preview when a photo is selected, plus lazy photo thumbnails for visible file rows. Preview uses a temporary local cache and never downloads large video files automatically.
8. Bundles a custom application icon for taskbar and Explorer.

## Known limitations / next milestones

- **Not yet hardware-tested:** requires Windows and a physical TECNO phone; builds and MTP API compatibility must be verified there.
- Video previews are not yet implemented: select and save a recording, then open it from Windows. Large recordings are never automatically downloaded for preview.
- Photo thumbnails load on demand while scrolling (limited to 12 MB photos); select a photo to load its larger preview.
- Recording-folder naming differs across TECNO/HiOS versions: inspect actual folders and tune filter.
- Transfer progress is based on the bytes the MTP library writes; some devices may report size inaccurately. Cancellation is not yet implemented.
- Large recursive galleries may take time to index; implement caching, pagination and thumbnails next.
- `.partial` files are left when transfer fails; a cleanup/retry UI is planned.
- Scan performance depends on how many folders HiOS exposes. Inaccessible files are skipped individually; Android data/obb and thumbnail-cache folders are ignored.
