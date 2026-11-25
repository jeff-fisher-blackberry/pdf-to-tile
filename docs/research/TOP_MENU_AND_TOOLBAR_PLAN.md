# Research Report & Implementation Plan: Desktop Top Menu Bar & Traditional Toolbar

**Target Application:** `pdf-to-Tiles` (WPF, .NET 8.0/9.0 Windows x64)  
**Document Path:** `pdf-to-Tiles/docs/research/TOP_MENU_AND_TOOLBAR_PLAN.md`  
**Reference Benchmark:** `GTL Text Editor` (`Read_GTL/GTL Text Editor.Designer.cs`, `GTL Text Editor.cs`, classic desktop ergonomics)  
**Status:** Architecture Specification & Engineering Blueprint (Strictly Read-Only Research Phase)

---

## 1. Executive Summary & Design Vision

`pdf-to-Tiles` is a high-precision wargame counter extraction, deskewing, slicing, and chit-editing desktop utility. While it provides high-throughput workflow tabs (Tab 1: *PDF Viewer & Page Capture*, Tab 2: *Deskew & Grid Slicer*, Tab 3: *Chit Editor*), it currently relies solely on in-panel buttons and mode-selection radio tabs along the top header.

### Core Objectives
1. **Provide a Classic Desktop Menu Bar:**
   - Standard top-level menus: `File`, `Edit`, `View`, and `Help` with conventional keyboard mnemonics (`Alt+F`, `Alt+E`, `Alt+V`, `Alt+H`).
   - Standard keyboard accelerators (`Ctrl+O`, `Ctrl+W`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+X`, `Ctrl+C`, `Ctrl+V`, `Ctrl+1`, `Ctrl+2`, `Ctrl+3`, `F1`).
   - Dynamic item enablement (`IsEnabled`) bound cleanly to MVVM ViewModel state (`HasLoadedPdf`, `HasSlicerImage`, `HasSelectedChit`, `CanUndoChit`, `CanRedoChit`, etc.), mirroring `GTL Text Editor`'s behavior where actions are disabled when no document or piece is loaded.

2. **Add a Traditional Desktop ToolBar:**
   - A flat, clean, native WPF `ToolBarTray` / `ToolBar` positioned immediately below the menu bar.
   - Organized into logical groups with vertical separators:
     - **File Group:** Open PDF, Open Sheet Image, Export/Save.
     - **Edit/Clipboard Group:** Undo, Redo, Cut, Copy, Paste.
     - **Workflow / Slicer Handoff Group:** Auto-Detect Tilt, Send 2×10 Band to Chit Editor.
     - **View / Zoom Group:** Fit to Width, Fit to View, 100% Zoom.
   - Distinct, crisp iconography / glyphs paired with descriptive tooltips including keyboard shortcut hints.

3. **Zero Layout Regressions & Strict Height Budgeting:**
   - The combined vertical height of the Menu (~24 px) and Toolbar (~32 px) is ~56 px.
   - Adding a dedicated top row in `MainWindow.xaml` preserves the exact height constraints of the workspace viewports, the `TILE STRIP (20 pieces)` scroll bar, the centered `WORKING CHIT` canvas, and the bottom status bar.
   - Full compatibility with the Light Theme color palette (`{DynamicResource ...}` brushes).

---

## 2. Audit of `GTL Text Editor` Menu & Toolbar Patterns

A read-only inspection of `C:\Antigravity\GTL Text Editor\Read_GTL\GTL Text Editor.Designer.cs` and `GTL Text Editor.cs` reveals key WinForms desktop conventions that make GTL Text Editor highly familiar and efficient for counter-sheet design:

### 2.1 Menu Hierarchy (`menuStrip1`)
1. **`fileToolStripMenuItem` ("&File"):**
   - `openToolStripMenuItem` (`ShortcutKeys = Keys.Control | Keys.O`)
   - `saveToolStripMenuItem` (`ShortcutKeys = Keys.Control | Keys.S`)
   - `saveAsToolStripMenuItem`
   - `toolStripSeparator1`
   - `mRUToolStripMenuItem` (Recent files list)
   - `toolStripSeparator2`
   - `openPositionToolStripMenuItem` / `savePositionToolStripMenuItem`
   - `toolStripSeparator3`
   - `closeFileToolStripMenuItem`
   - `createPNGToolStripMenuItem` (`ShortcutKeys = Keys.Control | Keys.P`)
   - `toolStripSeparator4`
   - `exitToolStripMenuItem` (`ShortcutKeys = Keys.Alt | Keys.F4`)
2. **`editToolStripMenuItem` ("&Edit"):**
   - `copyToolStripMenuItem` (`ShortcutKeys = Keys.Control | Keys.C`)
   - `pasteToolStripMenuItem` (`ShortcutKeys = Keys.Control | Keys.V`)
   - `deleteTileToolStripMenuItem` (`ShortcutKeys = Keys.Delete`)
   - Tile transform actions (Rotate 90° CW/CCW, Flip Horizontal/Vertical)
3. **`helpToolStripMenuItem` ("&Help"):**
   - `contentsToolStripMenuItem` (`F1`)
   - `howToToolStripMenuItem`
   - `aboutToolStripMenuItem` -> opens `About` form displaying version, authors, and license/system details.

### 2.2 Toolbar Structure (`toolStrip1`)
- Standard 24×24 / 16×16 icon buttons:
  - `openToolStripButton` (Folder icon)
  - `saveToolStripButton` (Floppy disk icon)
  - `toolStripSeparator`
  - `copyToolStripButton` / `pasteToolStripButton`
  - `toolStripSeparator`
  - Font and text styling dropdowns / buttons
  - Rotation buttons: `toolRotateLeftButton`, `toolRotateRightButton`
  - Flip buttons: `toolFlipHorizontal`, `toolFlipVertical`
  - Delete tile button

### 2.3 Enable/Disable Lifecycle (State Guarding)
In `GTL Text Editor.cs` (lines 2350–2480 and 4440–4475):
- When no scenario or sheet is loaded:
  - Save, Save As, Close File, Create PNG, Copy, Paste, and Transform actions are explicitly set to `Enabled = false`.
- When a document is loaded or tiles are selected:
  - Applicable commands switch to `Enabled = true`.
- In WPF/MVVM, this corresponds to `ICommand.CanExecute` or direct `IsEnabled="{Binding ...}"` property bindings.

---

## 3. Audit of Existing `pdf-to-Tiles` Chrome & Commands

`MainWindow.xaml` and `MainViewModel.cs` currently define robust command pipelines across the 3 workflows:

| Workflow Area | Existing Commands in `MainViewModel.cs` | Trigger Element in UI | Applicable State Property |
| :--- | :--- | :--- | :--- |
| **Tab 0: PDF Viewer** | `OpenPdfCommand` | Left-panel button | Always available |
| | `PreviousPageCommand`, `NextPageCommand`, `GoToPageCommand` | Pager controls | `HasLoadedPdf`, `CurrentPageNumber > 1`, etc. |
| | `RotatePdfClockwiseCommand`, `RotatePdfCounterClockwiseCommand` | PDF viewer buttons | `HasLoadedPdf` |
| | `ExportPageToPngCommand` | "Export Page to PNG..." button | `HasLoadedPdf` |
| | `SendPageToSlicerCommand` | "Send Page to Grid Slicer" button | `HasLoadedPdf` |
| **Tab 1: Deskew & Slicer** | `OpenImageForSlicingCommand` | Slicer panel button | Always available |
| | `AutoDetectTiltCommand` | "Auto-Detect Tilt" button | `HasSlicerImage` |
| | `ApplyDeskewCommand` | "Apply" button | `HasSlicerImage` |
| | `ResetDeskewCommand` | "Reset" button | `HasSlicerImage` |
| | `SliceGridCommand` | "Export Sliced PNG Pieces..." button | `HasSlicerImage` |
| | `SendToChitEditorCommand` | "Send 2×10 Band to Chit Editor" button | `HasSlicerImage` |
| **Tab 2: Chit Editor** | `UndoChitCommand` | Chit Editor panel button | `CanUndoChit` (`_chitUndoStack.Count > 0`) |
| | `RedoChitCommand` | Chit Editor panel button | `CanRedoChit` (`_chitRedoStack.Count > 0`) |
| | `CutChitCommand` | Keyboard shortcut | `HasSelectedChit` |
| | `CopyChitCommand` | Keyboard shortcut | `HasSelectedChit` |
| | `PasteChitCommand` | Keyboard shortcut | Clipboard has image data |
| | `ApplyChitCropCommand` / `ApplyClipCommand` | "Set" button | `HasSelectedChit` |
| | `CleanEdgesCommand` | "Clean" button | `HasSelectedChit` |
| | `ResetSelectedChitCommand` | "Reset Current" button | `HasSelectedChit` |
| | `ResetAllChitsCommand` | "Reset All 20" button | `LoadedChits.Count > 0` |
| | `ExportAllEditedChitsCommand` | "Export All 20 Chits to PNG..." button | `LoadedChits.Count > 0` |

---

## 4. Proposed Architecture: Top Menu Bar & Traditional Toolbar

### 4.1 Window Layout Reorganization
In `MainWindow.xaml`, the root `Grid` currently has 3 rows:
```xml
<!-- Current Layout -->
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- Row 0: Mode Header / Tab Strip -->
    <RowDefinition Height="*" />     <!-- Row 1: Workspace Content Area -->
    <RowDefinition Height="Auto" />  <!-- Row 2: Status Bar -->
</Grid.RowDefinitions>
```

We transition cleanly to a 4-row layout:
```xml
<!-- Proposed Layout -->
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- Row 0: Menu Bar + ToolBarTray -->
    <RowDefinition Height="Auto" />  <!-- Row 1: Mode Header / Tab Strip -->
    <RowDefinition Height="*" />     <!-- Row 2: Workspace Content Area -->
    <RowDefinition Height="Auto" />  <!-- Row 3: Status Bar -->
</Grid.RowDefinitions>
```

### 4.2 Top Menu Bar Specification (`Menu`)

```xml
<Menu Grid.Row="0"
      Background="{DynamicResource SystemChromeLowColorBrush}"
      Foreground="{DynamicResource SystemBaseHighColorBrush}"
      BorderBrush="{DynamicResource SystemChromeHighColorBrush}"
      BorderThickness="0,0,0,1"
      FontFamily="Segoe UI"
      FontSize="12">

    <!-- FILE MENU -->
    <MenuItem Header="_File">
        <MenuItem Header="_Open PDF Document..."
                  InputGestureText="Ctrl+O"
                  Command="{Binding OpenPdfCommand}">
            <MenuItem.Icon>
                <TextBlock Text="📄" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="Open _Counter Sheet Image..."
                  InputGestureText="Ctrl+Shift+O"
                  Command="{Binding OpenImageForSlicingCommand}">
            <MenuItem.Icon>
                <TextBlock Text="🖼" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="_Close Active Document"
                  InputGestureText="Ctrl+W"
                  Command="{Binding CloseDocumentCommand}"
                  IsEnabled="{Binding HasActiveDocument}">
            <MenuItem.Icon>
                <TextBlock Text="✕" FontSize="12" FontWeight="Bold" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="Export Captured _Page to PNG..."
                  InputGestureText="Ctrl+E"
                  Command="{Binding ExportPageToPngCommand}"
                  IsEnabled="{Binding HasLoadedPdf}">
            <MenuItem.Icon>
                <TextBlock Text="💾" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="Export Sliced _Grid Pieces..."
                  Command="{Binding SliceGridCommand}"
                  IsEnabled="{Binding HasSlicerImage}">
            <MenuItem.Icon>
                <TextBlock Text="💾" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="Export _All 20 Chits to PNG..."
                  InputGestureText="Ctrl+Shift+S"
                  Command="{Binding ExportAllEditedChitsCommand}"
                  IsEnabled="{Binding HasChitsLoaded}">
            <MenuItem.Icon>
                <TextBlock Text="💾" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="E_xit"
                  InputGestureText="Alt+F4"
                  Command="{Binding ExitApplicationCommand}">
            <MenuItem.Icon>
                <TextBlock Text="🚪" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
    </MenuItem>

    <!-- EDIT MENU -->
    <MenuItem Header="_Edit">
        <MenuItem Header="_Undo Chit Action"
                  InputGestureText="Ctrl+Z"
                  Command="{Binding UndoChitCommand}"
                  IsEnabled="{Binding CanUndoChit}">
            <MenuItem.Icon>
                <TextBlock Text="↩" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="_Redo Chit Action"
                  InputGestureText="Ctrl+Y"
                  Command="{Binding RedoChitCommand}"
                  IsEnabled="{Binding CanRedoChit}">
            <MenuItem.Icon>
                <TextBlock Text="↪" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="Cu_t Active Chit"
                  InputGestureText="Ctrl+X"
                  Command="{Binding CutChitCommand}"
                  IsEnabled="{Binding HasSelectedChit}">
            <MenuItem.Icon>
                <TextBlock Text="✂" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="_Copy Active Chit"
                  InputGestureText="Ctrl+C"
                  Command="{Binding CopyChitCommand}"
                  IsEnabled="{Binding HasSelectedChit}">
            <MenuItem.Icon>
                <TextBlock Text="📋" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <MenuItem Header="_Paste Image to Chit"
                  InputGestureText="Ctrl+V"
                  Command="{Binding PasteChitCommand}">
            <MenuItem.Icon>
                <TextBlock Text="📥" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="Reset _Current Chit to Original"
                  Command="{Binding ResetSelectedChitCommand}"
                  IsEnabled="{Binding HasSelectedChit}" />
        <MenuItem Header="Reset All _20 Chits to Original"
                  Command="{Binding ResetAllChitsCommand}"
                  IsEnabled="{Binding HasChitsLoaded}" />
        <MenuItem Header="Reset _Edge Cleanup Depths"
                  Command="{Binding ResetEdgeCleanCommand}"
                  IsEnabled="{Binding HasSelectedChit}" />
    </MenuItem>

    <!-- VIEW MENU -->
    <MenuItem Header="_View">
        <MenuItem Header="PDF _Viewer &amp; Page Capture"
                  InputGestureText="Ctrl+1"
                  Command="{Binding SwitchTabCommand}"
                  CommandParameter="0" />
        <MenuItem Header="_Deskew &amp; Grid Slicer"
                  InputGestureText="Ctrl+2"
                  Command="{Binding SwitchTabCommand}"
                  CommandParameter="1" />
        <MenuItem Header="_Chit Editor"
                  InputGestureText="Ctrl+3"
                  Command="{Binding SwitchTabCommand}"
                  CommandParameter="2" />
        <Separator />
        <MenuItem Header="Fit to _Width"
                  Command="{Binding FitWidthCommand}" />
        <MenuItem Header="Fit to _View"
                  Command="{Binding FitViewCommand}" />
        <MenuItem Header="_Actual Size (100%)"
                  Command="{Binding ActualSizeCommand}" />
    </MenuItem>

    <!-- HELP MENU -->
    <MenuItem Header="_Help">
        <MenuItem Header="_Keyboard Shortcuts Reference..."
                  Command="{Binding ShowShortcutsCommand}">
            <MenuItem.Icon>
                <TextBlock Text="⌨" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
        <Separator />
        <MenuItem Header="_About PDF to Tiles..."
                  Command="{Binding ShowAboutCommand}">
            <MenuItem.Icon>
                <TextBlock Text="ℹ" FontSize="14" VerticalAlignment="Center" />
            </MenuItem.Icon>
        </MenuItem>
    </MenuItem>
</Menu>
```

### 4.3 Traditional Desktop ToolBar Specification (`ToolBarTray` / `ToolBar`)

Positioned immediately beneath the `Menu` inside Row 0:
```xml
<ToolBarTray Grid.Row="0"
             Margin="0,24,0,0"
             Background="{DynamicResource SystemChromeLowColorBrush}"
             IsLocked="True">
    <ToolBar Background="Transparent"
             Band="0" BandIndex="0"
             Height="32"
             ToolBarTray.IsLocked="True">

        <!-- FILE GROUP -->
        <Button Command="{Binding OpenPdfCommand}"
                ToolTip="Open PDF Document (Ctrl+O)"
                Padding="4,2">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="📄" FontSize="14" Margin="0,0,4,0" VerticalAlignment="Center" />
                <TextBlock Text="Open PDF" VerticalAlignment="Center" />
            </StackPanel>
        </Button>
        <Button Command="{Binding OpenImageForSlicingCommand}"
                ToolTip="Open Counter Sheet Image (Ctrl+Shift+O)"
                Padding="4,2"
                Margin="2,0,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="🖼" FontSize="14" Margin="0,0,4,0" VerticalAlignment="Center" />
                <TextBlock Text="Open Image" VerticalAlignment="Center" />
            </StackPanel>
        </Button>
        <Button Command="{Binding ExportAllEditedChitsCommand}"
                IsEnabled="{Binding HasChitsLoaded}"
                ToolTip="Export All 20 Chits to PNG (Ctrl+Shift+S)"
                Padding="4,2"
                Margin="2,0,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="💾" FontSize="14" Margin="0,0,4,0" VerticalAlignment="Center" />
                <TextBlock Text="Export Chits" VerticalAlignment="Center" />
            </StackPanel>
        </Button>

        <Separator Margin="4,2" />

        <!-- EDIT / CLIPBOARD GROUP -->
        <Button Command="{Binding UndoChitCommand}"
                IsEnabled="{Binding CanUndoChit}"
                ToolTip="Undo Chit Action (Ctrl+Z)"
                Width="28" Height="26">
            <TextBlock Text="↩" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding RedoChitCommand}"
                IsEnabled="{Binding CanRedoChit}"
                ToolTip="Redo Chit Action (Ctrl+Y)"
                Width="28" Height="26"
                Margin="2,0,0,0">
            <TextBlock Text="↪" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding CutChitCommand}"
                IsEnabled="{Binding HasSelectedChit}"
                ToolTip="Cut Active Chit (Ctrl+X)"
                Width="28" Height="26"
                Margin="4,0,0,0">
            <TextBlock Text="✂" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding CopyChitCommand}"
                IsEnabled="{Binding HasSelectedChit}"
                ToolTip="Copy Active Chit (Ctrl+C)"
                Width="28" Height="26"
                Margin="2,0,0,0">
            <TextBlock Text="📋" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding PasteChitCommand}"
                ToolTip="Paste Image to Chit (Ctrl+V)"
                Width="28" Height="26"
                Margin="2,0,0,0">
            <TextBlock Text="📥" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Button>

        <Separator Margin="4,2" />

        <!-- WORKFLOW / PIPELINE HANDOFF GROUP -->
        <Button Command="{Binding AutoDetectTiltCommand}"
                IsEnabled="{Binding HasSlicerImage}"
                ToolTip="Auto-Detect Sheet Rotation &amp; Tilt"
                Padding="4,2">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="📐" FontSize="14" Margin="0,0,4,0" VerticalAlignment="Center" />
                <TextBlock Text="Detect Tilt" VerticalAlignment="Center" />
            </StackPanel>
        </Button>
        <Button Command="{Binding SendToChitEditorCommand}"
                IsEnabled="{Binding HasSlicerImage}"
                ToolTip="Send 2×10 Band to Chit Editor"
                Padding="4,2"
                Margin="2,0,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="✂" FontSize="14" Margin="0,0,4,0" VerticalAlignment="Center" />
                <TextBlock Text="Send to Chit Editor" VerticalAlignment="Center" />
            </StackPanel>
        </Button>

        <Separator Margin="4,2" />

        <!-- ZOOM / VIEW GROUP -->
        <Button Command="{Binding FitWidthCommand}"
                ToolTip="Fit to Window Width"
                Padding="4,2">
            <TextBlock Text="Fit Width" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding FitViewCommand}"
                ToolTip="Fit Entire Content into View"
                Padding="4,2"
                Margin="2,0,0,0">
            <TextBlock Text="Fit View" VerticalAlignment="Center" />
        </Button>
        <Button Command="{Binding ActualSizeCommand}"
                ToolTip="Zoom to Actual 100% Size"
                Padding="4,2"
                Margin="2,0,0,0">
            <TextBlock Text="100%" VerticalAlignment="Center" />
        </Button>

    </ToolBar>
