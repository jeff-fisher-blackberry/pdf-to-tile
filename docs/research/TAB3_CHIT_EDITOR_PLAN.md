# TAB3_CHIT_EDITOR_PLAN.md — Research & Implementation Plan for In-Memory Chit Editor (Tab 3)

## 1. Executive Summary & Objective

### 1.1 Goal
Extend **`PDF to Tiles`** with a dedicated third workspace tab: **`Chit Editor`** (positioned immediately to the right of **`Deskew & Grid Slicer`**). This tab provides a high-productivity, in-memory wargame counter refinement environment modeled after the battle-tested image manipulation workflows in **`GTL Text Editor`** (`image_b5e404.png`).

### 1.2 User Workflow Handoff
Currently, Tab 2 (`Deskew & Grid Slicer`) exports sliced pieces directly to disk via `OpenFolderDialog`. The new architecture introduces a seamless handoff:
1. In Tab 2, slicing a calibrated `2 × 10` band (or clicking **`Send to Chit Editor`**) slices all 20 individual chits directly **into an in-memory collection** (`ObservableCollection<ChitItem>`) without requiring immediate disk I/O.
2. The application automatically transitions to Tab 3 (**`Chit Editor`**).
3. The right-hand column presents a selectable **Tile Strip** (`#01`–`#20`) showing thumbnail previews, dimensions, row/column badges, and edit states.
4. The center viewport displays the active chit at **100% actual size** alongside a high-magnification zoomed inspection canvas (checkerboard background for transparency).
5. The left-hand panel provides full-resolution image manipulation tools ported and modernized from `GTL Text Editor`:
   - **Bevel:** 3D raised-edge gradient shading (depth `1..N` px) simulating vintage die-cut physical chits.
   - **Clip:** Corner notch/rounding trimming (`1..N` px) or inward border clipping.
   - **Clear:** Clearing border bleed/scanner noise (`1..N` px margin) to true transparency or a key color.
   - **Adjust (Resize / Stretch):** Exact `Width × Height` rescaling, canvas cropping/expansion, transparent color keying, and robust per-tile **Undo / Redo**.
   - **Batch & Export:** Apply adjustments across all 20 chits simultaneously, and export all 20 pieces to lossless PNGs.

---

## 2. Deconstruction of `GTL Text Editor` Reference Algorithms

