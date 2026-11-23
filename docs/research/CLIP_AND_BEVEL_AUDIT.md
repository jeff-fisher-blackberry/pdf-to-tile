# Audit Report: Corner Clip & Bevel vs. GTL Text Editor

**Date:** 2026-11-23  
**Target Project:** `PDF to Tiles` (`pdf-to-Tiles`)  
**Reference Project:** `GTL Text Editor` (`Read_GTL/GTL Text Editor.cs`)  
**Status:** Audit & Architecture Analysis (Strictly Read-Only Research)

---

## 1. Executive Summary

An in-depth comparative audit was conducted between `pdf-to-Tiles` (`Services/ChitEditorService.cs`, `ViewModels/MainViewModel.cs`, `Models/ChitItem.cs`) and the reference application `GTL Text Editor` (`C:\Antigravity\GTL Text Editor\Read_GTL\GTL Text Editor.cs`, methods `applyCorners` and `applyGradient`).

### Key Findings:
1. **Corner Clip Failure Root Cause:**
   - In `MainViewModel.cs`, `ApplyClip` passes `key = ColorToVec4b(TransparentColor)`.
   - `TransparentColor` is initialized to `Colors.Cyan` (`#00FFFF`), which has **`Alpha = 255`** (`Vec4b(255, 255, 0, 255)`).
   - In WPF with `PixelFormats.Bgra32` and `CheckerboardBrush`, transparency requires **`Alpha = 0`** (`Vec4b(0, 0, 0, 0)`). Writing opaque cyan means corners are rendered as solid cyan squares, not transparent.
   - Immediately following `ApplyClip`, `UpdateLiveEdgeCleanPreview()` is called. If edge cleanup depths are non-zero (as seen in active usage with e.g. `Top: 2px, Left: 10px`), `ApplyEdgeCleanup` inspects pixels in the outer band. Because the clipped corners have `Alpha == 255` (not `0`), the `preserveTransparentCorners && cur.Item3 == 0` check **fails**. `TargetedReplace` evaluates color distance against the counter background, sees cyan as an artifact, and **overwrites the corner pixels with background color**, completely obliterating the clip.
2. **Bevel Overlap & Gradient Artifacts:**
   - In `GTL Text Editor`, `applyGradient` constructs 4 discrete trapezoids (mitered polygons) and fills them with a linear gradient from `Color.Beige` / `Color.Black` to the counter background color.
   - In `ChitEditorService.cs`, `ApplyBevel` loops Top, Left, Bottom, and Right sequentially. Because the loops overlap at the corners, the Right edge shadow **overwrites** the Top edge highlight at the top-right corner, and the Bottom edge shadow **overwrites** the Left edge highlight at the bottom-left corner.
   - In addition, if edge cleanup is active, `UpdateLiveEdgeCleanPreview()` detects the darkened/lightened bevel pixels within the edge cleanup depth and replaces them with solid background color.

---

## 2. Corner Clip Audit: `pdf-to-Tiles` vs. `GTL Text Editor`

### 2.1 GTL Text Editor Reference (`applyCorners`, lines 4845–4896)
```csharp
private void applyCorners(int cornersize)
{
    Image workerTile = (Image)workingTile.Image.Clone();
    UpdateWorkingTileGraphics(workerTile);

    Pen pen = new Pen(pctTransparentColor.BackColor);
   
    // Upper Left
    gWorkingTile.DrawLine(pen, 0, 0, 0, cornersize);
    gWorkingTile.DrawLine(pen, 0, 0, cornersize, 0);
    // Upper Right
    gWorkingTile.DrawLine(pen, workerTile.Width - 1, 0, workerTile.Width - cornersize - 1, 0);
    gWorkingTile.DrawLine(pen, workerTile.Width - 1, 0, workerTile.Width - 1, cornersize);
    // Lower Right
    gWorkingTile.DrawLine(pen, workingTile.Width - 1, workingTile.Height - 1, workingTile.Width - 1, workingTile.Height - cornersize - 1);
    gWorkingTile.DrawLine(pen, workingTile.Width - 1, workingTile.Height - 1, workingTile.Width - cornersize-1, workingTile.Height - 1);
    // Lower Left
    gWorkingTile.DrawLine(pen, 0, workingTile.Height - 1, cornersize, workingTile.Height - 1);
    gWorkingTile.DrawLine(pen, 0, workingTile.Height - 1, 0, workingTile.Height - cornersize - 1);

    if (cornersize == 4) { /* extra 1px inner diagonals */ }
    if (cornersize == 5) { /* extra inner steps */ }
    ...
}
```
*Note:* GTL Text Editor was built for 24-bit GDI+ images where transparency was achieved by drawing with a chroma-key color (`pctTransparentColor.BackColor`, typically Cyan `#00FFFF` or Magenta `#FF00FF`) that CyberBoard / GTL later converted using `bmp.MakeTransparent(key)`.