</ToolBarTray>
```

---

## 5. Command Wiring & State Synchronization Matrix

To ensure that the Menu and Toolbar buttons reflect the state of the active file or selected chit seamlessly without manual event thrashing, the following ViewModel properties govern `CanExecute`:

| Action / Command | ViewModel Binding Property | True Condition | False Condition |
| :--- | :--- | :--- | :--- |
| `OpenPdfCommand` | Always `true` | Application running | Never |
| `OpenImageForSlicingCommand` | Always `true` | Application running | Never |
| `CloseDocumentCommand` | `HasActiveDocument` | `HasLoadedPdf \|\| HasSlicerImage \|\| HasChitsLoaded` | No file or image loaded |
| `ExportPageToPngCommand` | `HasLoadedPdf` | PDF file loaded and rendered | PDF not loaded |
| `SliceGridCommand` | `HasSlicerImage` | Deskew/Slicer has image | No image in Slicer |
| `ExportAllEditedChitsCommand` | `HasChitsLoaded` | `LoadedChits.Count > 0` | 0 chits loaded |
| `UndoChitCommand` | `CanUndoChit` | `_chitUndoStack.Count > 0` | Undo stack empty |
| `RedoChitCommand` | `CanRedoChit` | `_chitRedoStack.Count > 0` | Redo stack empty |
| `CutChitCommand` / `CopyChitCommand` | `HasSelectedChit` | `SelectedChit != null` | No chit selected |
| `PasteChitCommand` | `CanPasteChit` | Clipboard contains image data | Clipboard has no image |
| `ResetSelectedChitCommand` | `HasSelectedChit` | `SelectedChit != null` | No chit selected |
| `ResetAllChitsCommand` | `HasChitsLoaded` | `LoadedChits.Count > 0` | 0 chits loaded |
| `AutoDetectTiltCommand` | `HasSlicerImage` | Deskew/Slicer has image | No image in Slicer |
| `SendToChitEditorCommand` | `HasSlicerImage` | Deskew/Slicer has image | No image in Slicer |

---

## 6. Layout & Vertical Budget Analysis

### Height Breakdown
- Window MinHeight: `800 px` (Standard working height: 900–1080 px)
- Top Menu Bar: `24 px`
- Traditional Toolbar: `32 px`
- Total Top Chrome (Menu + Toolbar): **`56 px`**
- Tab Selection Header: `38 px`
- Status Bar: `26 px`
- Remaining Viewport Height at 900 px: **`780 px`**
- Remaining Viewport Height at 1080 px: **`960 px`**

### Viewport Impact Evaluation
1. **Tab 1 (PDF Viewer & Page Capture):**
   - The central viewer uses dynamic layout sizing (`ZoomFactor`). A 56 px vertical reduction is completely absorbed by the vector scaling canvas without clipping.
2. **Tab 2 (Deskew & Grid Slicer):**
   - The slicer viewport and draggable 2×10 grid overlay auto-fit to available bounds. The left control panel (`320 px` wide) remains unaffected.
3. **Tab 3 (Chit Editor):**
   - Left Panel: Fixed width (`320 px`), controls stack vertically.
   - Center Working Chit: Dynamically zooms (`1%` to `1600%`).
   - Right `TILE STRIP (20 pieces)`: Fixed width (`140 px`), wrapped in a native `ScrollViewer`. At 780+ px viewport height, ~7 to 8 chits are visible simultaneously with zero clipping of the scroll thumb.

---

## 7. Mandatory Gotchas, Safety & Edge Cases

1. **Keyboard Accelerator Conflicts with TextBoxes:**
   - In Tab 3, the user enters numeric dimensions and zoom values in `TextBox` controls (e.g., width, height, zoom, edge cleanup px).
   - If a `KeyBinding` for `Ctrl+C`, `Ctrl+X`, or `Ctrl+V` is registered globally at the `Window.InputBindings` level, it could steal clipboard operations from the focused `TextBox`.
   - **Resolution:** Use WPF `ApplicationCommands.Copy`, `ApplicationCommands.Cut`, and `ApplicationCommands.Paste` routing, or gate `CutChitCommand` / `CopyChitCommand` so that when a `TextBox` has keyboard focus (`Keyboard.FocusedElement is TextBox`), the default textbox clipboard handler takes precedence.
2. **Memory Safety & Image Disposal on Close (`CloseDocumentCommand`):**
   - Closing an active document must release underlying OpenCV `Mat` instances and Win32 bitmap handles (`DeleteObject`) to prevent GDI / unmanaged memory leaks.
   - `CloseDocumentCommand` will invoke `CleanupUnmanagedResources()` and reset `CurrentDocumentPath`, `HasLoadedPdf`, `HasSlicerImage`, and clear `LoadedChits`.
3. **Theme & Color Consistency:**
   - The Menu and ToolBar will strictly use `{DynamicResource ...}` brush references (e.g., `SystemChromeLowColorBrush`, `SystemBaseHighColorBrush`) to ensure 100% styling consistency in both light and dark themes without hardcoded colors.
4. **Non-Intrusive Integration:**
   - Existing in-panel buttons (`Export All 20 Chits to PNG...`, `Send 2×10 Band to Chit Editor`, `Auto-Detect Tilt`, `Undo`, `Redo`) remain in place. The Menu and ToolBar act as standard desktop accelerator surfaces, providing discoverability without disrupting existing muscle memory.

---

## 8. Implementation Steps & Verification Plan (Pending Approval)

Once approved by the user:
1. **Step 1: ViewModel Command Augmentation (`MainViewModel.cs`):**
   - Add `CloseDocumentCommand`, `FitWidthCommand`, `FitViewCommand`, `ActualSizeCommand`, `ShowShortcutsCommand`, and `ShowAboutCommand`.
   - Expose composite boolean properties: `HasActiveDocument` and `HasChitsLoaded`.
2. **Step 2: About & Shortcuts Dialog (`Views/AboutWindow.xaml` or Clean Message Box):**
   - Display version (`2026-11-24.1`), authors, memory status, and SPI WorldKiller / Armada support details.
3. **Step 3: XAML Top Chrome Integration (`MainWindow.xaml`):**
   - Update `Grid.RowDefinitions` to 4 rows.
   - Insert `Menu` and `ToolBarTray` / `ToolBar` into Row 0 with dynamic resource bindings and input gestures.
   - Update Row indices for Tab strip (Row 1), Content (Row 2), and Status Bar (Row 3).
4. **Step 4: Build & Visual Verification:**
   - Build `Release|x64`.
   - Verify DPI scaling, menu dropdowns, tooltips, accelerator shortcuts, and disabled button visual states.
