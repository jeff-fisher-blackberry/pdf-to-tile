# Research & Implementation Plan: 2×10 Band & Variable-Width Grid Slicing

**Application:** PDF to Tiles (`pdf-to-Tiles`)  
**Target Solution:** WPF Desktop Application (`net8.0-windows` x64, .NET 8.0 / C# 12)  
**Status:** Strictly Read-Only Architecture & Implementation Plan  
**Date:** November 22, 2026  
**Target Version for Next Release:** `2026-22-11.3`  
**Reference Asset:** `media_1795363026102_1de52ba7.jpg` (*WorldKiller* Counter Section Nr. 1 Front Scan)

---

## 1. Executive Summary & Problem Diagnosis

In Phase 2, `pdf-to-Tiles` established:
- Scan tilt auto-detection via probabilistic Hough lines and Lanczos4 rotation.
- A single continuous bounding box defined by `[Left, Top, Width, Height]`.
- Even division of that single box into `Rows × Columns` (e.g. 10 × 10).
- Zero-copy sub-matrix extraction (`new Mat(parentMat, roi)`).
- Crisp high-contrast Light Theme and maximized startup state.

### The Problem Shown in Real Scans (Reference: `media_1795363026102_1de52ba7.jpg`)
Inspection of the actual Redmond A. Simonsen 1980 SPI *WorldKiller* counter sheet reveals that assuming a **single continuous 10×10 uniform grid** fails for real-world wargame counter sheets:

1. **Discrete 2×10 Bands Separated by Headers & Gaps:**
   The 100 counters on *WorldKiller* are physically manufactured as **five discrete horizontal bands of 2 rows × 10 columns**:
   - **Band 1 (Red Intruders):** 2 rows × 10 columns = 20 counters (Units 701–801 on Row 1; 802–906 on Row 2).
   - **Band 2 (White Intruders & Shields):** 2 rows × 10 columns = 20 counters (Left vertical "INTRUDERS" label, Shield A/B/C, Damage 1s).
   - **Header Gap 1:** A wide horizontal separator containing the bold header banner **"DAMAGE MARKERS"** (~25–40 px high at 300 DPI, ~50–80 px at 600 DPI).
   - **Band 3 (White/Beige Damage Markers):** 2 rows × 10 columns = 20 counters (Damage 3s on Row 5, Damage 5s on Row 6).
   - **Divider Gap 2:** A horizontal spacing line.
   - **Band 4 (Damage Markers & Planetary Forces):** 2 rows × 10 columns = 20 counters (Damage 7s/9s/11s and blue planetary units 401–403 / 504–506).
   - **Header Gap 3:** A header text banner **"PLANETARY FORCES"** above the bottom section.
   - **Band 5 (Blue Planetary Forces):** 2 rows × 10 columns = 20 counters (Units 201–207 / 301–303 on Row 9; 408–506 on Row 10).
   - **Bottom Sheet Margin:** Publisher copyright footer.

   When a single continuous bounding box is applied over the whole sheet and divided by 10 rows:
   - Rows 1–4 partially align, but the "DAMAGE MARKERS" header pushes Rows 5–6 downward.
   - The slicing algorithm cuts directly through the words "DAMAGE MARKERS", corrupting the counter images.
   - By Row 9 and 10, the cumulative error is massive, slicing half of one counter and half of another.

2. **Non-Uniform Individual Column Widths Within a Fixed Total 10-Column Width:**
   While the **total width across all 10 columns** is tightly bounded by the sheet's outer margins:
   - Due to 1980 mechanical film strip compositing and die-cutting rules, individual counter widths are not always mathematically identical down to the pixel. For example, column 1 may be 298 px, column 2 may be 304 px, column 3 may be 301 px.
   - A naive `Width / 10.0` column calculation drifts slightly across columns 3–8, causing die-cut lines or adjacent chit numbers to clip into counter faces.
   - **Requirement:** The total outer width across the 10 columns must remain firmly anchored, while allowing internal column divider lines to be nudged or fine-tuned individually.

---

## 2. Audit of Existing Codebase & Architecture

### 2.1 Existing Model: `SheetProfile.cs`
- `GridRelativeBounds` (`Left`, `Top`, `Width`, `Height` from 0.0 to 1.0) represents only a single box for the entire sheet.
- `Rows` (10) and `Columns` (10) assume uniform spacing:
  $$\text{cellW} = \frac{\text{gridW}}{\text{Columns}}, \quad \text{cellH} = \frac{\text{gridH}}{\text{Rows}}$$
- There is no concept of multiple bands, inter-band gaps, or non-uniform column dividers.

### 2.2 Existing Slicer Service: `GridSlicerService.cs`
- Line 60: Computes cell positions with a simple linear loop:
  $$\text{cellX} = \text{gridX} + c \cdot \text{cellW} + \text{inset}$$
  $$\text{cellY} = \text{gridY} + r \cdot \text{cellH} + \text{inset}$$
- Zero-copy extraction (`new Mat(masterMat, roi)`) is already working and highly performant.
- Front/Back coordinate mapping (`HorizontalFlip`, `VerticalFlip`, `Rotate180`, `Direct`) is implemented for uniform $R \times C$, but needs to map seamlessly across discrete bands.

### 2.3 Existing UI & Overlay: `MainWindow.xaml` / `MainWindow.xaml.cs`
- Currently draws a single rectangle and uniform vertical/horizontal grid lines on `SlicerGridCanvas`.
- The single chit preview only extracts Cell (0,0) of the whole sheet.
- Sliders control single sheet-level X, Y, Width, and Height percentages.

---

## 3. Architecture for 2×10 Band Slicing & Total-Width Anchoring

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SHEET PROFILE (WorldKiller)                        │
│                                                                             │
│  Slicing Mode: Banded2x10                                                   │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Band 1 (Rows 1-2, Cols 1-10): Red Intruders                           │  │
│  │ [Left, Top, Width, Height] + [9 Column Dividers d1..d9] + RowSplit    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Band 2 (Rows 3-4, Cols 1-10): White Intruders & Shields               │  │
│  │ [Left, Top, Width, Height] + [9 Column Dividers d1..d9] + RowSplit    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│   === [ SKIPPED HEADER GAP: "DAMAGE MARKERS" ] =========================    │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Band 3 (Rows 5-6, Cols 1-10): Damage Markers 3 & 5                    │  │
│  │ [Left, Top, Width, Height] + [9 Column Dividers d1..d9] + RowSplit    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│   === [ SKIPPED SEPARATOR GAP ] ========================================    │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Band 4 (Rows 7-8, Cols 1-10): Damage Markers 7-11 & Blue Units        │  │
│  │ [Left, Top, Width, Height] + [9 Column Dividers d1..d9] + RowSplit    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│   === [ SKIPPED HEADER GAP: "PLANETARY FORCES" ] =======================    │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Band 5 (Rows 9-10, Cols 1-10): Blue Planetary Forces                  │  │
│  │ [Left, Top, Width, Height] + [9 Column Dividers d1..d9] + RowSplit    │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Data Model Specifications (`Models/`)

#### 1. Slicing Mode Enum
```csharp
namespace pdf_to_Tiles.Models
{
    public enum GridSlicingMode
    {
        /// <summary>
        /// Default: Slices 2 rows x 10 columns across discrete horizontal bands,
        /// skipping header bars and allowing fine-tuned column dividers within a fixed total width.
        /// </summary>
        Banded2x10,

        /// <summary>
        /// Single continuous rectangular grid of N rows x M columns (legacy / generic sheets).
        /// </summary>
        UniformGrid
    }
}
```

#### 2. `Band2x10Definition.cs`
```csharp
namespace pdf_to_Tiles.Models
{
    public class Band2x10Definition
    {
        public string Name { get; set; } = "Band 1";

        /// <summary>
        /// Normalized outer bounding box (0.0 to 1.0) of the entire 2x10 band.
        /// Anchors the total width across all 10 columns and total height of both rows.
        /// </summary>
        public GridRelativeBounds Bounds { get; set; } = new GridRelativeBounds
        {
            Left = 0.05,
            Top = 0.08,
            Width = 0.90,
            Height = 0.14
        };

        /// <summary>
        /// Vertical split ratio between Row 1 and Row 2 inside this band (default 0.50 = 50% split).
        /// </summary>
        public double RowSplitRatio { get; set; } = 0.50;

        /// <summary>
        /// Relative column divider positions (0.0 to 1.0 within the band width).
        /// For 10 columns, contains exactly 9 dividers: [0.10, 0.20, ..., 0.90].
        /// Allows individual column widths to vary while the outer left and right boundaries remain fixed.
        /// </summary>
        public List<double> ColumnDividers { get; set; } = GetDefaultEvenDividers(10);

        public static List<double> GetDefaultEvenDividers(int columnCount = 10)
        {
            var dividers = new List<double>();
            for (int i = 1; i < columnCount; i++)
            {
                dividers.Add(Math.Round(i / (double)columnCount, 4));
            }
            return dividers;
        }

        public Band2x10Definition Clone()
        {
            return new Band2x10Definition
            {
                Name = Name,
                Bounds = Bounds.Clone(),
                RowSplitRatio = RowSplitRatio,
                ColumnDividers = new List<double>(ColumnDividers)
            };
        }
    }
}
```

#### 3. Updated `SheetProfile.cs` (Backward Compatible)
```csharp
public class SheetProfile
{
    public string Name { get; set; } = "SPI WorldKiller 1980 (10x10)";
    public GridSlicingMode SlicingMode { get; set; } = GridSlicingMode.Banded2x10;

    // Legacy Uniform Grid fields (preserved for generic sheets)
    public int Rows { get; set; } = 10;
    public int Columns { get; set; } = 10;
    public GridRelativeBounds FrontBounds { get; set; } = new GridRelativeBounds();
    public GridRelativeBounds BackBounds { get; set; } = new GridRelativeBounds();

    // 2x10 Band Slicing Lists (supports independent Front and Back press alignment)
    public List<Band2x10Definition> FrontBands { get; set; } = new();
    public List<Band2x10Definition> BackBands { get; set; } = new();

    public double InwardInsetPx { get; set; } = 4.0;
    public BackOrientationMode BackOrientation { get; set; } = BackOrientationMode.HorizontalFlip;
    public string NamingPrefix { get; set; } = "WorldKiller";
    public string NamingFormat { get; set; } = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}";
}
```

---

## 4. Total-Width Anchoring & Variable Column Slicing Math

### 4.1 Fixed Total Width vs. Internal Dividers
Let the deskewed master image have dimensions $(W_{\text{img}}, H_{\text{img}})$.  
For a given band with `Bounds = (Left, Top, Width, Height)`:
- Outer pixel coordinates:
  $$X_{\text{start}} = \text{round}(\text{Left} \cdot W_{\text{img}})$$
  $$Y_{\text{start}} = \text{round}(\text{Top} \cdot H_{\text{img}})$$
  $$W_{\text{total}} = \text{round}(\text{Width} \cdot W_{\text{img}})$$
  $$H_{\text{total}} = \text{round}(\text{Height} \cdot H_{\text{img}})$$

The band's outer boundaries $X_{\text{start}}$ and $X_{\text{start}} + W_{\text{total}}$ are **strictly invariant** when adjusting internal column dividers.

### 4.2 Column Coordinate Resolution
Let $d_0 = 0.0$, $d_{10} = 1.0$, and $d_1 \dots d_9$ be the 9 internal divider ratios ($0.0 < d_1 < d_2 < \dots < d_9 < 1.0$).  
For column $c \in [0, 9]$:
- Left edge:
  $$x_{\text{left}}(c) = X_{\text{start}} + \text{round}(d_c \cdot W_{\text{total}})$$
- Right edge:
  $$x_{\text{right}}(c) = X_{\text{start}} + \text{round}(d_{c+1} \cdot W_{\text{total}})$$
- Column width:
  $$w_{\text{col}}(c) = x_{\text{right}}(c) - x_{\text{left}}(c)$$

### 4.3 Row Coordinate Resolution (2 Rows per Band)
With `RowSplitRatio` $s$ (default $0.50$):
- Row 0 (Top):
  $$y_0 = Y_{\text{start}}, \quad h_0 = \text{round}(s \cdot H_{\text{total}})$$
- Row 1 (Bottom):
  $$y_1 = Y_{\text{start}} + h_0, \quad h_1 = H_{\text{total}} - h_0$$

### 4.4 Inward Bleed Inset Application
For cell $(r, c)$ in the band:
$$\text{cropX} = x_{\text{left}}(c) + \text{inset}$$
$$\text{cropY} = y_r + \text{inset}$$
$$\text{cropW} = w_{\text{col}}(c) - 2 \cdot \text{inset}$$
$$\text{cropH} = h_r - 2 \cdot \text{inset}$$

**Safety Guard:** Validate that $\text{cropW} \ge 10\text{ px}$ and $\text{cropH} \ge 10\text{ px}$ before extracting `new Mat(masterMat, roi)`.

---

## 5. Global Sheet Coordinate & Front/Back Pairing Logic

### 5.1 Deterministic Piece Naming (100 Pieces)
Across the 5 bands on a standard SPI sheet (*WorldKiller*):
| Band Index | Band Name | Local Rows | Global Sheet Rows | Columns | Piece Numbers |
| :---: | :--- | :---: | :---: | :---: | :---: |
| **Band 1** | Red Intruders | 0, 1 | **R01, R02** | C01–C10 | 001–020 |
| **Band 2** | White Intruders & Shields | 0, 1 | **R03, R04** | C01–C10 | 021–040 |
| *Gap* | *DAMAGE MARKERS Header* | *Skipped* | *Skipped* | — | — |
| **Band 3** | Damage Markers 3 & 5 | 0, 1 | **R05, R06** | C01–C10 | 041–060 |
| *Gap* | *Divider Gap* | *Skipped* | *Skipped* | — | — |
| **Band 4** | Damage 7-11 & Blue Units | 0, 1 | **R07, R08** | C01–C10 | 061–080 |
| *Gap* | *PLANETARY FORCES Header* | *Skipped* | *Skipped* | — | — |
| **Band 5** | Blue Planetary Forces | 0, 1 | **R09, R10** | C01–C10 | 081–100 |

Global row formula for band index $b \in [0, 4]$ and local row $r \in [0, 1]$:
$$\text{globalRow} = (b \cdot 2) + r + 1 \quad (\text{range } 1 \dots 10)$$

Piece filename:
```text
WorldKiller_R01_C01_Front.png ... WorldKiller_R10_C10_Front.png
```

### 5.2 Front/Back Reverse Side Mapping
On the back counter sheet (with `HorizontalFlip`):
- Band $b$ on the front maps to Band $b$ on the back (both are band $b$ from top).
- Column $c \in [0, 9]$ on the front maps to column $9 - c$ on the back:
  $$\text{mappedCol} = 9 - c \quad (\text{Col } 1 \leftrightarrow \text{Col } 10)$$
- **Crucial Alignment Gotcha:** If Column 1 on the front has divider $d_1 = 0.105$ (slightly wider than average), then on the reverse sheet with `HorizontalFlip`, the corresponding chit is located at Column 10! The back profile's 9th divider $d_9^{\text{back}}$ must match $1.0 - d_1^{\text{front}}$.
- Supporting independent `FrontBands` and `BackBands` in the profile allows the user to align the back bands and dividers to the physical back scan with 100% fidelity.

---

## 6. UI & Interactive Canvas Overlay Workflow

### 6.1 Multi-Band Live Canvas Overlay (`MainWindow.xaml.cs`)
The canvas renders:
1. **All Defined Bands:**
   - Inactive bands: drawn with thin semi-transparent outlines (`#38BDF8`, Opacity 0.45).
   - Shows the user the entire layout of all 5 bands at a glance, immediately verifying that headers ("DAMAGE MARKERS", "PLANETARY FORCES") fall cleanly into the empty gaps between bands.
2. **The Active Band:**
   - Outer bounding box: bold Sky-600 (`#0284C7`), StrokeThickness 3, dashed.
   - Horizontal dividing line at `RowSplitRatio` (default 50%).
   - 9 vertical dividing lines at positions $d_1 \dots d_9$.
3. **Interactive Visual Feedback:**
   - Adjusting any band slider (`Left`, `Top`, `Width`, `Height`, `RowSplit`) or divider position instantly updates the canvas lines in real time.

### 6.2 Settings Sidebar Layout
1. **Band Selector Strip:**
   - Band navigation: `[◀ Prev]` `[Band 1 of 5 (Red Intruders)]` `[Next ▶]`
   - Add Band (`+`) / Remove Band (`-`) buttons.
2. **Active Band Outer Bounding Box Card:**
   - Sliders + numeric readouts:
     - `Band Left (X %)` (0–50%)
     - `Band Top (Y %)` (0–90%)
     - `Band Width (%)` (50–100%)
     - `Band Height (%)` (5–30%)
     - `Row Split (%)` (30–70%, default 50%)
3. **Column Divider Tuning Card:**
   - Radio buttons: `[●] Even 10 Columns` vs `[○] Fine-Tune Column Dividers`.
   - When Fine-Tune is selected:
     - Divider selector: `Divider 1 (Cols 1|2)`, `Divider 2 (Cols 2|3)` ... `Divider 9 (Cols 9|10)`.
     - Fine nudging: `[-1 px]`, `[+1 px]`, and `[Reset to Even]` buttons.
4. **Single Chit Preview Card:**
   - Cell selector: `Row: [1 / 2]`, `Col: [1 .. 10]`.
   - High-magnification preview of that specific cell with the red bleed-inset cut boundary box.
   - Allows the user to inspect Column 1, Column 5, and Column 10 to guarantee that neither die-cut borders nor neighbor numbers bleed in.
5. **Batch Slice Actions:**
   - `✂️ Slice All Bands (100 Pieces)...` (primary accent button).
   - `✂️ Slice Active Band Only (20 Pieces)...` (secondary button for fast spot-checking).

---

## 7. Comprehensive "Gotchas & Edge Cases" Analysis

### 7.1 Header Bars & Inter-Band Gaps
- **Failure Mode:** In *WorldKiller*, the gap between Band 2 and Band 3 contains "DAMAGE MARKERS" text. Slicing with continuous 10 rows causes damage markers to be cut in half and all lower pieces to be corrupted.
- **Guardrail:** Banded slicing isolates each 2×10 block independently. The space between $Y_{\text{start}}(\text{Band 2}) + H(\text{Band 2})$ and $Y_{\text{start}}(\text{Band 3})$ is completely ignored during cropping.

### 7.2 Variable Column Widths & Cumulative Pixel Rounding
- **Failure Mode:** If each column width is computed as $\text{round}(W / 10)$, the sum of the 10 rounded column widths may differ from the total width by $\pm 1$ to $4\text{ px}$, causing the 10th column to be clipped or overshoot the band boundary.
- **Guardrail:** Column boundaries are computed from monotonic divider ratios $d_0 = 0.0 < d_1 < \dots < d_9 < d_{10} = 1.0$. Column $c$ right edge is always column $c+1$ left edge:
  $$x_{\text{left}}(c+1) \equiv x_{\text{right}}(c)$$
  The right edge of Column 9 is guaranteed to equal $X_{\text{start}} + W_{\text{total}}$ exactly.

### 7.3 Scanner Margin Asymmetry & Print Registration
- **Failure Mode:** Vintage sheets scanned on flatbed scanners rarely sit perfectly square. Even after rotation deskew, the left margin of Band 1 may differ from Band 5 by 0.5%.
- **Guardrail:** Each band has its own independent `Left` and `Width` properties, allowing individual bands to be shifted horizontally if the physical cardboard has trapezoidal or shear distortion.

### 7.4 Die-Cut Line Bleed vs. Unit Artwork Clipping
- **Failure Mode:** Setting inward inset to 0 causes physical black die-cut lines or adjacent unit colors (e.g. Red bleeding into White) to appear on counter edges. Setting inset $> 8\text{ px}$ cuts off combat numbers.
- **Guardrail:** Single chit preview lets the user toggle between Row 1/Col 1 and Row 2/Col 10 while tuning the inset slider in real-time before exporting 100 files.

### 7.5 Front/Back Inversion & Press Misalignment
- **Failure Mode:** Offset printing in 1980 often shifted the reverse side up or down by 1–3 mm relative to the front. Using front coordinates for the back sheet misaligns the chits.
- **Guardrail:** Profiles maintain discrete `FrontBands` and `BackBands`. When switching the UI between "Front" and "Back", the active bands automatically switch, preserving independent physical registration for both sides.

### 7.6 Deterministic Disposal & Memory Safeguards
- **Failure Mode:** Slicing 5 bands $\times$ 20 pieces = 100 pieces at 600 DPI (each piece $\approx 300 \times 300\text{ px}$) creates 100 image buffers.
- **Guardrail:** Zero-copy sub-`Mat` references (`new Mat(masterMat, cellRoi)`) reference the deskewed master without duplicating memory. Every piece `Mat` is disposed in a `using` block immediately after `Cv2.ImWrite`.

---

## 8. Phased Implementation Roadmap

When approved to implement, the changes will proceed in clean, verifiable steps:

1. **Step 1: Models & Default Profiles (`Models/SheetProfile.cs`, `Services/ProfileRepository.cs`):**
   - Implement `GridSlicingMode`, `Band2x10Definition`, and update `SheetProfile`.
   - Configure pre-calibrated 5-band preset for **SPI WorldKiller 1980 (10×10)**.
2. **Step 2: Slicer Service Refactor (`Services/GridSlicerService.cs`):**
   - Add `SliceBandedGridAsync` supporting multi-band iteration, variable column dividers, and global sheet row/col mapping (`R01_C01` to `R10_C10`).
   - Add cell preview extraction for arbitrary band, row, and column.
3. **Step 3: ViewModel State & Commands (`ViewModels/MainViewModel.cs`):**
   - Add active band selection, band bounding sliders, divider nudging, and preview cell selection.
   - Add `SliceAllBandsCommand` and `SliceActiveBandCommand`.
4. **Step 4: UI & Canvas Overlay (`MainWindow.xaml`, `MainWindow.xaml.cs`):**
   - Update `MainWindow.xaml` with the band selector, divider nudging controls, and chit preview cell selector.
   - Update `MainWindow.xaml.cs` to render all bands on the canvas with active band highlighting and column divider lines.
5. **Step 5: Pre-Build Lock Check, Compilation & Verification:**
   - Execute pre-build process lock check.
   - Run `dotnet build -c Debug` to confirm 0 warnings and 0 errors.
   - Version bump to `2026-22-11.3`.

---

## 9. Approval Gate

Per `AGENT.md` and user instructions, this plan is strictly read-only. No source files, project configs, or git repositories have been modified. Implementation will begin only upon explicit user approval.
