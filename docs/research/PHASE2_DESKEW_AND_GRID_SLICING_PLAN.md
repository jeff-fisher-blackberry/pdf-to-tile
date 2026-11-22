# Phase 2 Implementation Plan: Deskew / Straighten & Configurable Grid Slicing

**Application:** PDF to Tiles (`C:\Users\jefff\source\repos\pdf-to-Tiles`)  
**Target Solution:** WPF Desktop Application (`net8.0-windows` x64, .NET 8.0 / C# 12)  
**Document Status:** Strictly Read-Only Architecture & Implementation Plan  
**Date:** November 22, 2026  
**Target Version for Phase 2:** `2026-22-11.2`  

---

## 1. Executive Summary & Objective

In Phase 1, `pdf-to-Tiles` established:
- Non-locking PDF page loading (`PdfPageRenderer` via in-memory stream sharing).
- Responsive UI preview generation (150 DPI) with thread-safe `BitmapSource` binding.
- Viewport interaction (Fit-to-Page auto-scaling, Actual Size 100%, incremental zoom).
- Direct high-DPI page rendering to lossless PNG (`300`, `450`, or `600` DPI with segment tiling).

**Phase 2 Objective:**  
Enable board wargamers and digital archivists to take a captured high-DPI counter sheet (or loaded counter image), automatically or manually detect and correct scan tilt (deskew), overlay a configurable row/column matrix (starting with Redmond A. Simonsen’s 1980 SPI classic *WorldKiller* — 10×10 front and back counter sheet), apply an inward bleed inset, map front/back chit orientations, and batch-export individual, perfectly registered `.png` playing pieces.

---

## 2. Existing Phase 1 Codebase & NuGet Evaluation

### Current Project Setup
- **Target Framework:** `net8.0-windows`
- **Nullable & Usings:** `Nullable=enable`, `ImplicitUsings=enable`, `UseWPF=true`
- **Installed Packages:**
  - `CommunityToolkit.Mvvm` (8.4.2)
  - `PDFtoImage` (5.4.0) — pulls in `SkiaSharp` (4.150.1) and native binaries for Win32/PDFium.

### Library Selection for Phase 2: OpenCvSharp4 vs. SkiaSharp
| Capability Required | SkiaSharp (Already Present) | OpenCvSharp4 (`OpenCvSharp4` + `runtime.win`) | Recommendation |
| :--- | :--- | :--- | :--- |
| **Tilt Detection (Hough Lines / Contours / Moments)** | Not native; requires writing custom edge/Radon transforms in pure C#. | Native, industry-standard: `Cv2.Canny`, `Cv2.HoughLinesP`, `Cv2.MinAreaRect`. | **OpenCvSharp4** |
| **High-Precision Image Rotation** | `SKCanvas.RotateDegrees` with cubic sampling. | `Cv2.GetRotationMatrix2D` + `Cv2.WarpAffine` with `InterpolationFlags.Lanczos4`. | **OpenCvSharp4** |
| **Sub-Pixel Grid ROI Cropping** | Supported via `SKBitmap.ExtractSubset`. | Supported via `new Mat(sourceMat, roiRect)` (zero-copy view). | **OpenCvSharp4** |
| **Memory Footprint & Cleanup** | `using (var bmp = ...)` | `using (var mat = ...)` deterministic C++ handle release. | **Both deterministic** |

**Conclusion:**  
Add NuGet packages:
```xml
<PackageReference Include="OpenCvSharp4" Version="4.10.0.20241108" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20241108" />
```
*Note:* OpenCvSharp provides high-speed zero-copy ROI views (`new Mat(parentMat, rect)`), allowing 100 counter sub-images to be cropped and encoded without duplicating the master 150 MB buffer 100 times.

---

## 3. Core Architecture & Pipeline Design

The Phase 2 architecture strictly decouples UI controls from image transformations:

```
[ Captured Page / Loaded Image ]
               │
               ▼
   [ DeskewService.DetectAngleAsync ] ───► Auto-detected angle (e.g., -0.75°)
               │
               ▼
   [ User Fine-Angle Override ] ─────────► Slider / Stepper (±0.1° step)
               │
               ▼
   [ DeskewService.RotateImage ] ────────► Lanczos4 Interpolation, Border Padding
               │
               ▼
   [ GridBoundsDetector & SheetProfile ] ► Outer Rect (excludes sheet margins/staples)
               │
               ▼
   [ GridSlicerService.SliceGridAsync ] ─► Rows × Cols + Inward Inset + Orientation Map
               │
               ▼
   [ Exported Piece PNGs ] ──────────────► Piece_R01_C01_Front.png, etc.
```

---

## 4. Component Specifications

### 4.1 Data Models & Sheet Profiles (`Models/`)

#### `SheetProfile.cs` (JSON-Serializable Presets)
```csharp
namespace pdf_to_Tiles.Models
{
    public class SheetProfile
    {
        public string Name { get; set; } = "SPI WorldKiller 1980 (10x10)";
        public int Rows { get; set; } = 10;
        public int Columns { get; set; } = 10;

        // Normalized relative coordinates (0.0 to 1.0) so profiles apply across 300/600 DPI
        public double CropLeftRatio { get; set; } = 0.05;
        public double CropTopRatio { get; set; } = 0.08;
        public double CropWidthRatio { get; set; } = 0.90;
        public double CropHeightRatio { get; set; } = 0.84;

        // Inward inset in millimeters or pixels (scaled to DPI) to prevent adjacent chit bleed
        public double InsetPixelsAt600Dpi { get; set; } = 4.0;

        // Orientation mapping for reverse side
        public BackOrientationMode BackOrientation { get; set; } = BackOrientationMode.HorizontalFlip;

        public string NamingFormat { get; set; } = "{Side}_R{Row:D2}_C{Col:D2}";
    }

    public enum BackOrientationMode
    {
        Direct,          // Back matches same Row and Column (rare)
        HorizontalFlip,  // Back flips left-to-right (common for book/fold sheets: Col = MaxCol - Col + 1)
        VerticalFlip,    // Back flips top-to-bottom (Row = MaxRow - Row + 1)
        Rotate180        // Back is rotated 180° relative to front
    }
}
```

### 4.2 Deskew & Rotation Service (`Services/DeskewService.cs`)

1. **Fast Downsampled Tilt Detection:**
   - Downscale the master image to ~1000px width (factor ~5× to 6× for a 600 DPI scan) to keep edge detection under 50ms.
   - Convert to Grayscale -> Apply Gaussian Blur (kernel 3×3) -> Canny edge detection (thresholds 50, 150).
   - Run `Cv2.HoughLinesP` to detect straight edge lines.
   - Filter lines by length (`length > 80px`) and angle (`|angle| < 10°` from horizontal or vertical).
   - Calculate the median angle of horizontal and vertical line sets.
   - If Hough lines are ambiguous (e.g. worn borders), calculate the Radon/projection-profile variance at `0.05°` increments between `[-3.0°, +3.0°]`.

2. **High-Quality Rotation (`RotateImage`):**
   - Given an angle $\theta$:
     ```csharp
     Point2f center = new Point2f(src.Width / 2f, src.Height / 2f);
     using Mat rotMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
     
     // Calculate new bounding rectangle if expanding canvas, or retain dimensions with border padding
     Mat dst = new Mat();
     Cv2.WarpAffine(src, dst, rotMatrix, src.Size(), 
                    InterpolationFlags.Lanczos4, 
                    BorderTypes.Constant, 
                    new Scalar(255, 255, 255, 255)); // Crisp white border
     return dst;
     ```

### 4.3 Grid Slicer Service (`Services/GridSlicerService.cs`)

1. **Absolute Coordinate Resolution:**
   - Given the deskewed master image dimensions (`Width`, `Height`):
     - `GridLeft = CropLeftRatio * Width`
     - `GridTop = CropTopRatio * Height`
     - `GridWidth = CropWidthRatio * Width`
     - `GridHeight = CropHeightRatio * Height`
     - `CellWidth = GridWidth / Columns`
     - `CellHeight = GridHeight / Rows`

2. **Sub-Pixel Inset & Cropping:**
   - For cell `(r, c)` where `r ∈ [0, Rows-1]`, `c ∈ [0, Columns-1]`:
     ```csharp
     int inset = (int)Math.Round(insetPixels);
     int cellX = (int)Math.Round(GridLeft + c * CellWidth) + inset;
     int cellY = (int)Math.Round(GridTop + r * CellHeight) + inset;
     int cellW = (int)Math.Round(CellWidth) - (2 * inset);
     int cellH = (int)Math.Round(CellHeight) - (2 * inset);

     // Safety guard against zero or negative dimensions
     if (cellW <= 0 || cellH <= 0) continue;

     Rect cellRoi = new Rect(cellX, cellY, cellW, cellH);
     using Mat cellMat = new Mat(deskewedMat, cellRoi); // Zero-copy sub-Mat
     Cv2.ImWrite(pieceFilePath, cellMat);
     ```

3. **Front/Back Index Resolution:**
   - If `Side == "Back"`:
     - `HorizontalFlip`: `effectiveCol = Columns - 1 - c`
     - `VerticalFlip`: `effectiveRow = Rows - 1 - r`
     - `Rotate180`: `effectiveRow = Rows - 1 - r`, `effectiveCol = Columns - 1 - c`
   - File output names match logically:
     - Front: `WorldKiller_R01_C01_Front.png`
     - Back: `WorldKiller_R01_C01_Back.png` (representing the physical reverse of the same counter).

---

## 5. UI Integration & Visual Interactive Overlay

### Tab / Mode Workflow inside `MainWindow.xaml`:
1. **Mode Selection (Navigation Bar):**
   - **Tab 1: PDF Viewer & Page Capture** (Phase 1, working)
   - **Tab 2: Deskew & Grid Slicer** (Phase 2)
     - Allows "Send Captured Page to Slicer" directly from Tab 1, or "Open Counter Image File..." (`.png`, `.tif`, `.jpg`).

2. **Deskew Toolbar:**
   - `Auto-Detect Tilt` button (computes angle in background).
   - Angle numeric input & slider: range `[-5.0°, +5.0°]` with `0.05°` resolution.
   - `[-0.1°]` and `[+0.1°]` nudging buttons.
   - `Reset Angle (0.0°)` button.
   - `Show Alignment Grid` toggle (draws fine horizontal/vertical reference crosshairs over the preview).

3. **Grid Configuration Controls:**
   - Preset selector: ComboBox (`SPI WorldKiller 10x10`, `Generic 10x10`, `Generic 12x15`, `Custom...`).
   - Inputs for `Rows` and `Columns`.
   - Grid bounds sliders/spinners: `Grid X`, `Grid Y`, `Grid Width`, `Grid Height`.
   - Inward Inset spinner: `0 to 20 px` (with real-time zoomed chit preview).
   - Side selector: `Front` vs. `Back` with Back Orientation mode dropdown.

4. **Interactive Grid Overlay on Canvas:**
   - In the preview viewport, draw a WPF `Canvas` overlaying the image.
   - Bounding rectangle rendered with dashed accent color (`#007ACC`) and draggable corners/edges.
   - Internal grid lines rendered with semi-transparent cyan/white guidelines.

5. **Batch Slice & Progress:**
   - "✂️ Slice Grid (100 Pieces)..." button.
   - Output folder picker dialog.
   - Modal/status progress bar (`Slicing piece 42 of 100...`).
   - Asynchronous background worker using `IProgress<SliceProgressReport>` and `CancellationToken`.

---

## 6. Comprehensive "Gotchas & Edge Cases" Analysis

### 6.1 600 DPI Unmanaged Memory Spikes & Leak Prevention
- **The Gotcha:** A 600 DPI counter sheet scan ($8.5 \times 11\text{ in}$) yields $\approx 5100 \times 6600$ pixels. A 32-bit BGRA buffer occupies **$\approx 135\text{ MB}$ per uncompressed `Mat` or bitmap**. If auto-deskew, manual rotation, and 100 sliced cells allocate independent full-size buffers, native memory can rapidly spike past **1.5–2.0 GB**, causing `OutOfMemoryException` or native heap exhaustion.
- **Mitigation & Guardrails:**
  - Downsample to a 1000px proxy buffer for edge detection and Hough line computations.
  - Apply rotation only to the master image once the user confirms the angle.
  - Slicing MUST use OpenCvSharp sub-matrix ROI views (`new Mat(masterDeskewedMat, cellRoi)`), which create a light descriptor referencing existing pixel pointers without duplicating uncompressed image data.
  - Every intermediate `Mat`, `SKBitmap`, and stream must be scoped in a `using` statement.

### 6.2 Scanner Border Creases, Staples & Margin Asymmetry
- **The Gotcha:** Real vintage wargame rulebooks and counter sheets (e.g. SPI 1980 *WorldKiller*) were staple-bound into magazines (*Ares* magazine or SPI flat-packs). Scans inevitably contain dark scanner-lid edges, staple holes, spine shadows, and publisher reference margins at the top/bottom. If an algorithm blindly calculates cell size as $\text{PageWidth} / 10$, pieces will be off-center and include staple shadows or cut marks.
- **Mitigation & Guardrails:**
  - Auto-deskew must crop out the outer 5% periphery before edge analysis so black scanner borders do not skew the Hough line angle.
  - The counter matrix MUST be defined by an independent `GridBounds` bounding box that isolates only the counter area, completely bypassing scanner borders and publisher margin text.

### 6.3 Die-Cut Line Bleed vs. Counter Art Clipping
- **The Gotcha:** Physical counter sheets have physical die-cut lines stamped through the cardboard. If pieces are cut exactly to the cell grid boundary:
  - Slight physical scan distortion causes black/white cut lines or slivers of neighbor units to appear on the piece edges.
  - Conversely, an excessive inset ($>8\text{ px}$) will clip unit combat factors, movement allowances, or border framing.
- **Mitigation & Guardrails:**
  - Provide a configurable inward pixel inset (`2–6 px` default at 600 DPI).
  - Include an interactive **"Single Piece Preview"** zoom box in the UI that displays a high-magnification sample of Cell $(0,0)$ and Cell $(5,5)$ with the cut border highlighted, allowing the user to tune the inset to the exact pixel before running the batch export.

### 6.4 Front vs. Back Printing Press Misregistration
- **The Gotcha:** In 1980, front and back sheets were printed in separate offset press runs. Front and back counter grids are rarely aligned to the exact millimeter on the sheet. If the exact same bounding box $(X, Y, W, H)$ is used for both Front and Back scans, the reverse side pieces will be visibly drifted.
- **Mitigation & Guardrails:**
  - Allow `GridBounds` to be calibrated independently for the Front sheet and the Back sheet, while preserving the logical row/column pairing.
  - Store profile adjustments as `FrontGridBounds` and `BackGridBounds` within the `SheetProfile`.

### 6.5 Zero or Inverted Cell Dimensions
- **The Gotcha:** If an invalid grid bounding box or an inset larger than the cell radius is entered (e.g. `CellWidth = 40px`, `Inset = 25px`), crop dimensions become negative, crashing OpenCV with native unhandled exceptions (`cv::Rect assertion failed`).
- **Mitigation & Guardrails:**
  - Enforce validation guards before any slicing call:
    `if (cellWidth <= 2 * inset || cellHeight <= 2 * inset) throw new ArgumentException(...)`
  - Disable the Slice button and display a warning banner if calculated cell dimensions are invalid.

### 6.6 UI Responsiveness During 100+ Piece Batch Slicing
- **The Gotcha:** Encoding and saving 100 lossless 600 DPI PNG files involves substantial disk I/O and PNG compression CPU work. Running this synchronously on the UI thread will freeze the application and trigger Windows "(Not Responding)" states.
- **Mitigation & Guardrails:**
  - Perform all slicing and file encoding on a background thread (`Task.Run`).
  - Provide an `IProgress<SliceProgressReport>` to update a smooth progress bar and status message.
  - Include a `CancellationTokenSource` and "Cancel" button so the user can stop batch slicing at any time.

---

## 7. Phased Implementation Roadmap for Phase 2

When approved to implement Phase 2, the work will proceed in clean, verifiable increments:

1. **Step 1: Dependency Integration & Service Scaffolding:**
   - Add `OpenCvSharp4` and `OpenCvSharp4.runtime.win` to `pdf-to-Tiles.csproj`.
   - Implement `Models/SheetProfile.cs` (with pre-configured preset for *WorldKiller* 10×10).
   - Implement `Services/DeskewService.cs` (angle detection + Lanczos4 rotation).
2. **Step 2: Grid Slicer Service:**
   - Implement `Services/GridSlicerService.cs` (sub-pixel ROI cropping, inset calculation, orientation mapping, PNG writing).
3. **Step 3: ViewModel & UI Overlay Integration:**
   - Expand `ViewModels/MainViewModel.cs` with deskew angle properties, grid coordinates, and batch slicing commands.
   - Update `MainWindow.xaml` with the visual interactive grid overlay, deskew controls, and zoomed piece preview.
4. **Step 4: Build, File-Lock Verification & Testing:**
   - Perform pre-build file-lock checks (`Get-Process`).
   - Run `dotnet build -c Debug` to confirm 0 warnings and 0 errors.
   - Bump version to `2026-22-11.2`.

---

## 8. Approval Gate

Per `AGENT.md` and user instructions, this plan is strictly read-only. No source files, project configs, or git repositories have been modified. Implementation will begin only upon explicit user approval.