An audit of `C:\Antigravity\GTL Text Editor\Read_GTL\` (`GTL Text Editor.cs`, `Form1.TileDisplay.cs`, `Bitmap_Utilities.cs`) reveals the exact mathematical and operational foundations of the tile editing suite:

### 2.1 Bevel Algorithm (`applyGradient`)
In `GTL Text Editor.cs` (lines 4761–4828):
- **Mechanism:** Constructs 4 non-overlapping trapezoidal gradient zones around the outer perimeter of the tile image using a user-specified `bordersize` parameter:
  - **Left Edge Trapezoid:** `(0, 0) -> (bordersize, bordersize) -> (bordersize, H - bordersize) -> (0, H)`.
    - Horizontal linear gradient from `Color.Beige` (highlight) to base pixel color.
  - **Top Edge Trapezoid:** `(0, 0) -> (bordersize, bordersize) -> (W - bordersize, bordersize) -> (W, 0)`.
    - Vertical linear gradient from `Color.Beige` (highlight) to base pixel color.
  - **Right Edge Trapezoid:** `(W, 0) -> (W - bordersize, bordersize) -> (W - bordersize, H - bordersize) -> (W, H)`.
    - Horizontal linear gradient from base pixel color to `Color.Black` (shadow).
  - **Bottom Edge Trapezoid:** `(0, H) -> (bordersize, H - bordersize) -> (W - bordersize, H - bordersize) -> (W, H)`.
    - Vertical linear gradient from base pixel color to `Color.Black` (shadow).
- **Visual Result:** Recreates directional lighting (top-left light source, bottom-right shadow) that gives flat digital scans the tactile 3D relief of physical punchboard counters.
- **Modern WPF / OpenCV Adaptation:**
  Rather than GDI+ software rendering which can degrade alpha channels, `pdf-to-Tiles` implements this in `ChitEditorService` using `OpenCvSharp4`:
  - Operates directly on a 4-channel `CV_8UC4` (BGRA) buffer.
  - Creates 4 linear gradient ramp masks for top, left, right, and bottom borders.
  - Highlights use additive or soft-light blending (`Vec4b` interpolation towards light cream/white `(220, 245, 245)` with configurable alpha).
  - Shadows use multiplicative blending (`Vec4b` interpolation towards deep shadow/black `(0, 0, 0)`).
  - Preserves existing graphic ink and transparency perfectly.

### 2.2 Clip Algorithm (`applyCorners` & Inward Trimming)
In `GTL Text Editor.cs` (lines 4845–4896):
- **Mechanism:** Clips or rounds corner pixels by drawing transparency lines across the corners based on `cornersize` (1..5 px):
  - At `cornersize = 1..3`, cuts 90-degree corner notches.
  - At `cornersize = 4..5`, cuts diagonal chamfers across `(1,1)`, `(W-2, 1)`, `(W-2, H-2)`, `(1, H-2)` using the active `pctTransparentColor`.
- **Modern WPF / OpenCV Adaptation:**
  In addition to corner chamfering, `pdf-to-Tiles` will support two clip modes:
  1. **Corner Clip / Chamfer:** Sets pixels within distance $R$ of each corner to transparent $(0,0,0,0)$.
  2. **Inward Edge Crop (Trim):** Uniformly cuts $N$ px from all 4 borders, updating chit dimensions from $(W, H)$ to $(W - 2N, H - 2N)$ with zero quality loss.

### 2.3 Clear Algorithm (`btnClear_Click`)
In `GTL Text Editor.cs` (lines 5446–5495):
- **Mechanism:** Sweeps through the outer border defined by `int buffer = Convert.ToInt16(cboClear.Text)`:
  ```csharp
  if ((X >= buffer && X < Width - buffer) && (Y >= buffer && Y < Height - buffer))
  {
      // Preserve original content
      c = sourceBitmap.GetPixel(X, Y);
  }
  else
  {
      // Clear outer perimeter to transparent color
      c = workingSmallTile.BackColor; // or TransparentColor
  }
  ```
- **Visual Result:** Erases ragged die-cut borders, misalignment fringe, and neighbor bleed from scanned sheets while leaving the interior artwork intact.
- **Modern WPF / OpenCV Adaptation:**
  - With OpenCV `Mat`, this is an ultra-fast vectorized ROI operation:
    ```csharp
    // Zero out (transparent) outer border
    var innerRoi = new Rect(buffer, buffer, mat.Width - (2 * buffer), mat.Height - (2 * buffer));
    // Any pixel outside innerRoi is filled with transparent color Scalar(0, 0, 0, 0)
    ```

### 2.4 Adjust: Resize & Stretch Algorithms
In `GTL Text Editor.cs` (lines 5305–5442):
- **Stretch (`btnStretch_Click`):** Resamples the image to the exact dimensions entered in `txtWidth` × `txtHeight` using bicubic/bilinear interpolation.
- **Resize (`btnResize_Click2`):** Crops or expands the canvas around the center:
  - If smaller: extracts center rectangle `startX = (Width - targetWidth) / 2`, `startY = (Height - targetHeight) / 2`.
  - If larger: centers the original artwork on an expanded canvas and fills the newly created border margin with `pctTransparentColor`.
- **Modern WPF / OpenCV Adaptation:**
  - **Stretch:** `Cv2.Resize(srcMat, dstMat, new Size(targetW, targetH), 0, 0, InterpolationFlags.Lanczos4)`.
  - **Resize:** `Cv2.CopyMakeBorder` with `BorderTypes.Constant` and transparent scalar for expansion, or `Mat(srcMat, roi).Clone()` for contraction.

### 2.5 Per-Tile Undo / Redo Architecture
In `GTL Text Editor.cs` (lines 956–1205):
- Maintains an explicit `List<TileSnapshot>` with `tileHistoryIndex` bounded to 50 levels.
- Each snapshot stores a cloned bitmap, width, height, and color state.
- Calling any destructive tool calls `PushTileActionSnapshot()`, pruning any redo branches ahead of `tileHistoryIndex`.
- `Undo` and `Redo` update the working image, dimensions, size label, and re-renders the zoom and working canvases.

---

## 3. Architecture & Data Flow in `pdf-to-Tiles`

```
┌────────────────────────────────────────────────────────────────────────┐
│                   TAB 2: DESKEW & GRID SLICER                          │
│                                                                        │
│  [ Calibrate 2×10 Section ] ──> Click [ Edit 20 Chits in Editor... ]   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                         In-Memory 20-Piece Slicing
                         (GridSlicerService.SliceBandedGridToMemoryAsync)
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        TAB 3: CHIT EDITOR                              │
│                                                                        │
│  ┌───────────────────────┐  ┌─────────────────────┐  ┌──────────────┐  │
│  │ LEFT CONTROLS PANEL   │  │ CENTER CANVAS       │  │ RIGHT STRIP  │  │
│  │ ───────────────────── │  │ ─────────────────── │  │ ──────────── │  │
│  │ • Width × Height      │  │ • 100% Actual Size  │  │ • #01 .. #20 │  │
│  │ • Stretch / Resize    │  │ • High-Mag Zoom     │  │ • Thumbnail  │  │
│  │ • Undo / Redo         │  │   Pixel Inspection  │  │ • Dimensions │  │
│  │ • Bevel (1..10 px)    │  │ • Checkerboard BG   │  │ • Dirty /    │  │
│  │ • Clip (1..10 px)     │  │   (Transparency)    │  │   Edit State │  │
│  │ • Clear (1..10 px)    │  └─────────────────────┘  └──────────────┘  │
│  │ • Transparent Color   │                                             │
│  │ • Batch Apply to All  │                                             │
│  │ • Export 20 to PNG    │                                             │
│  └───────────────────────┘                                             │
└────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Data Model: `ChitItem`
Located in `pdf-to-Tiles/Models/ChitItem.cs`:
```csharp
public class ChitItem : ObservableObject, IDisposable
{
    public int Index { get; set; }               // 1..20
    public int Row { get; set; }                 // 1..2
    public int Col { get; set; }                 // 1..10
    public string Side { get; set; }             // "Front" or "Back"
    public string PieceName { get; set; }        // e.g. "WorldKiller_R01_C01_Front.png"
    
    // Core image buffers (4-channel BGRA)
    public Mat OriginalMat { get; set; }         // Baseline slice for reset
    public Mat WorkingMat { get; private set; }  // Active editable buffer
    
    // UI reactive properties
    [ObservableProperty] private BitmapSource _previewSource;
    [ObservableProperty] private BitmapSource _thumbnailSource;
    [ObservableProperty] private int _pixelWidth;
    [ObservableProperty] private int _pixelHeight;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSelected;

    // Per-chit Undo / Redo history
    public Stack<ChitSnapshot> UndoStack { get; } = new();
    public Stack<ChitSnapshot> RedoStack { get; } = new();
}
```