### 2.2 `pdf-to-Tiles` Current Implementation
In `Services/ChitEditorService.cs` (`ApplyCornerClip`):
```csharp
public static Mat ApplyCornerClip(Mat src, int cornerSize, Vec4b? clipColor = null)
{
    int c = Math.Clamp(cornerSize, 1, Math.Min(src.Width / 2, src.Height / 2));
    Mat dst = src.Clone();
    int width = dst.Width;
    int height = dst.Height;
    Vec4b clr = clipColor ?? new Vec4b(0, 0, 0, 0);

    var indexer = dst.GetGenericIndexer<Vec4b>();

    for (int y = 0; y < c; y++)
    {
        for (int x = 0; x < c; x++)
        {
            if (x + y < c) indexer[y, x] = clr;                     // Upper-Left
            int dxR = width - 1 - x;
            if (x + y < c) indexer[y, dxR] = clr;                  // Upper-Right
            int dyB = height - 1 - y;
            if (x + y < c) indexer[dyB, x] = clr;                  // Lower-Left
            if (x + y < c) indexer[dyB, dxR] = clr;                // Lower-Right
        }
    }
    return dst;
}
```
In `ViewModels/MainViewModel.cs` (`ApplyClip`):
```csharp
[RelayCommand(CanExecute = nameof(CanEditChit))]
private void ApplyClip()
{
    if (SelectedChit?.WorkingMat == null) return;
    var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };
    Vec4b key = ColorToVec4b(TransparentColor); // <-- BUG: Returns (255, 255, 0, 255), Alpha = 255!

    foreach (var chit in targets)
    {
        if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
        var clipped = ChitEditorService.ApplyCornerClip(chit.WorkingMat, ChitEditorClipSize, key);
        chit.UpdateWorkingMat(clipped, recordHistory: true, TransparentColor);
    }
    _lastCommittedSignature = null;
    UpdateLiveEdgeCleanPreview();
    ...
}
```

### 2.3 Diagnosis: Why Transparent Clipped Corners Are Not Appearing
1. **Alpha Channel Dropped / Set to Opaque:**
   `ColorToVec4b(TransparentColor)` converts `TransparentColor` (default Cyan) to `Vec4b(255, 255, 0, 255)`. The alpha component is `255`. A 32-bit BGRA surface in WPF requires `A = 0` to expose the underlying `CheckerboardBrush`.
2. **Immediate Overwrite by Live Edge Cleanup:**
   Because `_lastCommittedSignature` is invalidated, `UpdateLiveEdgeCleanPreview()` triggers immediately on the new matrix. In `ApplyEdgeCleanup`:
   ```csharp
   if (preserveTransparentCorners && cur.Item3 == 0) continue;
   ```
   Since `cur.Item3` is `255`, this check fails. Because cyan deviates strongly from the background counter color, `TargetedReplace` / `SolidFlood` / `SmartInpaint` immediately replaces the corner pixels with the sampled counter color, erasing the clip before the user ever sees it!
3. **Corner Geometry:**
   The `x + y < c` chamfer cuts 3 pixels at `c=2` (`(0,0)`, `(1,0)`, `(0,1)`), 6 pixels at `c=3`, 10 pixels at `c=4`, and 15 pixels at `c=5`. This diagonal chamfer is geometrically superior to GTL's L-shaped pixel lines, but it **must** write `Vec4b(0, 0, 0, 0)`.

---

## 3. Bevel Audit: `pdf-to-Tiles` vs. `GTL Text Editor`

### 3.1 GTL Text Editor Reference (`applyGradient`, lines 4761–4828)
GTL defines 4 non-overlapping trapezoids using 45-degree corner miters:
- **Top Trapezoid:** `(0,0)`, `(b, b)`, `(w - b, b)`, `(w, 0)` -> LinearGradient from `(0,0)` to `(0,b)` (`Color.Beige` to `BackColor`).
- **Left Trapezoid:** `(0,0)`, `(b, b)`, `(b, h - b)`, `(0, h)` -> LinearGradient from `(0,0)` to `(b,0)` (`Color.Beige` to `BackColor`).
- **Right Trapezoid:** `(w, 0)`, `(w - b, b)`, `(w - b, h - b)`, `(w, h)` -> LinearGradient from `(w,0)` to `(w - b, 0)` (`Color.Black` to `BackColor`).
- **Bottom Trapezoid:** `(0, h)`, `(b, h - b)`, `(w - b, h - b)`, `(w, h)` -> LinearGradient from `(0,h)` to `(0, h - b)` (`Color.Black` to `BackColor`).

