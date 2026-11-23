# CHIT_EDGE_CLEANUP_PLAN.md — Research & Implementation Plan for Color-Matched Edge Cleanup (Tab 3)

## 1. Executive Summary & Objective

### 1.1 Goal
Extend **Tab 3 (`Chit Editor`)** in **`pdf-to-Tiles`** with a **Color-Matched Edge Cleanup** system. This allows wargame counter designers and digital archivists to heal cut marks, die-cut border nicks, scanner edge bleed, and registration tick marks along chit perimeters by replacing offending pixels with a matching counter background color—while rigorously preserving valid artwork, numbers, symbols, and alpha transparency (corner clips).

### 1.2 The Physical Problem in Wargame Counter Slicing
Vintage boardgame counter sheets (such as SPI's 1980 *WorldKiller*, Avalon Hill, GDW, and TSR punchboards) feature dense matrices of die-cut counters separated by black crop lines, guide ticks, or bleed borders.
- When slicing a `2 × 10` band into individual chits, slight scanning skew, printing plate drift, or die-cut variations often cause a 1- to 3-pixel sliver of dark die-cut ink or scanner margin to appear along one or more edges.
- While the existing **`Clear`** tool (ported from `GTL Text Editor`) wipes the outer border margin to pure transparency (`0, 0, 0, 0`), this is undesirable for solid physical-look counters: modern game engines (Vassal, Tabletop Simulator, Cyberboard) and print-and-play workflows expect solid counter edges.
- **Edge Cleanup** solves this by intelligently healing the edge: detecting the true background color of the counter, identifying anomalous edge pixels that deviate from that background color, and filling or inpainting them smoothly without eroding inner artwork or numbers.

```
+------------------------------------+
|  [Cut Mark]  ####################  |  <-- Outer 1-3 px margin
|  ##########  COUNTER ART  #######  |
|  ##########   (Numbers)   #######  |
|  ################################  |
+------------------------------------+
                 |
                 v [Color-Matched Edge Cleanup]
+------------------------------------+
|  [Healed]    ####################  |  <-- Clean background color
|  ##########  COUNTER ART  #######  |
|  ##########   (Numbers)   #######  |
|  ################################  |
+------------------------------------+
```

---

## 2. Technical Architecture & Algorithm Design

### 2.1 Background Color Auto-Sampling Algorithm

To replace defect pixels, the system must first know what the counter's background color actually is. Because counters contain black unit names, combat values, and white weapon icons, a naive average/mean produces a muddy, incorrect gray.

#### Modal Histogram Sampling (Dominant Perimeter Color)
We sample an inner sampling band situated just inside the outer edge margin:
- Let $D$ be the user-configured edge depth (e.g., $1 \le D \le 10$ px, default 2 px).
- The sampling region is an inner band located at inset $D + 1$ to $D + 4$ pixels from the four boundaries.
- **Color Quantization:** Each 8-bit channel (B, G, R) is quantized into 32 levels (5-bit: `val >> 3`) to cluster minor scanner sensor noise.
- **Histogram & Mode Selection:**
  - Build a 3D frequency histogram of the quantized colors for all non-transparent pixels (`A > 0`).
  - Find the bin with the highest frequency count (the statistical mode).
  - Compute the average B, G, R values of the original unquantized pixels that fell into this modal bin.
  - Return this as the detected `Vec4b(B, G, R, 255)` background color.
- **Result:** Because text and silhouettes occupy a minority of pixels along any counter band, the modal algorithm reliably isolates the true paper/ink background (e.g., SPI red `RGB(220, 42, 34)` or tan `RGB(214, 196, 162)`) with >99% accuracy.

#### Manual Override & Eyedropper
- The UI exposes a **Color Swatch** displaying the detected background color.
- An **"Auto"** button recalculates the modal color on demand.
- Clicking the swatch opens a color dialog or cycles through common palette swatches.
- An **Eyedropper tool / Click-to-Sample** mode allows the user to click any pixel in the 100% or zoomed chit preview to explicitly select the background color.

---

### 2.2 Defect Mask Generation & Color Distance Metric

Within the outer edge band ($x < D$, $x \ge W - D$, $y < D$, or $y \ge H - D$):
1. **Alpha Preservation Guard:**
   - If a pixel is fully transparent ($A = 0$), it is skipped (unless the user explicitly unchecks "Preserve Transparent Corners"). This ensures previously applied **`Clip`** corner notches or chamfers are never overwritten.
2. **Color Distance Calculation:**
   - Compare the pixel color $(B, G, R)$ against the target background color $(B_{bg}, G_{bg}, R_{bg})$.
   - Compute the perceptual Euclidean distance:
     $$\Delta E = \sqrt{2(R - R_{bg})^2 + 4(G - G_{bg})^2 + 3(B - B_{bg})^2}$$
   - Alternatively, standard Euclidean distance:
     $$\Delta E = \sqrt{(B - B_{bg})^2 + (G - G_{bg})^2 + (R - R_{bg})^2}$$
3. **Defect Thresholding:**
   - If $\Delta E > \text{Tolerance}$ (configurable `10..150`, default `35`), mark the pixel as a defect in a binary `Mat mask` (`CV_8UC1`).
   - If $\Delta E \le \text{Tolerance}$, the pixel is already consistent with the background color and remains completely untouched.

---

### 2.3 Healing Strategies (`ChitEditorService`)

We design three distinct healing modes to suit different counter types and scanning qualities:

#### Mode 1: Targeted Color Replace (Default — Fast & Crisp)
- **Algorithm:** For every pixel where `mask == 255`, replace its B, G, R channels directly with $(B_{bg}, G_{bg}, R_{bg})$ while retaining $A = 255$.
- **Best For:** High-contrast cut marks (black cut lines or white scanner slivers) on flat-colored counters.
- **Performance:** Instantaneous vectorized or pointer-indexed operation (< 1 ms per chit).

#### Mode 2: Smart Inpaint (`Cv2.Inpaint` — Telea Seamless Fill)
- **Algorithm:**
  - Create a 1-channel mask of all defect pixels within the edge band.
  - Split the 4-channel BGRA matrix into a 3-channel BGR image and a 1-channel Alpha mask.
  - Execute OpenCV's Fast Marching inpainting:
    ```csharp
    Cv2.Inpaint(bgrMat, defectMask, inpaintedBgr, inpaintRadius: 3, InpaintMethod.Telea);
    ```
  - Re-merge the inpainted BGR with the original Alpha channel.
- **Best For:** Paper scans with textured fibers, subtle printing halftone dot patterns, or lighting gradients across the sheet.
- **Advantage:** Blends surrounding background variations into the cut-mark region without leaving flat, artificial solid-color bands.

#### Mode 3: Solid Edge Flood (Uniform Perimeter Fill)
- **Algorithm:** Replaces all pixels within the outer $D$ margin with the background color (equivalent to `Clear`, but writing solid background color instead of transparency).
- **Best For:** Chits with widespread edge fraying where the outer 1–2 pixels contain no valid counter text or artwork.

---

## 3. UI / UX Design in Tab 3 (`Chit Editor`)

### 3.1 Integration in Left Tools Panel (`MainWindow.xaml`)
In `MainWindow.xaml`, inside the `TILE TOOLS (WORKING AREA)` card, add a dedicated row directly below `Row 3 (Clear)`:

```
+-------------------------------------------------------------+
| TILE TOOLS (WORKING AREA)                                   |
| [ 50 ] x [ 50 ]   [↩] [↪]               [ 2 v ] [ Clip   ]  |
| [ Stretch     ]   [ Resize ]            [ 4 v ] [ Bevel  ]  |
| [■] Transparent Color                   [ 1 v ] [ Clear  ]  |
|-------------------------------------------------------------|
| EDGE CLEANUP                                                |
| [■ Red] Auto   Depth: [ 2 v ] px   Tol: [ 35 ]   [ Clean ]  |
| Mode: (o) Replace  ( ) Inpaint  ( ) Flood                   |
| [x] Preserve Transparent Corners                            |
+-------------------------------------------------------------+
```

### 3.2 Controls & ViewModel Bindings (`MainViewModel.cs`)
1. **Background Color Swatch & Auto-Sample Button:**
   - `ChitEdgeCleanColor` (`System.Windows.Media.Color`, default auto-sampled).
   - `AutoSampleEdgeColorCommand`: Analyzes `SelectedChit.WorkingMat` and sets `ChitEdgeCleanColor`.
2. **Depth Selector:**
   - `ChitEdgeCleanDepth` (`int`, default `2`, range `1..10`).
   - `AvailableEdgeCleanDepths`: `[1, 2, 3, 4, 5, 6, 8, 10]`.
3. **Tolerance Slider / Input:**
   - `ChitEdgeCleanTolerance` (`int`, default `35`, range `10..150`).
4. **Clean Mode Selector:**
   - `ChitEdgeCleanMode` (`EdgeCleanMode`: `TargetedReplace`, `SmartInpaint`, `SolidFlood`).
5. **Preserve Alpha Checkbox:**
   - `PreserveTransparentCorners` (`bool`, default `true`).
6. **Action Command:**
   - `ApplyEdgeCleanCommand`: Executes cleanup on active chit (or all 20 chits if `ApplyToAllChits` is checked).

---

## 4. Multi-Chit Batch Processing & Safety Rules

### 4.1 Critical Batch Gotcha: Heterogeneous Counter Sheets
As seen in SPI *WorldKiller* (Section 1), a single `2 × 10` band often contains **different unit colors** (e.g., Red front-line strike cruisers in row 1, Tan support units or White damage markers in row 2):
- **Danger:** If `ApplyToAllChits` is active and the tool used a static global background color, running cleanup would spray Red background into Tan or White chits!
- **Mandatory Batch Rule:** When applying across all 20 chits, the service **must auto-sample each chit's background color dynamically per chit**, unless the user explicitly checks an "Override All with Selected Color" lock.

---

## 5. Gotchas & Edge Cases Analysis

| Risk / Edge Case | Impact | Mitigation Strategy |
| :--- | :--- | :--- |
| **Combat values or unit numbers near bottom edge** | Setting depth $> 3$ px could erode the bottom of numbers (e.g. `444-2` in WorldKiller). | Default depth to conservative `2 px`. The color distance tolerance prevents clean background from wiping text unless depth directly overlaps the number. Provide instant Undo/Redo. |
| **Halftone print grain / paper texture** | Solid replacement creates flat 2px bars that stand out against grainy scanned paper. | Provide `SmartInpaint` (Telea) mode which samples texture and gradients from immediately adjacent pixels. |
| **Clipped corners wiped by edge clean** | If the user previously applied `Clip` to round corners, edge cleanup might fill corners back in. | Strict `PreserveTransparentCorners` flag (default `true`): checks $A > 0$ before evaluating or replacing any pixel. |
| **Native OpenCV Memory Leaks** | Allocating intermediate matrices for masks, channel splits, or inpainting could leak unmanaged RAM. | Wrap all intermediate `Mat`, `Mat[]`, and `Scalar` objects in deterministic `using` blocks. High-DPI scans are guarded against memory bloat. |
| **Channel Order (`BGRA` vs `RGBA`)** | OpenCV `CV_8UC4` natively orders bytes as `B, G, R, A`. Mismatched indices would sample red as blue. | Use `indexer[y, x].Item0` for Blue, `Item1` for Green, `Item2` for Red, `Item3` for Alpha consistently with `ChitEditorService`. |
| **Zero/Negative Dimensions** | Corrupt or invalid matrices passed to service. | Enforce `src.Width >= 8 && src.Height >= 8` guard at the top of all new methods. |

---

## 6. Verification Plan

### 6.1 Automated Compilation & Process Lock Check
- Pre-build check:
  ```powershell
  Get-Process | Where-Object { $_.ProcessName -like "*PdfToTiles*" -or $_.ProcessName -like "*pdf-to-Tiles*" } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  dotnet build -c Debug
  ```
- Must succeed with `0 Warning(s)` and `0 Error(s)`.

### 6.2 Manual Functional Verification
1. Load *WorldKiller* counter sheet into Tab 2, position `2 × 10` band over Row 1 & 2, and click **`Send 2×10 Band to Chit Editor`**.
2. In Tab 3, select a chit with visible black cut lines along its edge.
3. Test **Auto-Sample**: Confirm the color swatch accurately captures the red counter background.
4. Set Depth = `2 px`, Tolerance = `35`, Mode = `TargetedReplace`, and click **`Clean`**.
5. Inspect the Zoomed Inspection canvas at `200%` and `400%`: Verify the black cut-mark pixels are gone, the background color matches seamlessly, and no interior artwork or text was altered.
6. Test **Undo / Redo** (`↩` / `↪`): Verify single-click restore and reapply.
7. Test **`Apply edits to all 20 chits`**: Verify that different colored chits in the band dynamically detect and heal using their own correct background colors.