### 3.2 Service Layer: `ChitEditorService`
Located in `pdf-to-Tiles/Services/ChitEditorService.cs`:
- `ApplyBevel(Mat src, int borderSize, Color highlightColor, Color shadowColor) -> Mat`
- `ApplyCornerClip(Mat src, int cornerSize, Scalar transparentKey) -> Mat`
- `ApplyBorderTrim(Mat src, int trimPx) -> Mat`
- `ApplyBorderClear(Mat src, int marginPx, Scalar transparentKey) -> Mat`
- `ApplyResize(Mat src, int targetW, int targetH, Scalar fillKey) -> Mat`
- `ApplyStretch(Mat src, int targetW, int targetH, InterpolationFlags flags) -> Mat`

---

## 4. UI Layout & Visual Specifications (Tab 3)

The Tab 3 UI integrates cleanly into the existing WPF window using the established **Light Theme** palette and `{DynamicResource ...}` brush references.

### 4.1 Grid Hierarchy
A 3-column layout inside Tab 3:
- **Column 0 (Width: 320 px, Fixed):** Left Controls Panel inside a `ScrollViewer`.
- **Column 1 (Width: `*`):** Center Working Chit Viewport & Zoom Inspection.
- **Column 2 (Width: 260 px, Fixed):** Right Chit List / Strip (`#01`–`#20`).

### 4.2 Left Controls Panel Cards
1. **`TILE TOOLS (WORKING AREA)` Card:**
   - **Dimensions Row:** `Width` textbox $\times$ `Height` textbox, accompanied by square **`Undo`** (`\uE7A7`) and **`Redo`** (`\uE7A6`) glyph buttons.
   - **Transform Actions:** `[Stretch]` and `[Resize]` buttons.
   - **Clip Row:** ComboBox (`1..10 px`, default `2 px`) + `[Clip]` button.
   - **Bevel Row:** ComboBox (`1..10 px`, default `4 px`) + `[Bevel]` button.
   - **Clear Row:** ComboBox (`1..10 px`, default `1 px`) + `[Clear]` button.
   - **Transparent Color Row:** Swatch button (showing active transparent color) + `Transparent Color` label + Eyedropper toggle button.
2. **`BATCH ACTIONS & EXPORT` Card:**
   - `[Apply Current Action to All 20 Chits]` checkbox or action button.
   - `[Reset Chit to Original]` and `[Reset All 20 Chits]`.
   - `[📸 Export Selected Chit to PNG...]`.
   - **`[✂ Export All 20 Chits to PNG...]`** (primary accent button).