### 3.2 `pdf-to-Tiles` Current Implementation & Bugs
In `ChitEditorService.ApplyBevel`:
```csharp
// 1. Top Edge Highlight (y from 0 to b-1, startX = y, endX = width - 1 - y)
// 2. Left Edge Highlight (x from 0 to b-1, startY = x, endY = height - 1 - x)
// 3. Bottom Edge Shadow (d from 0 to b-1, startX = d, endX = width - 1 - d)
// 4. Right Edge Shadow (d from 0 to b-1, startY = d, endY = height - 1 - d)
```
**Bugs Identified:**
1. **Corner Collision & Shadow Overwriting Highlight:**
   - In the top-right corner, the Top Edge renders a highlight for `(width - 1 - y, y)`. Then the Right Edge renders a shadow for `(width - 1 - d, y)`, overwriting the highlight with a dark shadow!
   - In the bottom-left corner, the Left Edge renders a highlight for `(x, height - 1 - x)`. Then the Bottom Edge renders a shadow for `(x, height - 1 - d)`, overwriting the highlight with a dark shadow!
   - In top-left and bottom-right corners, pixels are visited twice, causing excessive double-highlighting and double-shadowing.
2. **Edge Cleanup Erasing Bevel:**
   When Bevel is applied to a chit whose edge-cleanup depths are non-zero (e.g. `Top: 2px, Left: 10px`), `UpdateLiveEdgeCleanPreview` detects the beveled pixels as differing from the sampled background and immediately replaces or inpaints over the bevel!
3. **Preservation of Clipped Corners:**
   `ApplyBevel` already has `if (p.Item3 == 0) continue;`. Once `ApplyCornerClip` properly sets `Alpha = 0`, `ApplyBevel` will automatically preserve the clipped corners.

---

## 4. Implementation Plan (For Subsequent Coding Pass)

### Step 1: Fix `ApplyCornerClip` & `ApplyClip`
1. In `MainViewModel.ApplyClip()`, do **not** pass `ColorToVec4b(TransparentColor)` with `Alpha = 255`. Pass `new Vec4b(0, 0, 0, 0)` (true 32-bit transparent alpha).
2. If `TransparentColor` is selected by the user for keyed export, provide a toggle or ensure that editor operations operate on true 32-bit alpha (`A = 0`) while preserving RGB if needed, or pure `Vec4b(0, 0, 0, 0)`.
3. In `ChitEditorService.ApplyCornerClip`, guarantee `dst.GetGenericIndexer<Vec4b>()[y, x] = new Vec4b(0, 0, 0, 0)`.
4. Verify that `ApplyEdgeCleanup` with `preserveTransparentCorners = true` preserves all corner pixels with `Item3 == 0`.

### Step 2: Fix `ApplyBevel` Mitered Trapezoids
1. Replace the 4 sequential edge loops in `ChitEditorService.ApplyBevel` with a single mitered classification pass for each pixel `(x, y)` within the border depth `b`:
   - Calculate distances from borders: `dt = y`, `db = height - 1 - y`, `dl = x`, `dr = width - 1 - x`.
   - Find minimum distance `minD = Math.Min(Math.Min(dt, db), Math.Min(dl, dr))`.
   - If `minD >= b` or `p.Item3 == 0`, continue (interior or transparent pixel).
   - Classify region:
     - If `dt == minD` and `y < x` and `y < width - 1 - x` -> Top Highlight.
     - If `dl == minD` and `x <= y` and `x < height - 1 - y` -> Left Highlight.
     - If `db == minD` and `height - 1 - y < x` and `height - 1 - y < width - 1 - x` -> Bottom Shadow.
     - If `dr == minD` and `width - 1 - x <= y` and `width - 1 - x < height - 1 - y` -> Right Shadow.
   - At diagonal miter intersections (`y == x` etc.), blend transition smoothly so corners have clean 45-degree miter joints.
2. In `MainViewModel.ApplyBevel()`, commit the state and update `_lastCommittedSignature` so that the live edge-clean preview does not immediately overwrite the freshly applied bevel.
