# AGENT.md — PDF to Tiles (`pdf-to-Tiles`)

## 1. Project Overview
- **Application:** PDF to Tiles (`C:\Users\jefff\source\repos\pdf-to-Tiles`)
- **Remote Repository:** `https://github.com/jeff-fisher-blackberry/pdf-to-tile`
- **Stack:** C# / .NET Windows desktop application (WPF on `.NET 9.0` / `.NET 8.0` `net9.0-windows` x64, MVVM with `CommunityToolkit.Mvvm`, `PDFtoImage` for PDF rendering, and `OpenCvSharp4` + `OpenCvSharp4.runtime.win` for deskewing and slicing).
- **Primary Purpose:** Open board/wargame PDF rulebooks and counter sheets (starting with Redmond A. Simonsen's 1980 SPI wargame *WorldKiller* — 100-piece / 10×10 die-cut counter sheet, front and back), inspect pages interactively, export full pages to lossless high-DPI PNGs (300–600 DPI), straighten/deskew tilted scans, and slice configurable grids into individual front/back piece `.png` files.

---

## 2. Core Pipeline & Architecture
Keep UI orchestration strictly separated from PDF rendering and image processing:
1. **PDF Viewer & Non-Locking Page Navigation (`PdfPageRenderer`):**
   - Load PDF bytes into memory or open with `FileShare.ReadWrite` so the source PDF on disk is never locked while viewing or re-exporting.
   - Render low-DPI previews (120–150 DPI) for responsive UI zoom/pan navigation and high-DPI bitmaps (300–600 DPI) on demand for lossless `.png` capture and slicing.
2. **Deskew & Straighten (`DeskewService`):**
   - Auto-detect rotational tilt on scanned counter sheets (e.g., Hough line transform, `MinAreaRect`, or projection-profile variance) with manual fine-angle override (`±0.1°` increments).
   - Rotate around image center using high-quality bicubic/Lanczos4 interpolation (`InterpolationFlags.Lanczos4`) with clean border padding.
3. **Configurable Grid Slicing & Front/Back Pairing (`GridBoundsDetector`, `GridSlicerService`, `SheetProfile`):**
   - Support reusable JSON sheet presets (`SheetProfile`: rows, columns, outer grid crop bounds, inward pixel inset, and back-side orientation mapping).
   - Bound the inner counter matrix explicitly (do **not** blindly divide full scanned pages containing scanner margins or staple marks by rows/cols).
   - Apply a configurable inward pixel inset (`2–6 px` scaled by DPI) so die-cut lines and adjacent counter borders do not bleed into sliced pieces.
   - Support front/back orientation mapping modes (`Direct`, `HorizontalFlip`, `VerticalFlip`, `Rotate180`) so `Front_R01_C01.png` pairs accurately with its true reverse side `Back_R01_C01.png`.

---

## 3. Operational Guardrails & IDE Rules

### Strict Scope & Zero-Drift Policy
- **Authorized Scope Only:** Modify only the specific files required by the active task. Do not refactor, reformat, or "clean up" unrelated files or working features.
- **No Unsolicited Builds or Runs:** Do not run `dotnet build`, `dotnet test`, or launch the app automatically upon opening the workspace unless requested or required as a final verification gate after approved edits.
- **No Indefinite Retry Loops:** Never exceed 3 consecutive tool/terminal retries on a failing command—stop immediately and report the exact error logs.

### Research & "Gotchas" Rule (`docs\research\`)
- Whenever asked to perform research, audit code, or design an implementation plan before coding:
  1. Perform a **strictly read-only** inspection first (no source edits, builds, or git commits).
  2. Write the complete findings and implementation plan into a dedicated Markdown file inside `docs\research\` (e.g., `docs\research\RESEARCH_<topic>.md`) so it can be reviewed and copied easily.
  3. Explicitly include a **"Gotchas & Edge Cases"** section analyzing failure modes (such as 600 DPI memory spikes, PDF file locks, native runtime DLL loading, scanner margin offsets, die-cut bleed, and front/back mirror orientation) before writing implementation code.
  4. Wait for explicit user approval before executing code changes.

### Imaging, Memory & Resource Safety
- **Deterministic Disposal:** Always wrap `OpenCvSharp.Mat`, `SKBitmap`, `Stream`, `Graphics`, and temporary image buffers in `using` blocks. High-DPI (`600 DPI`) scans exceed `5000 × 7000` pixels (~150–250 MB per uncompressed buffer); dispose intermediate `Mat` instances immediately after each pipeline stage.
- **Zero/Negative Dimension Guards:** Validate `Width > 0 && Height > 0` before cropping, rotating, or encoding any image slice.

---

## 4. Safe Build, Versioning, Git Push & Detached Launch Protocol

### Manual Versioning & Commit Convention (`YYYY-MM-dd.#`)
- **Version Format:** Use the ISO date-based format `YYYY-MM-dd.#` (e.g., `2026-11-24.1` for November 24, 2026—never `YYYY-dd-MM.#` and never carrying over a previous calendar day's date).
- **Daily Reset Rule:** Always check the current system date (`Get-Date -Format 'yyyy-MM-dd'`). Whenever the date advances to a new calendar day, **reset the counter to `.1`** (`YYYY-MM-dd.1`). Only increment `.#` (`.2`, `.3`, ...) for multiple builds/releases on the **same** calendar date.
- **MSI 4-Part Mapping:** Map `YYYY-MM-dd.#` to Windows Installer's `Major.Minor.Build.Revision` (`Major < 256`, e.g., `26.11.24.1` for `2026-11-24.1`) while naming the `.msi` output **`PDF to Tiles-YYYY-MM-dd.#.msi`** (e.g., `PDF to Tiles-2026-11-24.1.msi`). Keep `UpgradeCode` `FF1FAC66-A55B-4F14-9C53-3F058C9742AB` unchanged.
- **Project Metadata & Window Title:** Store the current version string in `<InformationalVersion>` / `<Version>` in the main `.csproj` (using numeric fallback `YYYY.MM.dd.#` where MSBuild requires 4-part numeric assembly attributes). Main window title displays clean base title `PDF to Tiles` (or `PDF to Tiles - <FileName>` when a file is open).
- **Commit Message Prefix:** Always prefix commit messages with `<Version> - ` (e.g., `2026-11-24.1 - ...`).

### Pre-Build File-Lock Check (Prevents `MSB3027` / `MSB3021` Locked Binary Errors)
- Before running `dotnet build`, check `Get-Process` for any running instance of the compiled app, stop it cleanly, and wait 500ms so Windows releases file locks on `.exe` and `.dll` outputs:
  ```powershell
  Get-Process | Where-Object { $_.ProcessName -like "*PdfToTiles*" -or $_.ProcessName -like "*pdf-to-Tiles*" } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500


### WiX v5 MSI Installer Build & Packaging (`PdfToTilesInstaller`)
- **Installer Project:** `PdfToTilesInstaller\PdfToTilesInstaller.wixproj` (WiX Toolset SDK `5.0.2`, `WixToolset.UI.wixext`).
- **Build & Publish Command:**
  ```powershell
  dotnet build "PdfToTilesInstaller\PdfToTilesInstaller.wixproj" -c Release -p:Platform=x64
  ```
- **Output Artifact:** Built installer is output to `PdfToTilesInstaller\bin\x64\Release\PDF to Tiles-<version>.msi`.
- **Target Runtime Prerequisite:** .NET Desktop Runtime 8.0 (x64).