### 4.3 Center Working Chit Canvas
- **Checkerboard Background:** Standard 16×16 px alternating `#FFFFFF` and `#E5E7EB` squares to clearly visualize alpha transparency.
- **Dual Display:**
  - **Upper View:** 100% Actual Size display with border guidelines.
  - **Lower View:** Zoomed inspection view (`400%` / `800%` nearest-neighbor magnification) with an optional 1 px pixel grid to verify individual pixel bevels and transparency sweeps.

### 4.4 Right Chit List / Strip
- Vertical `ListBox` bound to `ObservableCollection<ChitItem>` with virtualized stack panel.
- Item Template:
  - Blue accent selection background for the active chit (matching `GTL Text Editor`).
  - Chit thumbnail (`64 × 64 px` uniform).
  - Metadata column:
    - Piece number (`#01` to `#20`)
    - Coordinates (`R01_C01` ... `R02_C10`)
    - Dimensions (`284 × 284 px`)
    - Orange `● Edited` badge if modified.

---

## 5. Mandatory "Gotchas & Edge Cases" Check

1. **Native Memory Leaks with 20 High-DPI Chits & Undo Stacks:**
   - *Risk:* At 600 DPI, each chit is $\approx 600 \times 600$ px $\times$ 4 bytes (BGRA) $\approx 1.44\text{ MB}$. With 20 chits and 50 undo levels, memory could exceed $1.44\text{ GB}$.
   - *Mitigation:* Cap undo depth per chit to **15 snapshots**. When pushing new snapshots or pruning redo branches, call `.Dispose()` immediately on old `Mat` instances. Implement `IDisposable` on `ChitItem` and flush all unmanaged buffers whenever a new band is sliced or the application exits.
2. **Color Space & Channel Ordering (BGRA vs. RGBA):**
   - *Risk:* OpenCV default is BGR/BGRA. WPF `BitmapSource` can misinterpret channels if constructed with standard RGB, leading to inverted blue/red faces.
   - *Mitigation:* Standardize all internal chit buffers on `MatType.CV_8UC4` (`BGRA`). Use `BitmapSource.Create` with `PixelFormats.Bgra32` and `stride = width * 4`.
3. **Zero / Sub-Pixel Dimension Collapse:**
   - *Risk:* Successive `Clip` or `Resize` operations could shrink a chit down to $\le 0$ px, crashing OpenCV matrix constructors (`Width > 0 && Height > 0` violation).
   - *Mitigation:* Hard enforce minimum dimension clamp: `targetWidth = Math.Max(8, targetWidth)` and `targetHeight = Math.Max(8, targetHeight)`. Validate before every transform.
4. **Strict Workspace Isolation:**
   - *Risk:* Accidentally altering or creating files in the sibling `C:\Antigravity\GTL Text Editor\` repository.
   - *Mitigation:* Strictly read-only reference to `GTL Text Editor`. All new code and documentation will reside exclusively inside `pdf-to-Tiles/`.

---

## 6. Phased Implementation Roadmap

- [ ] **Step 1: Models & Chit Editor Service (`Models/ChitItem.cs`, `Services/ChitEditorService.cs`):**
  - Implement `ChitItem` and `ChitSnapshot` with deterministic `using`/`Dispose()` disposal.
  - Implement `ChitEditorService` with OpenCV-accelerated `Bevel`, `Clip`, `Clear`, `Resize`, `Stretch`, and transparency keying.
  - Add `SliceBandedGridToMemoryAsync` to `GridSlicerService` returning `List<ChitItem>`.

- [ ] **Step 2: Tab Navigation & ViewModel Integration (`ViewModels/MainViewModel.cs`):**
  - Add `SelectedTabIndex = 2` for Tab 3 (`Chit Editor`).
  - Add `ObservableCollection<ChitItem> LoadedChits` and `ChitItem? ActiveChit`.
  - Add commands: `SliceAndEditActiveBandCommand`, `UndoChitCommand`, `RedoChitCommand`, `ApplyBevelCommand`, `ApplyClipCommand`, `ApplyClearCommand`, `ApplyResizeCommand`, `ApplyStretchCommand`, `ExportAllEditedChitsCommand`.

- [ ] **Step 3: Tab 3 UI Implementation (`MainWindow.xaml`):**
  - Add Tab 3 header: `[✂ Chit Editor]`.
  - Build 3-column layout: Left Controls Panel (Cards matching Light Theme), Center Working/Zoom Canvas, Right Tile Strip ListBox.

- [ ] **Step 4: Quality & Verification Gate:**
  - Build with `dotnet build -c Debug`, verifying `0 Warning(s), 0 Error(s)`.
  - Test round-trip: Slice a 2×10 band in Tab 2 &rarr; transition to Tab 3 &rarr; edit chits &rarr; export to PNG.
