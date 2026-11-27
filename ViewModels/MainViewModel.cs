using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using pdf_to_Tiles.Models;
using pdf_to_Tiles.Services;

namespace pdf_to_Tiles.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string AppBaseTitle = "PDF to Tiles";
        private byte[]? _pdfBytes;

        // Native OpenCV Mat references for Slicer workspace
        private Mat? _masterMat;
        private Mat? _deskewedMat;
        private Mat? _preEdgeCleanMat;
        private CancellationTokenSource? _sliceCts;
        private bool _isSyncingBand = false;

        public Func<(double Width, double Height)>? GetPdfViewportSize { get; set; }
        public Func<(double Width, double Height)>? GetSlicerViewportSize { get; set; }

        #region App & Mode Properties

        [ObservableProperty]
        private string _windowTitle = AppBaseTitle;

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = PDF Viewer, 1 = Deskew & Grid Slicer, 2 = Chit Editor

        [ObservableProperty]
        private string _statusMessage = "Ready. Open a PDF rulebook or counter sheet to begin.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSendPageToSlicer))]
        [NotifyCanExecuteChangedFor(nameof(OpenPdfCommand))]
        [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private bool _isLoading = false;

        #endregion

        #region Phase 1: PDF Viewer Properties

        [ObservableProperty]
        private string? _loadedFilePath;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitStatusBarText))]
        private string? _loadedFileName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private int _totalPages = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private int _currentPageNumber = 0;

        [ObservableProperty]
        private BitmapSource? _currentPageImage;

        [ObservableProperty]
        private int _pagePixelWidth = 0;

        [ObservableProperty]
        private int _pagePixelHeight = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ZoomPercentageText))]
        private double _zoomScale = 1.0;

        [ObservableProperty]
        private bool _isFitToPage = true;

        [ObservableProperty]
        private int _selectedDpi = 600;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFrontOrBackTagSelected))]
        [NotifyPropertyChangedFor(nameof(CanSendPageToSlicer))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private string _selectedSideTag = "Page";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSendPageToSlicer))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private bool _hasExplicitlyPickedPage = false;

        public IReadOnlyList<int> AvailableDpis { get; } = new List<int> { 300, 450, 600 };
        public IReadOnlyList<string> AvailableSideTags { get; } = new List<string> { "Page", "Front", "Rear" };

        public static bool IsRearSide(string? side) =>
            string.Equals(side, "Rear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(side, "Back", StringComparison.OrdinalIgnoreCase);

        public bool IsFrontOrBackTagSelected =>
            string.Equals(SelectedSideTag, "Front", StringComparison.OrdinalIgnoreCase) ||
            IsRearSide(SelectedSideTag);

        public string ZoomPercentageText => $"{Math.Round(ZoomScale * 100)}%";
        public bool HasLoadedPdf => _pdfBytes != null && _pdfBytes.Length > 0 && TotalPages > 0;
        public bool CanSendPageToSlicer => !IsLoading && HasLoadedPdf && HasExplicitlyPickedPage && IsFrontOrBackTagSelected;
        public bool HasChitsLoaded => LoadedChits.Count > 0;
        public bool HasActiveDocument => HasLoadedPdf || HasSlicerImage || HasChitsLoaded;
        public bool CanUndoChit => SelectedChit?.CanUndo ?? false;
        public bool CanRedoChit => SelectedChit?.CanRedo ?? false;

        public bool IsPdfViewerTabSelected => SelectedTabIndex == 0;
        public bool IsTileCreatorTabSelected => SelectedTabIndex == 1;
        public bool IsTileEditorTabSelected => SelectedTabIndex == 2;
        public bool CanAccessRecentPdfs => IsPdfViewerTabSelected && HasRecentPdfs;
        public bool CanSaveTilePageImage => IsTileCreatorTabSelected && (HasSlicerImage || HasLoadedPdf);
        public bool CanOpenTileFiles => IsTileEditorTabSelected;
        public bool CanSaveCurrentTile => IsTileEditorTabSelected && HasSelectedChit;
        public bool CanSaveAllTiles => IsTileEditorTabSelected && HasChitsLoaded;
        public bool CanClosePdf => IsPdfViewerTabSelected && HasLoadedPdf;
        public bool CanCloseTilePageImage => IsTileCreatorTabSelected && HasSlicerImage;
        public bool CanCloseTiles => IsTileEditorTabSelected && HasChitsLoaded;

        public bool CanUndoTileAction => IsTileEditorTabSelected && CanUndoChit;
        public bool CanRedoTileAction => IsTileEditorTabSelected && CanRedoChit;
        public bool CanCutTile => IsTileEditorTabSelected && HasSelectedChit;
        public bool CanCopyTile => IsTileEditorTabSelected && HasSelectedChit;
        public bool CanPasteImageToTile => IsTileEditorTabSelected && HasSelectedChit && HasClipboardImage;
        public bool CanPasteImageToNewTile => IsTileEditorTabSelected && HasClipboardImage;
        public bool CanDeleteCurrentTile => IsTileEditorTabSelected && HasSelectedChit;
        public bool CanDeleteSelectedTiles => IsTileEditorTabSelected && HasSelectedChits;
        public bool CanResetCurrentTile => IsTileEditorTabSelected && HasSelectedChit;
        public bool CanResetAllTiles => IsTileEditorTabSelected && HasChitsLoaded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPasteImageToTile))]
        [NotifyPropertyChangedFor(nameof(CanPasteImageToNewTile))]
        [NotifyCanExecuteChangedFor(nameof(PasteImageToTileCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteImageToNewTileCommand))]
        private bool _hasClipboardImage = false;

        public void UpdateClipboardState()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dataObj = System.Windows.Clipboard.GetDataObject();
                    if (dataObj == null)
                    {
                        HasClipboardImage = false;
                        NotifyPasteProperties();
                        return;
                    }

                    if (dataObj.GetDataPresent("PNG") ||
                        dataObj.GetDataPresent("image/png") ||
                        dataObj.GetDataPresent("image/x-png") ||
                        dataObj.GetDataPresent(System.Windows.DataFormats.Bitmap) ||
                        dataObj.GetDataPresent(System.Windows.DataFormats.Dib) ||
                        System.Windows.Clipboard.ContainsImage())
                    {
                        HasClipboardImage = true;
                        NotifyPasteProperties();
                        return;
                    }

                    if (dataObj.GetDataPresent(System.Windows.DataFormats.FileDrop))
                    {
                        var fileList = System.Windows.Clipboard.GetFileDropList();
                        if (fileList != null && fileList.Count > 0)
                        {
                            foreach (var f in fileList)
                            {
                                if (!string.IsNullOrWhiteSpace(f))
                                {
                                    string ext = Path.GetExtension(f).ToLowerInvariant();
                                    if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff")
                                    {
                                        HasClipboardImage = true;
                                        NotifyPasteProperties();
                                        return;
                                    }
                                }
                            }
                        }
                        else if (dataObj.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                        {
                            HasClipboardImage = files.Any(f => {
                                string ext = Path.GetExtension(f).ToLowerInvariant();
                                return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff";
                            });
                            NotifyPasteProperties();
                            return;
                        }
                    }

                    HasClipboardImage = false;
                    NotifyPasteProperties();
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    if (attempt < 2)
                    {
                        Thread.Sleep(25);
                    }
                    else
                    {
                        HasClipboardImage = false;
                        NotifyPasteProperties();
                    }
                }
                catch
                {
                    HasClipboardImage = false;
                    NotifyPasteProperties();
                    return;
                }
            }
        }

        private void NotifyPasteProperties()
        {
            OnPropertyChanged(nameof(CanPasteImageToTile));
            OnPropertyChanged(nameof(CanPasteImageToNewTile));
            PasteImageToTileCommand.NotifyCanExecuteChanged();
            PasteImageToNewTileCommand.NotifyCanExecuteChanged();
        }

        public IRelayCommand ExportPageToPngCommand => CapturePageCommand;
        public IRelayCommand SaveTilePageImageCommand => SaveTilePageImageInternalCommand;
        public IRelayCommand SaveCurrentTileCommand => ExportSelectedChitCommand;
        public IRelayCommand SaveAllTilesCommand => ExportAllEditedChitsCommand;
        public IRelayCommand SendToChitEditorCommand => SendBandToChitEditorCommand;

        #endregion

        #region Phase 2 & 2.5: Deskew & Grid Slicer Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSlicerImage))]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectTiltCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceGridCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceAllBandsCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceActiveBandCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendBandToChitEditorCommand))]
        [NotifyCanExecuteChangedFor(nameof(RotateLeft90Command))]
        [NotifyCanExecuteChangedFor(nameof(RotateRight90Command))]
        private BitmapSource? _slicerImage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitStatusBarText))]
        private string? _slicerSourceFileName;

        [ObservableProperty]
        private int _slicerPixelWidth = 0;

        [ObservableProperty]
        private int _slicerPixelHeight = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SlicerZoomPercentageText))]
        private double _slicerZoomScale = 1.0;

        [ObservableProperty]
        private bool _isSlicerFitToWidth = true;

        [ObservableProperty]
        private bool _isSlicerFitToPage = false;

        public string SlicerZoomPercentageText => $"{Math.Round(SlicerZoomScale * 100)}%";
        public bool HasSlicerImage => SlicerImage != null && _masterMat != null && !_masterMat.IsDisposed;

        // Deskew
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DeskewAngleText))]
        private double _deskewAngle = 0.0;

        public string DeskewAngleText => $"{DeskewAngle:+0.00;-0.00;0.00}°";

        [ObservableProperty]
        private bool _showGridOverlay = true;

        [ObservableProperty]
        private bool _showAlignmentGrid = false;

        [ObservableProperty]
        private bool _isAutoDetectingTilt = false;

        // Profiles
        public ObservableCollection<SheetProfile> AvailableProfiles { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBandedMode))]
        [NotifyPropertyChangedFor(nameof(IsUniformGridMode))]
        private SheetProfile? _selectedProfile;

        public bool IsBandedMode => SelectedProfile?.SlicingMode == GridSlicingMode.Banded2x10;
        public bool IsUniformGridMode => !IsBandedMode;

        [ObservableProperty]
        private int _profileRows = 10;

        [ObservableProperty]
        private int _profileColumns = 10;

        // Legacy Relative Grid bounds (0 to 100%) for UniformGrid mode
        [ObservableProperty]
        private double _gridLeftPercent = 5.0;

        [ObservableProperty]
        private double _gridTopPercent = 8.0;

        [ObservableProperty]
        private double _gridWidthPercent = 90.0;

        [ObservableProperty]
        private double _gridHeightPercent = 84.0;

        // Discrete 2x10 Band Properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveBandDisplayIndex))]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        [NotifyCanExecuteChangedFor(nameof(PreviousBandCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextBandCommand))]
        private int _activeBandIndex = 0;

        public int ActiveBandDisplayIndex => ActiveBandIndex + 1;

        [ObservableProperty]
        private string _activeBandName = "Band 1: Red Intruders";

        [ObservableProperty]
        private int _totalBands = 5;

        [ObservableProperty]
        private double _activeBandLeftPercent = 5.0;

        [ObservableProperty]
        private double _activeBandTopPercent = 6.5;

        [ObservableProperty]
        private double _activeBandWidthPercent = 92.1;

        [ObservableProperty]
        private double _activeBandHeightPercent = 14.2;

        [ObservableProperty]
        private double _activeBandRowSplitPercent = 50.0;

        // Native integer pixel properties
        [ObservableProperty]
        private int _activeBandLeftPx = 0;

        [ObservableProperty]
        private int _activeBandTopPx = 0;

        [ObservableProperty]
        private int _activeBandWidthPx = 100;

        [ObservableProperty]
        private int _activeBandHeightPx = 100;

        [ObservableProperty]
        private int _activeBandRowSplitPx = 50;

        // Formatted pixel text properties for editable textboxes
        [ObservableProperty]
        private string _activeBandLeftText = "0 px";

        [ObservableProperty]
        private string _activeBandTopText = "0 px";

        [ObservableProperty]
        private string _activeBandWidthText = "100 px";

        [ObservableProperty]
        private string _activeBandHeightText = "100 px";

        [ObservableProperty]
        private string _activeBandRowSplitText = "50 px";

        // Readouts
        public int ActiveBandRightPx => ActiveBandLeftPx + ActiveBandWidthPx;
        public int ActiveBandBottomPx => ActiveBandTopPx + ActiveBandHeightPx;
        public string ActiveBandDimensionsText => $"{ActiveBandWidthPx} × {ActiveBandHeightPx} px";
        public string ActiveBandBoundsSummaryText => $"({ActiveBandLeftPx}, {ActiveBandTopPx}) to ({ActiveBandRightPx}, {ActiveBandBottomPx}) px";
        public string ActiveBandDimensionsSummaryText => $"{ActiveBandWidthPx} × {ActiveBandHeightPx} px (20 Tiles, ~{Math.Max(1, ActiveBandWidthPx / 10)} × {Math.Max(1, ActiveBandHeightPx / 2)} px)";
        public string ActiveBandLeftPercentText => $"({ActiveBandLeftPercent:F1}%)";
        public string ActiveBandTopPercentText => $"({ActiveBandTopPercent:F1}%)";
        public string ActiveBandWidthPercentText => $"({ActiveBandWidthPercent:F1}%)";
        public string ActiveBandHeightPercentText => $"({ActiveBandHeightPercent:F1}%)";
        public string ActiveBandRowSplitPercentText => $"({ActiveBandRowSplitPercent:F1}%)";

        // Columns and Row configuration for active band
        [ObservableProperty]
        private string _pendingBandBox1 = "2";

        [ObservableProperty]
        private string _pendingBandBox2 = "10";

        [ObservableProperty]
        private string _committedBandBox1 = "2";

        [ObservableProperty]
        private string _committedBandBox2 = "10";

        public string BandInputBox1
        {
            get => PendingBandBox1;
            set => PendingBandBox1 = value;
        }

        public string BandInputBox2
        {
            get => PendingBandBox2;
            set => PendingBandBox2 = value;
        }

        [ObservableProperty]
        private bool _isDoubleWidthColumns = false;

        public string BandBoundingBoxHeader
        {
            get
            {
                if (CommittedBandBox1 == "2" && CommittedBandBox2 == "10")
                {
                    return "2×10 Band Bounding Box";
                }
                return $"{CommittedBandBox1} x {CommittedBandBox2} Band Bounding Box";
            }
        }

        public int EffectiveBandRows
        {
            get
            {
                ParseCommittedBandInputs(out int r, out _);
                return r;
            }
        }

        public int EffectiveBandColumns
        {
            get
            {
                ParseCommittedBandInputs(out _, out int c);
                return c;
            }
        }

        public int TotalBandChits => EffectiveBandRows * EffectiveBandColumns;

        private void ParseCommittedBandInputs(out int rows, out int cols)
        {
            int.TryParse(CommittedBandBox1?.Trim(), out int val1);
            int.TryParse(CommittedBandBox2?.Trim(), out int val2);

            if (val1 <= 0) val1 = 2;
            if (val2 <= 0) val2 = 10;

            if (val1 > val2)
            {
                rows = Math.Clamp(val2, 1, 20);
                cols = Math.Clamp(val1, 1, 30);
            }
            else
            {
                rows = Math.Clamp(val1, 1, 20);
                cols = Math.Clamp(val2, 1, 30);
            }
        }

        [RelayCommand]
        public void SetBandDimensions()
        {
            int.TryParse(PendingBandBox1?.Trim(), out int val1);
            int.TryParse(PendingBandBox2?.Trim(), out int val2);

            if (val1 <= 0)
            {
                int.TryParse(CommittedBandBox1?.Trim(), out val1);
                if (val1 <= 0) val1 = 2;
            }
            if (val2 <= 0)
            {
                int.TryParse(CommittedBandBox2?.Trim(), out val2);
                if (val2 <= 0) val2 = 10;
            }

            val1 = Math.Clamp(val1, 1, 30);
            val2 = Math.Clamp(val2, 1, 30);

            CommittedBandBox1 = val1.ToString();
            CommittedBandBox2 = val2.ToString();
            PendingBandBox1 = CommittedBandBox1;
            PendingBandBox2 = CommittedBandBox2;

            // Strict policy: Never auto-select or flip IsDoubleWidthColumns.
            // Double Width Columns is ONLY controlled by user clicks.
            ParseCommittedBandInputs(out int rows, out int cols);
            RecalculateBandBoundingBoxForDimensions(rows, cols);

            OnPropertyChanged(nameof(BandBoundingBoxHeader));
            NotifyBandDimensionsChanged();
        }

        partial void OnIsDoubleWidthColumnsChanged(bool value)
        {
            if (_isSyncingBand) return;

            ParseCommittedBandInputs(out int rows, out int cols);
            RecalculateBandBoundingBoxForDimensions(rows, cols);
            NotifyBandDimensionsChanged();
        }

        private void RecalculateBandBoundingBoxForDimensions(int rows, int cols)
        {
            var band = GetActiveBand();
            if (band == null) return;

            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;

            int safeRows = Math.Max(1, rows);
            int safeCols = Math.Max(1, cols);

            int currentHeightPx = ActiveBandHeightPx > 0 ? ActiveBandHeightPx : (int)Math.Round(band.Bounds.Height * h);
            if (currentHeightPx < 8) currentHeightPx = 8;

            // Default Row Height Split = Selected Height / Number of Rows (e.g. 50% for 2 rows)
            double defaultRowSplitRatio = 1.0 / safeRows;
            int targetRowSplitPx = Math.Max(2, (int)Math.Round(defaultRowSplitRatio * currentHeightPx));
            targetRowSplitPx = Math.Clamp(targetRowSplitPx, 2, Math.Max(2, currentHeightPx - 2));

            // Default Column Width Split = Selected Width / Number of Columns
            double cellHeight = (double)currentHeightPx / safeRows;
            double cellWidth = IsDoubleWidthColumns ? (2.0 * cellHeight) : cellHeight;

            int targetWidthPx = (int)Math.Round(safeCols * cellWidth);
            targetWidthPx = Math.Max(8, targetWidthPx);

            int currentLeftPx = ActiveBandLeftPx >= 0 ? ActiveBandLeftPx : (int)Math.Round(band.Bounds.Left * w);

            if (w > 0 && currentLeftPx + targetWidthPx > w)
            {
                targetWidthPx = Math.Max(8, w - currentLeftPx);
            }

            _isSyncingBand = true;
            try
            {
                ActiveBandWidthPx = targetWidthPx;
                ActiveBandWidthText = $"{ActiveBandWidthPx} px";
                band.Bounds.Width = Math.Clamp((double)targetWidthPx / w, 8.0 / w, 1.0 - band.Bounds.Left);
                ActiveBandWidthPercent = Math.Round(band.Bounds.Width * 100, 2);

                // Update row split ratio to default even distribution (e.g. 50.0% for 2 rows)
                band.RowSplitRatio = Math.Clamp((double)targetRowSplitPx / currentHeightPx, 0.05, 0.95);
                ActiveBandRowSplitPx = targetRowSplitPx;
                ActiveBandRowSplitText = $"{ActiveBandRowSplitPx} px";
                ActiveBandRowSplitPercent = Math.Round(band.RowSplitRatio * 100, 1);

                // Update column dividers to even spacing for the active column count
                band.ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(safeCols);

                OnPropertyChanged(nameof(ActiveBandWidthPx));
                OnPropertyChanged(nameof(ActiveBandWidthText));
                OnPropertyChanged(nameof(ActiveBandWidthPercent));
                OnPropertyChanged(nameof(ActiveBandWidthPercentText));
                OnPropertyChanged(nameof(ActiveBandRightPx));
                OnPropertyChanged(nameof(ActiveBandRowSplitPx));
                OnPropertyChanged(nameof(ActiveBandRowSplitText));
                OnPropertyChanged(nameof(ActiveBandRowSplitPercent));
                OnPropertyChanged(nameof(ActiveBandRowSplitPercentText));
                OnPropertyChanged(nameof(ActiveBandDimensionsText));
                OnPropertyChanged(nameof(ActiveBandDimensionsSummaryText));
            }
            finally
            {
                _isSyncingBand = false;
            }

            SyncToActiveBand();
        }

        private void NotifyBandDimensionsChanged()
        {
            OnPropertyChanged(nameof(EffectiveBandRows));
            OnPropertyChanged(nameof(EffectiveBandColumns));
            OnPropertyChanged(nameof(TotalBandChits));
            OnPropertyChanged(nameof(PreviewCellText));
            OnPropertyChanged(nameof(AvailableChitNumbers));

            int maxChits = TotalBandChits;
            if (PreviewChitNumber > maxChits)
            {
                PreviewChitNumber = maxChits;
            }
            else if (PreviewChitNumber < 1)
            {
                PreviewChitNumber = 1;
            }
            else
            {
                SyncPreviewRowColFromChitNumber(PreviewChitNumber);
            }

            UpdateChitPreview();
        }

        // Slicer Overlay Color & Swatches (3x3 palette: Black top-left, White bottom-right, 9 distinct colors)
        [ObservableProperty]
        private Color _slicerOverlayColor = Color.FromRgb(239, 68, 68); // Red (#EF4444) default

        public ObservableCollection<OverlayColorOption> AvailableOverlayColors { get; } = new()
        {
            new OverlayColorOption { Color = Color.FromRgb(0, 0, 0), Name = "Black", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(239, 68, 68), Name = "Red", IsSelected = true },
            new OverlayColorOption { Color = Color.FromRgb(249, 115, 22), Name = "Orange", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(234, 179, 8), Name = "Yellow", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(34, 197, 94), Name = "Lime/Green", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(6, 182, 212), Name = "Cyan", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(59, 130, 246), Name = "Blue", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(217, 70, 239), Name = "Magenta", IsSelected = false },
            new OverlayColorOption { Color = Color.FromRgb(255, 255, 255), Name = "White", IsSelected = false }
        };

        [RelayCommand]
        private void SelectOverlayColor(OverlayColorOption option)
        {
            if (option == null) return;
            SlicerOverlayColor = option.Color;
            foreach (var opt in AvailableOverlayColors)
            {
                opt.IsSelected = (opt == option);
            }
        }

        // Column Divider Fine-Tuning
        [ObservableProperty]
        private bool _isFineTuningDividers = false;

        // Interactive Dual-Corner Picking
        [ObservableProperty]
        private bool _isSettingUpperLeftCorner = false;

        [ObservableProperty]
        private bool _isSettingLowerRightCorner = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedDividerPositionText))]
        private int _selectedDividerIndex = 0; // 0 to 8 for Dividers 1 to 9

        public IReadOnlyList<string> AvailableDividerNames { get; } = new List<string>
        {
            "Divider 1 (Cols 1|2)",
            "Divider 2 (Cols 2|3)",
            "Divider 3 (Cols 3|4)",
            "Divider 4 (Cols 4|5)",
            "Divider 5 (Cols 5|6)",
            "Divider 6 (Cols 6|7)",
            "Divider 7 (Cols 7|8)",
            "Divider 8 (Cols 8|9)",
            "Divider 9 (Cols 9|10)"
        };

        public string SelectedDividerPositionText
        {
            get
            {
                var band = GetActiveBand();
                if (band?.ColumnDividers != null && SelectedDividerIndex >= 0 && SelectedDividerIndex < band.ColumnDividers.Count)
                {
                    return $"{band.ColumnDividers[SelectedDividerIndex] * 100.0:F1}%";
                }
                return "10.0%";
            }
        }

        // Single Chit Cut Preview Coordinates
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        private int _previewChitNumber = 1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        private int _previewRowIndex = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        private int _previewColIndex = 0;

        public IReadOnlyList<int> AvailableChitNumbers => Enumerable.Range(1, Math.Max(1, TotalBandChits)).ToList();
        public IReadOnlyList<int> AvailablePreviewRows { get; } = new List<int> { 1, 2 };
        public IReadOnlyList<int> AvailablePreviewCols { get; } = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        public int SelectedPreviewRowDisplay
        {
            get => PreviewRowIndex + 1;
            set
            {
                if (value >= 1 && value <= EffectiveBandRows)
                {
                    PreviewRowIndex = value - 1;
                }
            }
        }

        public int SelectedPreviewColDisplay
        {
            get => PreviewColIndex + 1;
            set
            {
                if (value >= 1 && value <= EffectiveBandColumns)
                {
                    PreviewColIndex = value - 1;
                }
            }
        }

        public string PreviewCellText
        {
            get
            {
                if (IsBandedMode)
                {
                    int total = TotalBandChits;
                    int row = PreviewRowIndex + 1;
                    int col = PreviewColIndex + 1;
                    return $"Tile {PreviewChitNumber} of {total} — Row {row}, Col {col}";
                }
                return "Cell (Row 1, Col 1) with red cut boundary";
            }
        }

        [ObservableProperty]
        private double _inwardInsetPx = 4.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFrontSide))]
        [NotifyPropertyChangedFor(nameof(IsBackSide))]
        private string _selectedSide = "Front";

        public bool IsFrontSide
        {
            get => SelectedSide.Equals("Front", StringComparison.OrdinalIgnoreCase);
            set { if (value) SelectedSide = "Front"; }
        }

        public bool IsBackSide
        {
            get => IsRearSide(SelectedSide);
            set { if (value) SelectedSide = "Rear"; }
        }

        [ObservableProperty]
        private BackOrientationMode _selectedOrientation = BackOrientationMode.HorizontalFlip;

        [ObservableProperty]
        private string _namingPrefix = string.Empty;

        public IReadOnlyList<string> SlicerAvailableSides { get; } = new List<string> { "Front", "Rear" };
        public IReadOnlyList<BackOrientationMode> AvailableOrientations { get; } = Enum.GetValues<BackOrientationMode>();

        // Zoomed Chit Preview
        [ObservableProperty]
        private BitmapSource? _chitPreviewImage;

        // Batch Slicing Execution State
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SliceGridCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceAllBandsCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceActiveBandCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendBandToChitEditorCommand))]
        private bool _isSlicing = false;

        [ObservableProperty]
        private int _sliceProgressPercent = 0;

        [ObservableProperty]
        private string _sliceProgressText = string.Empty;

        #endregion

        public MainViewModel()
        {
            // Initialize Sheet Profiles
            var defaultProfiles = ProfileRepository.GetDefaultProfiles();
            foreach (var p in defaultProfiles)
            {
                AvailableProfiles.Add(p);
            }

            SelectedProfile = AvailableProfiles.FirstOrDefault();
            SyncFromActiveBand();

            LoadedChits.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasChitsLoaded));
                OnPropertyChanged(nameof(CanCloseTiles));
                OnPropertyChanged(nameof(HasActiveDocument));
                CloseTilesCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
                ExportAllEditedChitsCommand.NotifyCanExecuteChanged();
                ResetAllChitsCommand.NotifyCanExecuteChanged();
                RotateAllTilesLeftCommand.NotifyCanExecuteChanged();
                RotateAllTilesRightCommand.NotifyCanExecuteChanged();
                BevelAllCommand.NotifyCanExecuteChanged();
                ClipAllCommand.NotifyCanExecuteChanged();
                ApplySharedToolAllCommand.NotifyCanExecuteChanged();
                SmoothAllTilesCommand.NotifyCanExecuteChanged();
                DespeckleAllTilesCommand.NotifyCanExecuteChanged();
                SharpenAllTilesCommand.NotifyCanExecuteChanged();
                AutoTrimAllTilesCommand.NotifyCanExecuteChanged();
            };

            LoadRecentPdfs();
        }

        #region Recent PDFs Management

        public ObservableCollection<RecentPdfItem> RecentPdfs { get; } = new();

        public bool HasRecentPdfs => RecentPdfs.Count > 0;

        private void LoadRecentPdfs()
        {
            try
            {
                string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pdf-to-Tiles");
                string recentFile = Path.Combine(settingsDir, "recent_pdfs.txt");
                if (File.Exists(recentFile))
                {
                    var lines = File.ReadAllLines(recentFile);
                    RecentPdfs.Clear();
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && RecentPdfs.Count < 5)
                        {
                            RecentPdfs.Add(new RecentPdfItem { FilePath = trimmed });
                        }
                    }
                    OnPropertyChanged(nameof(HasRecentPdfs));
                }
            }
            catch
            {
                // Graceful fallback
            }
        }

        private void SaveRecentPdfs()
        {
            try
            {
                string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pdf-to-Tiles");
                Directory.CreateDirectory(settingsDir);
                string recentFile = Path.Combine(settingsDir, "recent_pdfs.txt");
                File.WriteAllLines(recentFile, RecentPdfs.Select(r => r.FilePath));
            }
            catch
            {
                // Graceful fallback
            }
        }

        public void AddRecentPdf(string filePath)
        {
            try
            {
                var existing = RecentPdfs.FirstOrDefault(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    RecentPdfs.Remove(existing);
                }
                RecentPdfs.Insert(0, new RecentPdfItem { FilePath = filePath });
                while (RecentPdfs.Count > 5)
                {
                    RecentPdfs.RemoveAt(RecentPdfs.Count - 1);
                }
                OnPropertyChanged(nameof(HasRecentPdfs));
                SaveRecentPdfs();
            }
            catch
            {
                // Graceful fallback
            }
        }

        [RelayCommand]
        private async Task OpenRecentPdfAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (!File.Exists(filePath))
            {
                StatusMessage = $"Recent PDF file not found: {filePath}";
                var missing = RecentPdfs.FirstOrDefault(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (missing != null)
                {
                    RecentPdfs.Remove(missing);
                    OnPropertyChanged(nameof(HasRecentPdfs));
                    SaveRecentPdfs();
                }
                return;
            }

            await LoadPdfFromPathAsync(filePath);
        }

        #endregion

        #region PDF Viewer Handlers

        private bool _suppressExplicitPagePick;

        partial void OnCurrentPageNumberChanged(int value)
        {
            if (!_suppressExplicitPagePick && HasLoadedPdf)
            {
                HasExplicitlyPickedPage = true;
            }
            if (HasLoadedPdf && value >= 1 && value <= TotalPages)
            {
                _ = LoadPageAsync(value);
            }
        }

        partial void OnSelectedSideTagChanged(string value)
        {
            if (!_suppressExplicitPagePick && HasLoadedPdf)
            {
                HasExplicitlyPickedPage = true;
            }
            OnPropertyChanged(nameof(IsFrontOrBackTagSelected));
            OnPropertyChanged(nameof(CanSendPageToSlicer));
            SendPageToSlicerCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanExecuteActions))]
        private async Task OpenPdfAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Open PDF Rulebook or Counter Sheet",
                Filter = "PDF Documents (*.pdf)|*.pdf|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            await LoadPdfFromPathAsync(openFileDialog.FileName);
        }

        public async Task LoadPdfFromPathAsync(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath)) return;

            IsLoading = true;
            StatusMessage = $"Loading {Path.GetFileName(selectedPath)} into memory (non-locking)...";

            try
            {
                byte[] bytes = await Task.Run(() => PdfPageRenderer.LoadPdfBytesNonLocking(selectedPath));
                int pageCount = await Task.Run(() => PdfPageRenderer.GetPageCount(bytes));

                if (pageCount <= 0)
                {
                    StatusMessage = "Error: Selected PDF contains no pages or is invalid.";
                    return;
                }

                _pdfBytes = bytes;
                LoadedFilePath = selectedPath;
                LoadedFileName = Path.GetFileName(selectedPath);
                TotalPages = pageCount;
                WindowTitle = $"{AppBaseTitle} - {LoadedFileName}";

                string pdfBaseName = Path.GetFileNameWithoutExtension(selectedPath);
                if (!string.IsNullOrWhiteSpace(pdfBaseName))
                {
                    NamingPrefix = pdfBaseName;
                    if (SelectedProfile != null)
                    {
                        SelectedProfile.NamingPrefix = pdfBaseName;
                    }

                    if (LoadedChits.Count > 0)
                    {
                        foreach (var chit in LoadedChits)
                        {
                            if (chit.PieceName.StartsWith("WorldKiller_", StringComparison.OrdinalIgnoreCase))
                            {
                                chit.PieceName = $"{pdfBaseName}{chit.PieceName["WorldKiller".Length..]}";
                            }
                        }
                    }
                }

                _suppressExplicitPagePick = true;
                try
                {
                    AddRecentPdf(selectedPath);
                    SelectedTabIndex = 0; // Ensure Tab 1 is active
                    CurrentPageNumber = 1;
                    SelectedSideTag = "Page";
                    await LoadPageAsync(1);
                    HasExplicitlyPickedPage = false;
                }
                finally
                {
                    _suppressExplicitPagePick = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open PDF: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasLoadedPdf));
                OnPropertyChanged(nameof(CanClosePdf));
                OnPropertyChanged(nameof(HasActiveDocument));
                OnPropertyChanged(nameof(CanSendPageToSlicer));
                SendPageToSlicerCommand.NotifyCanExecuteChanged();
                ClosePdfCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
            }
        }

        public async Task LoadPageAsync(int pageNumber)
        {
            if (_pdfBytes == null || _pdfBytes.Length == 0 || pageNumber < 1 || pageNumber > TotalPages)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = $"Rendering preview for page {pageNumber} of {TotalPages} (150 DPI)...";

            try
            {
                int pageIndex = pageNumber - 1;
                BitmapSource preview = await Task.Run(() => PdfPageRenderer.RenderPagePreview(_pdfBytes, pageIndex, dpi: 150));

                CurrentPageImage = preview;
                PagePixelWidth = preview.PixelWidth;
                PagePixelHeight = preview.PixelHeight;

                if (IsFitToPage && GetPdfViewportSize != null)
                {
                    var (width, height) = GetPdfViewportSize();
                    RecalculatePdfFitToPage(width, height);
                }

                StatusMessage = $"Page {pageNumber} of {TotalPages} loaded ({PagePixelWidth} × {PagePixelHeight} px preview @ 150 DPI).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error rendering page {pageNumber}: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void RecalculatePdfFitToPage(double viewportWidth, double viewportHeight)
        {
            if (CurrentPageImage == null || CurrentPageImage.PixelWidth <= 0 || CurrentPageImage.PixelHeight <= 0)
            {
                return;
            }

            double availableWidth = Math.Max(20, viewportWidth - 40);
            double availableHeight = Math.Max(20, viewportHeight - 40);

            double scaleX = availableWidth / CurrentPageImage.PixelWidth;
            double scaleY = availableHeight / CurrentPageImage.PixelHeight;
            double fitScale = Math.Min(scaleX, scaleY);

            if (fitScale > 0.01)
            {
                ZoomScale = Math.Round(fitScale, 4);
            }
        }

        [RelayCommand(CanExecute = nameof(CanGoPrevious))]
        private void FirstPage()
        {
            HasExplicitlyPickedPage = true;
            if (CurrentPageNumber > 1)
            {
                CurrentPageNumber = 1;
            }
        }

        [RelayCommand(CanExecute = nameof(CanGoPrevious))]
        private void PreviousPage()
        {
            HasExplicitlyPickedPage = true;
            if (CurrentPageNumber > 1)
            {
                CurrentPageNumber--;
            }
        }

        private bool CanGoPrevious() => !IsLoading && HasLoadedPdf && CurrentPageNumber > 1;

        [RelayCommand(CanExecute = nameof(CanGoNext))]
        private void NextPage()
        {
            HasExplicitlyPickedPage = true;
            if (CurrentPageNumber < TotalPages)
            {
                CurrentPageNumber++;
            }
        }

        [RelayCommand(CanExecute = nameof(CanGoNext))]
        private void LastPage()
        {
            HasExplicitlyPickedPage = true;
            if (CurrentPageNumber < TotalPages)
            {
                CurrentPageNumber = TotalPages;
            }
        }

        private bool CanGoNext() => !IsLoading && HasLoadedPdf && CurrentPageNumber < TotalPages;

        [RelayCommand]
        private void FitToPage()
        {
            IsFitToPage = true;
            if (GetPdfViewportSize != null)
            {
                var (width, height) = GetPdfViewportSize();
                RecalculatePdfFitToPage(width, height);
            }
        }

        [RelayCommand]
        private void ActualSize()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    IsFitToPage = false;
                    ZoomScale = 1.0;
                    break;
                case 1:
                    SlicerActualSize();
                    break;
                case 2:
                    ChitEditorZoomReset();
                    break;
            }
        }

        [RelayCommand]
        private void FitWidth()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    if (GetPdfViewportSize != null && PagePixelWidth > 0)
                    {
                        var (width, _) = GetPdfViewportSize();
                        if (width > 40)
                        {
                            IsFitToPage = false;
                            ZoomScale = Math.Clamp(Math.Round((width - 40) / PagePixelWidth, 2), 0.05, 5.0);
                        }
                    }
                    break;
                case 1:
                    SlicerFitToWidth();
                    break;
                case 2:
                    ChitEditorZoomReset();
                    break;
            }
        }

        [RelayCommand]
        private void FitView()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    FitToPage();
                    break;
                case 1:
                    SlicerFitToPage();
                    break;
                case 2:
                    ChitEditorZoomReset();
                    break;
            }
        }

        [RelayCommand]
        private void SwitchTab(object? parameter)
        {
            if (parameter is int intVal)
            {
                SelectedTabIndex = Math.Clamp(intVal, 0, 2);
            }
            else if (parameter is string strVal && int.TryParse(strVal, out int parsed))
            {
                SelectedTabIndex = Math.Clamp(parsed, 0, 2);
            }
        }

        private bool CanCloseDocument() => HasActiveDocument;

        [RelayCommand(CanExecute = nameof(CanCloseDocument))]
        private void CloseDocument()
        {
            try
            {
                // 1. Dispose & clear PDF Viewer state
                _pdfBytes = null;
                LoadedFilePath = null;
                LoadedFileName = null;
                CurrentPageImage = null;
                TotalPages = 0;
                CurrentPageNumber = 0;
                SelectedSideTag = "Page";
                HasExplicitlyPickedPage = false;
                PagePixelWidth = 0;
                PagePixelHeight = 0;
                IsFitToPage = true;
                ZoomScale = 1.0;

                // 2. Dispose & clear Deskew and Grid Slicer state
                _masterMat?.Dispose();
                _masterMat = null;
                _deskewedMat?.Dispose();
                _deskewedMat = null;
                _preEdgeCleanMat?.Dispose();
                _preEdgeCleanMat = null;
                _sliceCts?.Cancel();
                _sliceCts?.Dispose();
                _sliceCts = null;

                SlicerImage = null;
                SlicerSourceFileName = null;
                SlicerPixelWidth = 0;
                SlicerPixelHeight = 0;
                DeskewAngle = 0.0;
                IsSettingUpperLeftCorner = false;
                IsSettingLowerRightCorner = false;

                // 3. Dispose & clear Chit Editor state
                if (SelectedChit != null)
                {
                    SelectedChit.PropertyChanged -= SelectedChit_PropertyChanged;
                    SelectedChit = null;
                }

                foreach (var chit in LoadedChits)
                {
                    chit.PropertyChanged -= OnChitItemPropertyChanged;
                    chit.Dispose();
                }
                LoadedChits.Clear();
                _batchEditHistory.Clear();
                _batchEditRedoHistory.Clear();

                _lastCommittedSignature = null;
                ChitEditorWidth = 0;
                ChitEditorHeight = 0;
                ChitEditorZoomScale = 1.0;
                IsCloneStampActive = false;
                ResetCloneStampAnchor();

                // 4. Reset Window Title and Status
                WindowTitle = AppBaseTitle;
                StatusMessage = "Document closed. Ready. Open a PDF rulebook or counter sheet to begin.";
                SelectedTabIndex = 0;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing document: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(HasLoadedPdf));
                OnPropertyChanged(nameof(HasSlicerImage));
                OnPropertyChanged(nameof(HasChitsLoaded));
                OnPropertyChanged(nameof(HasSelectedChit));
                OnPropertyChanged(nameof(HasActiveDocument));
                OnPropertyChanged(nameof(CanClosePdf));
                OnPropertyChanged(nameof(CanCloseTilePageImage));
                OnPropertyChanged(nameof(CanCloseTiles));
                OnPropertyChanged(nameof(CanUndoChit));
                OnPropertyChanged(nameof(CanRedoChit));
                OnPropertyChanged(nameof(ChitStatusBarText));
                ClosePdfCommand.NotifyCanExecuteChanged();
                CloseTilePageImageCommand.NotifyCanExecuteChanged();
                CloseTilesCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
                ExportPageToPngCommand.NotifyCanExecuteChanged();
                SliceGridCommand.NotifyCanExecuteChanged();
                RotateLeft90Command.NotifyCanExecuteChanged();
                RotateRight90Command.NotifyCanExecuteChanged();
                ExportAllEditedChitsCommand.NotifyCanExecuteChanged();
                ResetAllChitsCommand.NotifyCanExecuteChanged();
                RotateAllTilesLeftCommand.NotifyCanExecuteChanged();
                RotateAllTilesRightCommand.NotifyCanExecuteChanged();
                BevelAllCommand.NotifyCanExecuteChanged();
                ClipAllCommand.NotifyCanExecuteChanged();
                ApplySharedToolAllCommand.NotifyCanExecuteChanged();

                // Explicit garbage collection to release unmanaged buffers
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [RelayCommand(CanExecute = nameof(CanClosePdf))]
        private void ClosePdf()
        {
            try
            {
                _pdfBytes = null;
                LoadedFilePath = null;
                LoadedFileName = null;
                CurrentPageImage = null;
                TotalPages = 0;
                CurrentPageNumber = 0;
                SelectedSideTag = "Page";
                HasExplicitlyPickedPage = false;
                PagePixelWidth = 0;
                PagePixelHeight = 0;
                IsFitToPage = true;
                ZoomScale = 1.0;

                if (HasSlicerImage && !string.IsNullOrWhiteSpace(SlicerSourceFileName))
                {
                    WindowTitle = $"{AppBaseTitle} - {SlicerSourceFileName}";
                }
                else
                {
                    WindowTitle = AppBaseTitle;
                }

                StatusMessage = "PDF closed. Ready. Open a PDF rulebook or counter sheet to begin.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing PDF: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(HasLoadedPdf));
                OnPropertyChanged(nameof(CanSendPageToSlicer));
                OnPropertyChanged(nameof(CanClosePdf));
                OnPropertyChanged(nameof(CanSaveTilePageImage));
                OnPropertyChanged(nameof(HasActiveDocument));
                OnPropertyChanged(nameof(ChitStatusBarText));

                ClosePdfCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
                FirstPageCommand.NotifyCanExecuteChanged();
                PreviousPageCommand.NotifyCanExecuteChanged();
                NextPageCommand.NotifyCanExecuteChanged();
                LastPageCommand.NotifyCanExecuteChanged();
                CapturePageCommand.NotifyCanExecuteChanged();
                SendPageToSlicerCommand.NotifyCanExecuteChanged();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [RelayCommand(CanExecute = nameof(CanCloseTilePageImage))]
        private void CloseTilePageImage()
        {
            try
            {
                _sliceCts?.Cancel();
                _sliceCts?.Dispose();
                _sliceCts = null;

                _masterMat?.Dispose();
                _masterMat = null;
                _deskewedMat?.Dispose();
                _deskewedMat = null;
                _preEdgeCleanMat?.Dispose();
                _preEdgeCleanMat = null;

                SlicerImage = null;
                ChitPreviewImage = null;
                SlicerSourceFileName = null;
                SlicerPixelWidth = 0;
                SlicerPixelHeight = 0;
                DeskewAngle = 0.0;
                IsSettingUpperLeftCorner = false;
                IsSettingLowerRightCorner = false;

                if (!HasLoadedPdf)
                {
                    WindowTitle = AppBaseTitle;
                }

                StatusMessage = "Tile page image closed. Ready for next counter sheet.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing tile page image: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(HasSlicerImage));
                OnPropertyChanged(nameof(CanCloseTilePageImage));
                OnPropertyChanged(nameof(CanSaveTilePageImage));
                OnPropertyChanged(nameof(HasActiveDocument));
                OnPropertyChanged(nameof(ChitStatusBarText));

                CloseTilePageImageCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
                AutoDetectTiltCommand.NotifyCanExecuteChanged();
                SliceGridCommand.NotifyCanExecuteChanged();
                SliceAllBandsCommand.NotifyCanExecuteChanged();
                SliceActiveBandCommand.NotifyCanExecuteChanged();
                SendBandToChitEditorCommand.NotifyCanExecuteChanged();
                RotateLeft90Command.NotifyCanExecuteChanged();
                RotateRight90Command.NotifyCanExecuteChanged();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [RelayCommand(CanExecute = nameof(CanCloseTiles))]
        private void CloseTiles()
        {
            try
            {
                DeactivateCloneStamp();

                if (SelectedChit != null)
                {
                    SelectedChit.PropertyChanged -= SelectedChit_PropertyChanged;
                    SelectedChit = null;
                }

                foreach (var chit in LoadedChits)
                {
                    chit.PropertyChanged -= OnChitItemPropertyChanged;
                    chit.Dispose();
                }
                LoadedChits.Clear();
                _batchEditHistory.Clear();
                _batchEditRedoHistory.Clear();

                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                ChitEditorWidth = 0;
                ChitEditorHeight = 0;
                ChitEditorZoomScale = 1.0;
                IsCloneStampActive = false;
                ResetCloneStampAnchor();

                StatusMessage = "Tiles closed. Ready to slice or open tile files.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing tiles: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(HasChitsLoaded));
                OnPropertyChanged(nameof(HasSelectedChit));
                OnPropertyChanged(nameof(HasSelectedChits));
                OnPropertyChanged(nameof(CanCloseTiles));
                OnPropertyChanged(nameof(CanSaveAllTiles));
                OnPropertyChanged(nameof(CanSaveCurrentTile));
                OnPropertyChanged(nameof(HasActiveDocument));
                OnPropertyChanged(nameof(CanUndoChit));
                OnPropertyChanged(nameof(CanRedoChit));
                OnPropertyChanged(nameof(ChitStatusBarText));
                NotifySelectionChanges();

                CloseTilesCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
                SaveAllTilesCommand.NotifyCanExecuteChanged();
                SaveCurrentTileCommand.NotifyCanExecuteChanged();
                ExportAllEditedChitsCommand.NotifyCanExecuteChanged();
                ResetAllChitsCommand.NotifyCanExecuteChanged();
                RotateAllTilesLeftCommand.NotifyCanExecuteChanged();
                RotateAllTilesRightCommand.NotifyCanExecuteChanged();
                BevelAllCommand.NotifyCanExecuteChanged();
                ClipAllCommand.NotifyCanExecuteChanged();
                ApplySharedToolAllCommand.NotifyCanExecuteChanged();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [RelayCommand]
        private void ExitApplication()
        {
            CloseDocument();
            System.Windows.Application.Current?.Shutdown();
        }

        [RelayCommand]
        private void ShowAbout()
        {
            var aboutWin = new Views.AboutWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            aboutWin.ShowDialog();
        }

        [RelayCommand]
        private void ShowShortcuts()
        {
            var shortcutsWin = new Views.ShortcutsWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            shortcutsWin.ShowDialog();
        }

        [RelayCommand]
        private void ZoomIn()
        {
            IsFitToPage = false;
            ZoomScale = Math.Min(Math.Round(ZoomScale + 0.01, 2), 5.0);
        }

        [RelayCommand]
        private void ZoomOut()
        {
            IsFitToPage = false;
            ZoomScale = Math.Max(Math.Round(ZoomScale - 0.01, 2), 0.05);
        }

        [RelayCommand(CanExecute = nameof(CanCapturePage))]
        private async Task CapturePageAsync()
        {
            if (_pdfBytes == null || CurrentPageNumber < 1 || CurrentPageNumber > TotalPages)
            {
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(LoadedFilePath) ?? "Page";
            string defaultFileName = $"{baseName}_p{CurrentPageNumber:D2}_{SelectedSideTag}_{SelectedDpi}dpi.png";
            string initialDir = !string.IsNullOrEmpty(LoadedFilePath) ? Path.GetDirectoryName(LoadedFilePath)! : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            var saveFileDialog = new SaveFileDialog
            {
                Title = $"Capture Page {CurrentPageNumber} as Lossless PNG ({SelectedDpi} DPI)",
                Filter = "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                FileName = defaultFileName,
                InitialDirectory = initialDir,
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            string outputPath = saveFileDialog.FileName;
            IsLoading = true;
            StatusMessage = $"Capturing page {CurrentPageNumber} at {SelectedDpi} DPI directly to lossless PNG...";

            try
            {
                int pageIndex = CurrentPageNumber - 1;
                int dpi = SelectedDpi;
                await Task.Run(() => PdfPageRenderer.ExportPageToPng(_pdfBytes, pageIndex, dpi, outputPath));

                StatusMessage = $"Successfully captured: {outputPath} ({dpi} DPI)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to capture page as PNG: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanCapturePage() => !IsLoading && HasLoadedPdf;
        private bool CanExecuteActions() => !IsLoading;

        [RelayCommand]
        private async Task SaveTilePageImageInternalAsync()
        {
            if (_deskewedMat != null && !_deskewedMat.IsDisposed)
            {
                string rawBaseName = !string.IsNullOrWhiteSpace(LoadedFilePath)
                    ? Path.GetFileNameWithoutExtension(LoadedFilePath)
                    : (!string.IsNullOrWhiteSpace(SlicerSourceFileName) && !SlicerSourceFileName.StartsWith("PdfToTiles_Capture_", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(SlicerSourceFileName)
                        : "PdfToTiles");

                char[] invalidChars = Path.GetInvalidFileNameChars();
                string pdfBaseName = string.Concat(rawBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (string.IsNullOrWhiteSpace(pdfBaseName))
                {
                    pdfBaseName = "PdfToTiles";
                }

                string side = !string.IsNullOrWhiteSpace(SelectedSide) && (SelectedSide.Equals("Front", StringComparison.OrdinalIgnoreCase) || IsRearSide(SelectedSide))
                    ? SelectedSide
                    : SelectedSideTag;

                string defaultFileName;
                if (IsRearSide(side))
                {
                    defaultFileName = $"{pdfBaseName} Tiles Rear.png";
                }
                else if (string.Equals(side, "Front", StringComparison.OrdinalIgnoreCase))
                {
                    defaultFileName = $"{pdfBaseName} Tiles Front.png";
                }
                else if (CurrentPageNumber > 0)
                {
                    defaultFileName = $"{pdfBaseName} Tiles Page {CurrentPageNumber}.png";
                }
                else
                {
                    defaultFileName = $"{pdfBaseName} Tiles.png";
                }

                string initialDir = !string.IsNullOrEmpty(LoadedFilePath)
                    ? Path.GetDirectoryName(LoadedFilePath)!
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Save Tile Page Image as Lossless PNG",
                    Filter = "PNG Image (*.png)|*.png",
                    DefaultExt = ".png",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = defaultFileName,
                    InitialDirectory = initialDir
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        Cv2.ImWrite(saveFileDialog.FileName, _deskewedMat);
                        StatusMessage = $"Saved tile page image to {saveFileDialog.FileName}";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Failed to save tile page image: {ex.Message}";
                        MessageBox.Show(
                            $"Failed to save tile page image:\n{ex.Message}",
                            "Save Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            else if (CanCapturePage())
            {
                await CapturePageAsync();
            }
        }

        [RelayCommand(CanExecute = nameof(CanOpenTileFiles))]
        private async Task OpenTileFilesAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Open Tile Image Files",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|PNG Files (*.png)|*.png|All Files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true || openFileDialog.FileNames.Length == 0)
            {
                return;
            }

            string[] fileNames = openFileDialog.FileNames;

            // Natural alphanumeric sort so numbered files (e.g., Tile_1, Tile_2, Tile_10) load in human order
            var sortedFiles = fileNames
                .OrderBy(f => Regex.Replace(Path.GetFileName(f), @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)
                .ToList();

            IsLoading = true;
            StatusMessage = $"Loading {sortedFiles.Count} tile files...";

            try
            {
                var loadedChits = await Task.Run(() =>
                {
                    var chits = new List<ChitItem>();
                    for (int i = 0; i < sortedFiles.Count; i++)
                    {
                        string filePath = sortedFiles[i];
                        try
                        {
                            Mat rawMat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                            if (rawMat == null || rawMat.Empty())
                            {
                                rawMat = Cv2.ImRead(filePath, ImreadModes.Color);
                            }

                            if (rawMat == null || rawMat.Empty() || rawMat.Width < 8 || rawMat.Height < 8)
                            {
                                rawMat?.Dispose();
                                continue;
                            }

                            Mat bgraMat = EnsureBgraMat(rawMat);

                            string fileName = Path.GetFileName(filePath);
                            string side = (fileName.Contains("_Rear", StringComparison.OrdinalIgnoreCase) || fileName.Contains("_Back", StringComparison.OrdinalIgnoreCase)) ? "Rear" : "Front";

                            // Attempt to parse row/col from standard naming conventions (e.g. R01_C02)
                            int row = 1;
                            int col = i + 1;
                            var coordMatch = Regex.Match(fileName, @"[_\b]R(?<r>\d+)[_\b]C(?<c>\d+)", RegexOptions.IgnoreCase);
                            if (coordMatch.Success &&
                                int.TryParse(coordMatch.Groups["r"].Value, out int parsedR) &&
                                int.TryParse(coordMatch.Groups["c"].Value, out int parsedC))
                            {
                                row = parsedR;
                                col = parsedC;
                            }

                            var chit = new ChitItem(
                                index: i + 1,
                                row: row,
                                col: col,
                                side: side,
                                pieceName: fileName,
                                bgraMat: bgraMat
                            );

                            chits.Add(chit);
                        }
                        catch
                        {
                            // Skip corrupted / unreadable files safely
                        }
                    }
                    return chits;
                });

                if (loadedChits.Count == 0)
                {
                    StatusMessage = "No valid tile images could be loaded from the selected files.";
                    return;
                }

                // Follow the exact same transition / activation flow as clicking Next from Tile Creator:
                DeactivateCloneStamp();
                _batchEditHistory.Clear();
                _batchEditRedoHistory.Clear();

                // Safe teardown: detach SelectedChit first, unhook handlers, and dispose existing native mats
                SelectedChit = null;
                foreach (var oldChit in LoadedChits)
                {
                    oldChit.PropertyChanged -= OnChitItemPropertyChanged;
                    oldChit.Dispose();
                }
                LoadedChits.Clear();

                foreach (var chit in loadedChits)
                {
                    chit.IsSelected = true;
                    chit.PropertyChanged += OnChitItemPropertyChanged;
                    LoadedChits.Add(chit);
                }

                _isSyncingTileSelection = true;
                try
                {
                    IsAllTilesSelected = true;
                }
                finally
                {
                    _isSyncingTileSelection = false;
                }
                NotifySelectionChanges();

                SelectedChit = LoadedChits[0];
                SelectedChit.IsSelected = true;

                SelectedTabIndex = 2; // Switch directly to Tab 3: Tile Editor
                StatusMessage = $"Loaded {LoadedChits.Count} tile files into Tile Editor.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load tile files: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Phase 2 & 2.5: Slicer & Deskew Handlers

        [RelayCommand(CanExecute = nameof(CanSendPageToSlicer))]
        private async Task SendPageToSlicerAsync()
        {
            if (_pdfBytes == null || CurrentPageNumber < 1 || CurrentPageNumber > TotalPages)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = $"Rendering page {CurrentPageNumber} at {SelectedDpi} DPI for Slicer...";

            try
            {
                int pageIndex = CurrentPageNumber - 1;
                int dpi = SelectedDpi;

                string tempPngPath = Path.Combine(Path.GetTempPath(), $"PdfToTiles_Capture_{Guid.NewGuid():N}.png");
                await Task.Run(() => PdfPageRenderer.ExportPageToPng(_pdfBytes, pageIndex, dpi, tempPngPath));

                await LoadImageFileForSlicingAsync(tempPngPath, isTempFile: true);
                SelectedTabIndex = 1; // Switch tab to Deskew & Grid Slicer

                // Automatically populate Front/Rear if Tag is Front or Rear/Back
                if (IsRearSide(SelectedSideTag))
                {
                    SelectedSide = "Rear";
                }
                else if (SelectedSideTag.Equals("Front", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedSide = "Front";
                }

                // Enforce Fit to Width on transfer from PDF Viewer
                IsSlicerFitToWidth = true;
                IsSlicerFitToPage = false;
                if (GetSlicerViewportSize != null)
                {
                    var (vWidth, _) = GetSlicerViewportSize();
                    if (vWidth > 50)
                    {
                        RecalculateSlicerFitToWidth(vWidth);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to send page to Slicer: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task OpenImageForSlicingAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Open Counter Sheet Image for Deskew & Slicing",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            await LoadImageFileForSlicingAsync(openFileDialog.FileName, isTempFile: false);
        }

        public async Task LoadImageFileForSlicingAsync(string filePath, bool isTempFile)
        {
            IsLoading = true;
            StatusMessage = $"Loading {Path.GetFileName(filePath)}...";

            try
            {
                await Task.Run(() =>
                {
                    _masterMat?.Dispose();
                    _deskewedMat?.Dispose();

                    _masterMat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                    if (_masterMat == null || _masterMat.Empty())
                    {
                        _masterMat = Cv2.ImRead(filePath, ImreadModes.Color);
                    }
                    if (_masterMat == null || _masterMat.Empty())
                    {
                        throw new InvalidOperationException("Failed to read image buffer.");
                    }
                    _masterMat = EnsureBgraMat(_masterMat);

                    if (isTempFile)
                    {
                        try { File.Delete(filePath); } catch { /* ignore temp cleanup */ }
                    }
                });

                SlicerSourceFileName = Path.GetFileName(filePath);
                if (!isTempFile && !string.IsNullOrWhiteSpace(filePath))
                {
                    string imgBase = Path.GetFileNameWithoutExtension(filePath);
                    if (!string.IsNullOrWhiteSpace(imgBase))
                    {
                        NamingPrefix = imgBase;
                        if (SelectedProfile != null)
                        {
                            SelectedProfile.NamingPrefix = imgBase;
                        }
                    }
                }
                SlicerPixelWidth = _masterMat!.Width;
                SlicerPixelHeight = _masterMat!.Height;

                DeskewAngle = 0.0;
                RotateLeft90Command.NotifyCanExecuteChanged();
                RotateRight90Command.NotifyCanExecuteChanged();
                await ApplyDeskewRotationAsync();
                SyncFromActiveBand();

                if (IsSlicerFitToWidth && GetSlicerViewportSize != null)
                {
                    var (width, _) = GetSlicerViewportSize();
                    if (width > 0)
                    {
                        RecalculateSlicerFitToWidth(width);
                    }
                }
                else if (IsSlicerFitToPage && GetSlicerViewportSize != null)
                {
                    var (width, height) = GetSlicerViewportSize();
                    if (width > 0 && height > 0)
                    {
                        RecalculateSlicerFitToPage(width, height);
                    }
                }

                StatusMessage = $"Loaded {SlicerSourceFileName} ({SlicerPixelWidth} × {SlicerPixelHeight} px). Ready for deskew & slicing.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load image for slicing: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasSlicerImage));
                OnPropertyChanged(nameof(CanCloseTilePageImage));
                OnPropertyChanged(nameof(HasActiveDocument));
                CloseTilePageImageCommand.NotifyCanExecuteChanged();
                CloseDocumentCommand.NotifyCanExecuteChanged();
            }
        }

        public Band2x10Definition? GetActiveBand()
        {
            if (SelectedProfile == null) return null;
            var bands = IsRearSide(SelectedSide)
                ? SelectedProfile.BackBands
                : SelectedProfile.FrontBands;

            if (bands == null || bands.Count == 0) return null;
            int idx = Math.Clamp(ActiveBandIndex, 0, bands.Count - 1);
            return bands[idx];
        }

        private void SyncFromActiveBand()
        {
            var bands = IsRearSide(SelectedSide)
                ? SelectedProfile?.BackBands
                : SelectedProfile?.FrontBands;

            TotalBands = bands != null && bands.Count > 0 ? bands.Count : 5;
            if (ActiveBandIndex >= TotalBands)
            {
                ActiveBandIndex = Math.Max(0, TotalBands - 1);
            }

            var band = GetActiveBand();
            if (band == null) return;

            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;

            _isSyncingBand = true;
            try
            {
                ActiveBandName = band.Name;
                ActiveBandLeftPercent = Math.Round(band.Bounds.Left * 100, 2);
                ActiveBandTopPercent = Math.Round(band.Bounds.Top * 100, 2);
                ActiveBandWidthPercent = Math.Round(band.Bounds.Width * 100, 2);
                ActiveBandHeightPercent = Math.Round(band.Bounds.Height * 100, 2);
                ActiveBandRowSplitPercent = Math.Round(band.RowSplitRatio * 100, 1);

                ActiveBandLeftPx = (int)Math.Round(band.Bounds.Left * w);
                ActiveBandTopPx = (int)Math.Round(band.Bounds.Top * h);
                ActiveBandWidthPx = Math.Max(20, (int)Math.Round(band.Bounds.Width * w));
                ActiveBandHeightPx = Math.Max(4, (int)Math.Round(band.Bounds.Height * h));
                ActiveBandRowSplitPx = Math.Max(2, (int)Math.Round(band.RowSplitRatio * ActiveBandHeightPx));

                ActiveBandLeftText = $"{ActiveBandLeftPx} px";
                ActiveBandTopText = $"{ActiveBandTopPx} px";
                ActiveBandWidthText = $"{ActiveBandWidthPx} px";
                ActiveBandHeightText = $"{ActiveBandHeightPx} px";
                ActiveBandRowSplitText = $"{ActiveBandRowSplitPx} px";

                OnPropertyChanged(nameof(ActiveBandRightPx));
                OnPropertyChanged(nameof(ActiveBandBottomPx));
                OnPropertyChanged(nameof(ActiveBandDimensionsText));
                OnPropertyChanged(nameof(ActiveBandBoundsSummaryText));
                OnPropertyChanged(nameof(ActiveBandDimensionsSummaryText));
                OnPropertyChanged(nameof(ActiveBandLeftPercentText));
                OnPropertyChanged(nameof(ActiveBandTopPercentText));
                OnPropertyChanged(nameof(ActiveBandWidthPercentText));
                OnPropertyChanged(nameof(ActiveBandHeightPercentText));
                OnPropertyChanged(nameof(ActiveBandRowSplitPercentText));
                OnPropertyChanged(nameof(SelectedDividerPositionText));
                OnPropertyChanged(nameof(PreviewCellText));
            }
            finally
            {
                _isSyncingBand = false;
            }

            UpdateChitPreview();
        }

        private void SyncToActiveBand()
        {
            if (_isSyncingBand) return;

            var band = GetActiveBand();
            if (band == null) return;

            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;

            // Clamping rules removing the 50% minimum width floor
            int targetLeftPx = Math.Clamp(ActiveBandLeftPx, 0, Math.Max(0, w - 20));
            int targetTopPx = Math.Clamp(ActiveBandTopPx, 0, Math.Max(0, h - 4));
            int targetWidthPx = Math.Clamp(ActiveBandWidthPx, 20, Math.Max(20, w - targetLeftPx));
            int targetHeightPx = Math.Clamp(ActiveBandHeightPx, 4, Math.Max(4, h - targetTopPx));
            int targetRowSplitPx = Math.Clamp(ActiveBandRowSplitPx, 2, Math.Max(2, targetHeightPx - 2));

            band.Bounds.Left = Math.Clamp((double)targetLeftPx / w, 0.0, 0.9999);
            band.Bounds.Top = Math.Clamp((double)targetTopPx / h, 0.0, 0.9999);
            band.Bounds.Width = Math.Clamp((double)targetWidthPx / w, 20.0 / w, 1.0 - band.Bounds.Left);
            band.Bounds.Height = Math.Clamp((double)targetHeightPx / h, 4.0 / h, 1.0 - band.Bounds.Top);
            band.RowSplitRatio = Math.Clamp((double)targetRowSplitPx / targetHeightPx, 0.05, 0.95);

            _isSyncingBand = true;
            try
            {
                ActiveBandLeftPercent = Math.Round(band.Bounds.Left * 100, 2);
                ActiveBandTopPercent = Math.Round(band.Bounds.Top * 100, 2);
                ActiveBandWidthPercent = Math.Round(band.Bounds.Width * 100, 2);
                ActiveBandHeightPercent = Math.Round(band.Bounds.Height * 100, 2);
                ActiveBandRowSplitPercent = Math.Round(band.RowSplitRatio * 100, 1);

                ActiveBandLeftPx = targetLeftPx;
                ActiveBandTopPx = targetTopPx;
                ActiveBandWidthPx = targetWidthPx;
                ActiveBandHeightPx = targetHeightPx;
                ActiveBandRowSplitPx = targetRowSplitPx;

                ActiveBandLeftText = $"{ActiveBandLeftPx} px";
                ActiveBandTopText = $"{ActiveBandTopPx} px";
                ActiveBandWidthText = $"{ActiveBandWidthPx} px";
                ActiveBandHeightText = $"{ActiveBandHeightPx} px";
                ActiveBandRowSplitText = $"{ActiveBandRowSplitPx} px";

                OnPropertyChanged(nameof(ActiveBandRightPx));
                OnPropertyChanged(nameof(ActiveBandBottomPx));
                OnPropertyChanged(nameof(ActiveBandDimensionsText));
                OnPropertyChanged(nameof(ActiveBandBoundsSummaryText));
                OnPropertyChanged(nameof(ActiveBandDimensionsSummaryText));
                OnPropertyChanged(nameof(ActiveBandLeftPercentText));
                OnPropertyChanged(nameof(ActiveBandTopPercentText));
                OnPropertyChanged(nameof(ActiveBandWidthPercentText));
                OnPropertyChanged(nameof(ActiveBandHeightPercentText));
                OnPropertyChanged(nameof(ActiveBandRowSplitPercentText));
                OnPropertyChanged(nameof(SelectedDividerPositionText));
                OnPropertyChanged(nameof(PreviewCellText));
            }
            finally
            {
                _isSyncingBand = false;
            }

            UpdateChitPreview();
        }

        partial void OnSelectedProfileChanged(SheetProfile? value)
        {
            if (value != null)
            {
                ProfileRows = value.Rows;
                ProfileColumns = value.Columns;
                InwardInsetPx = value.InwardInsetPx;
                SelectedOrientation = value.BackOrientation;

                if (!string.IsNullOrWhiteSpace(LoadedFileName))
                {
                    NamingPrefix = Path.GetFileNameWithoutExtension(LoadedFileName);
                    value.NamingPrefix = NamingPrefix;
                }
                else if (!string.IsNullOrWhiteSpace(value.NamingPrefix) && !value.NamingPrefix.Equals("WorldKiller", StringComparison.OrdinalIgnoreCase))
                {
                    NamingPrefix = value.NamingPrefix;
                }
                else
                {
                    NamingPrefix = string.Empty;
                }

                var bounds = IsRearSide(SelectedSide) ? value.BackBounds : value.FrontBounds;
                GridLeftPercent = Math.Round(bounds.Left * 100, 2);
                GridTopPercent = Math.Round(bounds.Top * 100, 2);
                GridWidthPercent = Math.Round(bounds.Width * 100, 2);
                GridHeightPercent = Math.Round(bounds.Height * 100, 2);

                OnPropertyChanged(nameof(IsBandedMode));
                OnPropertyChanged(nameof(IsUniformGridMode));

                SyncFromActiveBand();
            }
        }

        partial void OnSelectedSideChanged(string value)
        {
            if (SelectedProfile != null)
            {
                var bounds = IsRearSide(value)
                    ? SelectedProfile.BackBounds
                    : SelectedProfile.FrontBounds;

                GridLeftPercent = Math.Round(bounds.Left * 100, 2);
                GridTopPercent = Math.Round(bounds.Top * 100, 2);
                GridWidthPercent = Math.Round(bounds.Width * 100, 2);
                GridHeightPercent = Math.Round(bounds.Height * 100, 2);

                SyncFromActiveBand();
            }

            // Re-sync row/col from current PreviewChitNumber under new side ordering
            SyncPreviewRowColFromChitNumber(PreviewChitNumber);
            OnPropertyChanged(nameof(PreviewCellText));
            UpdateChitPreview();
        }

        partial void OnDeskewAngleChanged(double value)
        {
            _ = ApplyDeskewRotationAsync();
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            if (value != 1)
            {
                if (IsSettingUpperLeftCorner) IsSettingUpperLeftCorner = false;
                if (IsSettingLowerRightCorner) IsSettingLowerRightCorner = false;
            }

            if (value == 2)
            {
                UpdateClipboardState();
                UpdateLiveChitPreview();
            }
            else
            {
                SelectedChit?.SetPreviewMat(null);
            }

            OnPropertyChanged(nameof(IsPdfViewerTabSelected));
            OnPropertyChanged(nameof(IsTileCreatorTabSelected));
            OnPropertyChanged(nameof(IsTileEditorTabSelected));
            OnPropertyChanged(nameof(CanAccessRecentPdfs));
            OnPropertyChanged(nameof(CanSaveTilePageImage));
            OnPropertyChanged(nameof(CanOpenTileFiles));
            OnPropertyChanged(nameof(CanSaveCurrentTile));
            OnPropertyChanged(nameof(CanSaveAllTiles));
            OnPropertyChanged(nameof(CanClosePdf));
            OnPropertyChanged(nameof(CanCloseTilePageImage));
            OnPropertyChanged(nameof(CanCloseTiles));
            OnPropertyChanged(nameof(CanUndoTileAction));
            OnPropertyChanged(nameof(CanRedoTileAction));
            OnPropertyChanged(nameof(CanCutTile));
            OnPropertyChanged(nameof(CanCopyTile));
            OnPropertyChanged(nameof(CanPasteImageToTile));
            OnPropertyChanged(nameof(CanPasteImageToNewTile));
            OnPropertyChanged(nameof(CanDeleteCurrentTile));
            OnPropertyChanged(nameof(CanDeleteSelectedTiles));
            OnPropertyChanged(nameof(CanResetCurrentTile));
            OnPropertyChanged(nameof(CanResetAllTiles));
            ClosePdfCommand.NotifyCanExecuteChanged();
            CloseTilePageImageCommand.NotifyCanExecuteChanged();
            CloseTilesCommand.NotifyCanExecuteChanged();
            OpenPdfCommand.NotifyCanExecuteChanged();
            OpenImageForSlicingCommand.NotifyCanExecuteChanged();
            OpenTileFilesCommand.NotifyCanExecuteChanged();
            SaveTilePageImageCommand.NotifyCanExecuteChanged();
            SaveCurrentTileCommand.NotifyCanExecuteChanged();
            SaveAllTilesCommand.NotifyCanExecuteChanged();
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();
            CutChitCommand.NotifyCanExecuteChanged();
            CopyChitCommand.NotifyCanExecuteChanged();
            PasteImageToTileCommand.NotifyCanExecuteChanged();
            PasteImageToNewTileCommand.NotifyCanExecuteChanged();
            DeleteCurrentTileCommand.NotifyCanExecuteChanged();
            DeleteSelectedTilesCommand.NotifyCanExecuteChanged();
            ResetSelectedChitCommand.NotifyCanExecuteChanged();
            ResetAllChitsCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsSettingUpperLeftCornerChanged(bool value)
        {
            if (value)
            {
                if (IsSettingLowerRightCorner) IsSettingLowerRightCorner = false;
                StatusMessage = "Crosshair active: Click on the counter sheet to set the upper-left corner of the 2×10 band.";
            }
            else
            {
                if (StatusMessage.StartsWith("Crosshair active:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Ready.";
                }
            }
        }

        partial void OnIsSettingLowerRightCornerChanged(bool value)
        {
            if (value)
            {
                if (IsSettingUpperLeftCorner) IsSettingUpperLeftCorner = false;
                StatusMessage = "Crosshair active: Click on the counter sheet to set the lower-right corner of the 2×10 band.";
            }
            else
            {
                if (StatusMessage.StartsWith("Crosshair active:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Ready.";
                }
            }
        }

        partial void OnActiveBandIndexChanged(int value) => SyncFromActiveBand();
        partial void OnActiveBandLeftPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandTopPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandWidthPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandHeightPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandRowSplitPercentChanged(double value) => SyncToActiveBand();

        partial void OnActiveBandLeftPxChanged(int value) => SyncToActiveBand();
        partial void OnActiveBandTopPxChanged(int value) => SyncToActiveBand();
        partial void OnActiveBandWidthPxChanged(int value) => SyncToActiveBand();
        partial void OnActiveBandHeightPxChanged(int value) => SyncToActiveBand();
        partial void OnActiveBandRowSplitPxChanged(int value) => SyncToActiveBand();

        partial void OnSelectedDividerIndexChanged(int value) => OnPropertyChanged(nameof(SelectedDividerPositionText));
        partial void OnPreviewRowIndexChanged(int value) => UpdateChitPreview();
        partial void OnPreviewColIndexChanged(int value) => UpdateChitPreview();

        private void SyncPreviewRowColFromChitNumber(int chitNumber)
        {
            int total = TotalBandChits;
            int clamped = Math.Clamp(chitNumber, 1, Math.Max(1, total));
            int cols = EffectiveBandColumns;
            int r = (clamped - 1) / cols;
            int colIndexInRow = (clamped - 1) % cols;
            int c = IsRearSide(SelectedSide)
                ? (cols - 1 - colIndexInRow)
                : colIndexInRow;

            PreviewRowIndex = r;
            PreviewColIndex = c;
        }

        partial void OnPreviewChitNumberChanged(int value)
        {
            int total = TotalBandChits;
            int clamped = Math.Clamp(value, 1, Math.Max(1, total));
            if (clamped != value)
            {
                PreviewChitNumber = clamped;
                return;
            }

            SyncPreviewRowColFromChitNumber(clamped);
            OnPropertyChanged(nameof(PreviewCellText));
            UpdateChitPreview();
        }

        [RelayCommand]
        private void PreviousChit()
        {
            int total = TotalBandChits;
            if (PreviewChitNumber > 1)
            {
                PreviewChitNumber--;
            }
            else
            {
                PreviewChitNumber = Math.Max(1, total); // wrap-around
            }
        }

        [RelayCommand]
        private void NextChit()
        {
            int total = TotalBandChits;
            if (PreviewChitNumber < total)
            {
                PreviewChitNumber++;
            }
            else
            {
                PreviewChitNumber = 1; // wrap-around to 1
            }
        }

        partial void OnGridLeftPercentChanged(double value) => SyncUniformBoundsAndPreview();
        partial void OnGridTopPercentChanged(double value) => SyncUniformBoundsAndPreview();
        partial void OnGridWidthPercentChanged(double value) => SyncUniformBoundsAndPreview();
        partial void OnGridHeightPercentChanged(double value) => SyncUniformBoundsAndPreview();
        partial void OnInwardInsetPxChanged(double value) => UpdateChitPreview();
        partial void OnProfileRowsChanged(int value) => SyncUniformBoundsAndPreview();
        partial void OnProfileColumnsChanged(int value) => SyncUniformBoundsAndPreview();

        private void SyncUniformBoundsAndPreview()
        {
            if (SelectedProfile != null && !IsBandedMode)
            {
                var targetBounds = IsRearSide(SelectedSide)
                    ? SelectedProfile.BackBounds
                    : SelectedProfile.FrontBounds;

                targetBounds.Left = Math.Clamp(GridLeftPercent / 100.0, 0.0, 0.99);
                targetBounds.Top = Math.Clamp(GridTopPercent / 100.0, 0.0, 0.99);
                targetBounds.Width = Math.Clamp(GridWidthPercent / 100.0, 0.01, 1.0 - targetBounds.Left);
                targetBounds.Height = Math.Clamp(GridHeightPercent / 100.0, 0.01, 1.0 - targetBounds.Top);

                SelectedProfile.Rows = Math.Max(1, ProfileRows);
                SelectedProfile.Columns = Math.Max(1, ProfileColumns);
                SelectedProfile.InwardInsetPx = Math.Max(0, InwardInsetPx);
                SelectedProfile.BackOrientation = SelectedOrientation;
                SelectedProfile.NamingPrefix = NamingPrefix;
            }

            UpdateChitPreview();
        }

        #region Band Navigation & Divider Tuning Commands

        [RelayCommand(CanExecute = nameof(CanGoPreviousBand))]
        private void PreviousBand()
        {
            if (ActiveBandIndex > 0)
            {
                ActiveBandIndex--;
            }
        }

        private bool CanGoPreviousBand() => ActiveBandIndex > 0;

        [RelayCommand(CanExecute = nameof(CanGoNextBand))]
        private void NextBand()
        {
            if (ActiveBandIndex < TotalBands - 1)
            {
                ActiveBandIndex++;
            }
        }

        private bool CanGoNextBand() => ActiveBandIndex < TotalBands - 1;

        [RelayCommand]
        private void NudgeDividerNegative()
        {
            var band = GetActiveBand();
            if (band?.ColumnDividers != null && SelectedDividerIndex >= 0 && SelectedDividerIndex < band.ColumnDividers.Count)
            {
                double minPos = SelectedDividerIndex == 0 ? 0.02 : band.ColumnDividers[SelectedDividerIndex - 1] + 0.005;
                double newPos = Math.Round(band.ColumnDividers[SelectedDividerIndex] - 0.002, 4);
                band.ColumnDividers[SelectedDividerIndex] = Math.Max(minPos, newPos);
                OnPropertyChanged(nameof(SelectedDividerPositionText));
                UpdateChitPreview();
            }
        }

        [RelayCommand]
        private void NudgeDividerPositive()
        {
            var band = GetActiveBand();
            if (band?.ColumnDividers != null && SelectedDividerIndex >= 0 && SelectedDividerIndex < band.ColumnDividers.Count)
            {
                double maxPos = SelectedDividerIndex == band.ColumnDividers.Count - 1 ? 0.98 : band.ColumnDividers[SelectedDividerIndex + 1] - 0.005;
                double newPos = Math.Round(band.ColumnDividers[SelectedDividerIndex] + 0.002, 4);
                band.ColumnDividers[SelectedDividerIndex] = Math.Min(maxPos, newPos);
                OnPropertyChanged(nameof(SelectedDividerPositionText));
                UpdateChitPreview();
            }
        }

        [RelayCommand]
        private void ResetDividersToEven()
        {
            var band = GetActiveBand();
            if (band != null)
            {
                band.ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10);
                OnPropertyChanged(nameof(SelectedDividerPositionText));
                UpdateChitPreview();
            }
        }

        #region Bounding Box Nudge Commands (Axis-Isolated & Direction-Corrected)

        [RelayCommand]
        private void NudgeActiveBandLeftNegative()
        {
            ActiveBandLeftPx = Math.Max(0, ActiveBandLeftPx - 1);
        }

        [RelayCommand]
        private void NudgeActiveBandLeftPositive()
        {
            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int maxLeft = Math.Max(0, w - ActiveBandWidthPx);
            ActiveBandLeftPx = Math.Min(maxLeft, ActiveBandLeftPx + 1);
        }

        [RelayCommand]
        private void NudgeActiveBandTopUp()
        {
            ActiveBandTopPx = Math.Max(0, ActiveBandTopPx - 1);
        }

        [RelayCommand]
        private void NudgeActiveBandTopDown()
        {
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;
            int maxTop = Math.Max(0, h - ActiveBandHeightPx);
            ActiveBandTopPx = Math.Min(maxTop, ActiveBandTopPx + 1);
        }

        [RelayCommand]
        private void NudgeActiveBandWidthLeft()
        {
            ActiveBandWidthPx = Math.Max(20, ActiveBandWidthPx - 1);
        }

        [RelayCommand]
        private void NudgeActiveBandWidthRight()
        {
            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int maxWidth = Math.Max(20, w - ActiveBandLeftPx);
            ActiveBandWidthPx = Math.Min(maxWidth, ActiveBandWidthPx + 1);
        }

        [RelayCommand]
        private void NudgeActiveBandHeightUp()
        {
            ActiveBandHeightPx = Math.Max(4, ActiveBandHeightPx - 1);
        }

        [RelayCommand]
        private void NudgeActiveBandHeightDown()
        {
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;
            int maxHeight = Math.Max(4, h - ActiveBandTopPx);
            ActiveBandHeightPx = Math.Min(maxHeight, ActiveBandHeightPx + 1);
        }

        [RelayCommand]
        private void NudgeActiveBandRowSplitUp()
        {
            ActiveBandRowSplitPx = Math.Max(2, ActiveBandRowSplitPx - 1);
        }

        [RelayCommand]
        private void NudgeActiveBandRowSplitDown()
        {
            int maxSplit = Math.Max(2, ActiveBandHeightPx - 2);
            ActiveBandRowSplitPx = Math.Min(maxSplit, ActiveBandRowSplitPx + 1);
        }

        public void ApplyActiveBandLeftInput(string? raw)
        {
            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int max = Math.Max(0, w - 20);
            ActiveBandLeftPx = ParsePixelInput(raw, ActiveBandLeftPx, 0, max);
        }

        public void ApplyActiveBandTopInput(string? raw)
        {
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;
            int max = Math.Max(0, h - 4);
            ActiveBandTopPx = ParsePixelInput(raw, ActiveBandTopPx, 0, max);
        }

        public void ApplyActiveBandWidthInput(string? raw)
        {
            int w = SlicerPixelWidth > 0 ? SlicerPixelWidth : 1000;
            int max = Math.Max(20, w - ActiveBandLeftPx);
            ActiveBandWidthPx = ParsePixelInput(raw, ActiveBandWidthPx, 20, max);
        }

        public void ApplyActiveBandHeightInput(string? raw)
        {
            int h = SlicerPixelHeight > 0 ? SlicerPixelHeight : 1000;
            int max = Math.Max(4, h - ActiveBandTopPx);
            ActiveBandHeightPx = ParsePixelInput(raw, ActiveBandHeightPx, 4, max);
        }

        public void ApplyActiveBandRowSplitInput(string? raw)
        {
            int max = Math.Max(2, ActiveBandHeightPx - 2);
            ActiveBandRowSplitPx = ParsePixelInput(raw, ActiveBandRowSplitPx, 2, max);
        }

        public void RecalculateRowSplitToFiftyPercent()
        {
            int safeRows = Math.Max(1, EffectiveBandRows);
            double ratio = 1.0 / safeRows;
            int h = ActiveBandHeightPx;
            if (h < 4) return;

            int targetSplitPx = Math.Clamp((int)Math.Round(ratio * h), 2, Math.Max(2, h - 2));

            ActiveBandRowSplitPx = targetSplitPx;
            ActiveBandRowSplitText = $"{ActiveBandRowSplitPx} px";
        }

        private static int ParsePixelInput(string? raw, int currentVal, int minVal, int maxVal)
        {
            if (string.IsNullOrWhiteSpace(raw)) return currentVal;
            string text = raw.Trim().ToLowerInvariant().Replace("px", "").Trim();
            if (int.TryParse(text, out int val))
            {
                return Math.Clamp(val, minVal, maxVal);
            }
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
            {
                return Math.Clamp((int)Math.Round(dVal), minVal, maxVal);
            }
            return currentVal;
        }

        [RelayCommand]
        private void NudgeGridLeftNegative()
        {
            GridLeftPercent = Math.Clamp(Math.Round(GridLeftPercent - 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeGridLeftPositive()
        {
            GridLeftPercent = Math.Clamp(Math.Round(GridLeftPercent + 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeGridTopUp()
        {
            // Up arrow moves grid UP -> Top decreases
            GridTopPercent = Math.Clamp(Math.Round(GridTopPercent - 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeGridTopDown()
        {
            // Down arrow moves grid DOWN -> Top increases
            GridTopPercent = Math.Clamp(Math.Round(GridTopPercent + 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeGridWidthLeft()
        {
            // Left arrow moves right boundary LEFT -> Width decreases
            GridWidthPercent = Math.Clamp(Math.Round(GridWidthPercent - 0.1, 1), 50.0, 100.0);
        }

        [RelayCommand]
        private void NudgeGridWidthRight()
        {
            // Right arrow moves right boundary RIGHT -> Width increases
            GridWidthPercent = Math.Clamp(Math.Round(GridWidthPercent + 0.1, 1), 50.0, 100.0);
        }

        [RelayCommand]
        private void NudgeGridHeightUp()
        {
            // Up arrow moves bottom boundary UP -> Height decreases
            GridHeightPercent = Math.Clamp(Math.Round(GridHeightPercent - 0.1, 1), 50.0, 100.0);
        }

        [RelayCommand]
        private void NudgeGridHeightDown()
        {
            // Down arrow moves bottom boundary DOWN -> Height increases
            GridHeightPercent = Math.Clamp(Math.Round(GridHeightPercent + 0.1, 1), 50.0, 100.0);
        }

        #endregion

        #endregion

        private async Task ApplyDeskewRotationAsync()
        {
            if (_masterMat == null || _masterMat.IsDisposed)
            {
                return;
            }

            try
            {
                double angle = DeskewAngle;
                var rotated = await Task.Run(() =>
                {
                    _deskewedMat?.Dispose();
                    return DeskewService.RotateImage(_masterMat, angle);
                });

                _deskewedMat = rotated;

                var previewSource = await Task.Run(() => DeskewService.MatToBitmapSource(_deskewedMat));
                SlicerImage = previewSource;
                UpdateChitPreview();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Deskew rotation error: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanAutoDetectTilt))]
        private async Task AutoDetectTiltAsync()
        {
            if (_masterMat == null || _masterMat.IsDisposed) return;

            IsAutoDetectingTilt = true;
            StatusMessage = "Analyzing scan tilt angle via Hough line transform (excluding borders)...";

            try
            {
                double detectedAngle = await Task.Run(() => DeskewService.DetectTiltAngle(_masterMat));
                DeskewAngle = detectedAngle;
                StatusMessage = $"Auto-detected tilt angle: {DeskewAngleText}. Angle applied.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to detect tilt: {ex.Message}";
            }
            finally
            {
                IsAutoDetectingTilt = false;
            }
        }

        private bool CanAutoDetectTilt() => HasSlicerImage && !IsAutoDetectingTilt && !IsSlicing;

        [RelayCommand]
        private void NudgeAnglePositive()
        {
            DeskewAngle = Math.Round(DeskewAngle + 0.1, 2);
        }

        [RelayCommand]
        private void NudgeAngleNegative()
        {
            DeskewAngle = Math.Round(DeskewAngle - 0.1, 2);
        }

        [RelayCommand]
        private void ResetAngle()
        {
            DeskewAngle = 0.0;
        }

        [RelayCommand(CanExecute = nameof(HasSlicerImage))]
        private async Task RotateLeft90Async()
        {
            await RotateSheet90Async(clockwise: false);
        }

        [RelayCommand(CanExecute = nameof(HasSlicerImage))]
        private async Task RotateRight90Async()
        {
            await RotateSheet90Async(clockwise: true);
        }

        private async Task RotateSheet90Async(bool clockwise)
        {
            if (_masterMat == null || _masterMat.IsDisposed || _masterMat.Width < 8 || _masterMat.Height < 8)
            {
                return;
            }

            IsLoading = true;
            string dirName = clockwise ? "clockwise" : "counter-clockwise";
            StatusMessage = $"Rotating sheet 90° {dirName}...";

            try
            {
                await Task.Run(() =>
                {
                    var rotated = DeskewService.Rotate90(_masterMat, clockwise);
                    _masterMat.Dispose();
                    _masterMat = rotated;
                });

                SlicerPixelWidth = _masterMat.Width;
                SlicerPixelHeight = _masterMat.Height;

                // Re-fit / clamp band bounds within new rotated dimensions
                ClampBandsToDimensions(SlicerPixelWidth, SlicerPixelHeight);

                // Apply deskew (retains any fine angle on top of new 90° orientation)
                await ApplyDeskewRotationAsync();

                // Refresh active band UI properties and previews
                SyncFromActiveBand();

                // Recalculate zoom if fit-to-width or fit-to-view is active
                if (IsSlicerFitToWidth && GetSlicerViewportSize != null)
                {
                    var (vWidth, _) = GetSlicerViewportSize();
                    if (vWidth > 0) RecalculateSlicerFitToWidth(vWidth);
                }
                else if (IsSlicerFitToPage && GetSlicerViewportSize != null)
                {
                    var (vWidth, vHeight) = GetSlicerViewportSize();
                    if (vWidth > 0 && vHeight > 0) RecalculateSlicerFitToPage(vWidth, vHeight);
                }

                StatusMessage = $"Rotated sheet 90° {dirName} ({SlicerPixelWidth} × {SlicerPixelHeight} px).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Sheet rotation error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ClampBandsToDimensions(int width, int height)
        {
            if (width < 8 || height < 8) return;

            if (SelectedProfile != null)
            {
                var allBands = (SelectedProfile.FrontBands ?? Enumerable.Empty<Band2x10Definition>())
                    .Concat(SelectedProfile.BackBands ?? Enumerable.Empty<Band2x10Definition>());
                foreach (var b in allBands)
                {
                    if (b?.Bounds == null) continue;
                    int bLeft = (int)Math.Round(b.Bounds.Left * width);
                    int bTop = (int)Math.Round(b.Bounds.Top * height);
                    int bWidth = (int)Math.Round(b.Bounds.Width * width);
                    int bHeight = (int)Math.Round(b.Bounds.Height * height);

                    bLeft = Math.Clamp(bLeft, 0, Math.Max(0, width - 20));
                    bTop = Math.Clamp(bTop, 0, Math.Max(0, height - 4));
                    bWidth = Math.Clamp(bWidth, 20, Math.Max(20, width - bLeft));
                    bHeight = Math.Clamp(bHeight, 4, Math.Max(4, height - bTop));

                    b.Bounds.Left = Math.Clamp((double)bLeft / width, 0.0, 0.9999);
                    b.Bounds.Top = Math.Clamp((double)bTop / height, 0.0, 0.9999);
                    b.Bounds.Width = Math.Clamp((double)bWidth / width, 20.0 / width, 1.0 - b.Bounds.Left);
                    b.Bounds.Height = Math.Clamp((double)bHeight / height, 4.0 / height, 1.0 - b.Bounds.Top);
                }

                if (SelectedProfile.FrontBounds != null)
                {
                    ClampGridBounds(SelectedProfile.FrontBounds, width, height);
                }
                if (SelectedProfile.BackBounds != null)
                {
                    ClampGridBounds(SelectedProfile.BackBounds, width, height);
                }
            }
        }

        private static void ClampGridBounds(GridRelativeBounds bounds, int width, int height)
        {
            int bLeft = (int)Math.Round(bounds.Left * width);
            int bTop = (int)Math.Round(bounds.Top * height);
            int bWidth = (int)Math.Round(bounds.Width * width);
            int bHeight = (int)Math.Round(bounds.Height * height);

            bLeft = Math.Clamp(bLeft, 0, Math.Max(0, width - 20));
            bTop = Math.Clamp(bTop, 0, Math.Max(0, height - 4));
            bWidth = Math.Clamp(bWidth, 20, Math.Max(20, width - bLeft));
            bHeight = Math.Clamp(bHeight, 4, Math.Max(4, height - bTop));

            bounds.Left = Math.Clamp((double)bLeft / width, 0.0, 0.9999);
            bounds.Top = Math.Clamp((double)bTop / height, 0.0, 0.9999);
            bounds.Width = Math.Clamp((double)bWidth / width, 20.0 / width, 1.0 - bounds.Left);
            bounds.Height = Math.Clamp((double)bHeight / height, 4.0 / height, 1.0 - bounds.Top);
        }

        private void UpdateChitPreview()
        {
            if (_deskewedMat == null || _deskewedMat.IsDisposed || SelectedProfile == null)
            {
                ChitPreviewImage = null;
                return;
            }

            try
            {
                if (IsBandedMode)
                {
                    using var previewMat = GridSlicerService.ExtractBandedCellPreview(
                        _deskewedMat, SelectedProfile, SelectedSide, ActiveBandIndex, PreviewRowIndex, PreviewColIndex,
                        EffectiveBandRows, EffectiveBandColumns);
                    ChitPreviewImage = DeskewService.MatToBitmapSource(previewMat);
                }
                else
                {
                    using var previewMat = GridSlicerService.ExtractCellPreview(
                        _deskewedMat, SelectedProfile, SelectedSide, 0, 0);
                    ChitPreviewImage = DeskewService.MatToBitmapSource(previewMat);
                }
            }
            catch
            {
                ChitPreviewImage = null;
            }
        }

        public void RecalculateSlicerFitToWidth(double viewportWidth)
        {
            if (SlicerImage == null || SlicerImage.PixelWidth <= 0 || viewportWidth <= 0)
            {
                return;
            }

            // Margin: 24px left + 24px right = 48px, plus vertical scrollbar breathing room (~20px) = 68px.
            double availableWidth = Math.Max(20.0, viewportWidth - 68.0);
            double fitScale = availableWidth / SlicerImage.PixelWidth;

            if (fitScale > 0.005)
            {
                SlicerZoomScale = Math.Clamp(Math.Round(fitScale, 4), 0.05, 5.0);
            }
        }

        public void RecalculateSlicerFitToPage(double viewportWidth, double viewportHeight)
        {
            if (SlicerImage == null || SlicerImage.PixelWidth <= 0 || SlicerImage.PixelHeight <= 0)
            {
                return;
            }

            double availableWidth = Math.Max(20, viewportWidth - 40);
            double availableHeight = Math.Max(20, viewportHeight - 40);

            double scaleX = availableWidth / SlicerImage.PixelWidth;
            double scaleY = availableHeight / SlicerImage.PixelHeight;
            double fitScale = Math.Min(scaleX, scaleY);

            if (fitScale > 0.01)
            {
                SlicerZoomScale = Math.Round(fitScale, 4);
            }
        }

        [RelayCommand]
        private void SlicerFitToWidth()
        {
            IsSlicerFitToWidth = true;
            IsSlicerFitToPage = false;
            if (GetSlicerViewportSize != null)
            {
                var (width, _) = GetSlicerViewportSize();
                if (width > 0)
                {
                    RecalculateSlicerFitToWidth(width);
                }
            }
        }

        [RelayCommand]
        private void SlicerFitToPage()
        {
            IsSlicerFitToWidth = false;
            IsSlicerFitToPage = true;
            if (GetSlicerViewportSize != null)
            {
                var (width, height) = GetSlicerViewportSize();
                RecalculateSlicerFitToPage(width, height);
            }
        }

        [RelayCommand]
        private void SlicerActualSize()
        {
            IsSlicerFitToWidth = false;
            IsSlicerFitToPage = false;
            SlicerZoomScale = 1.0;
        }

        [RelayCommand]
        private void SlicerZoomIn()
        {
            IsSlicerFitToWidth = false;
            IsSlicerFitToPage = false;
            SlicerZoomScale = Math.Min(Math.Round(SlicerZoomScale + 0.01, 2), 5.0);
        }

        [RelayCommand]
        private void SlicerZoomOut()
        {
            IsSlicerFitToWidth = false;
            IsSlicerFitToPage = false;
            SlicerZoomScale = Math.Max(Math.Round(SlicerZoomScale - 0.01, 2), 0.05);
        }

        [RelayCommand(CanExecute = nameof(CanSliceGrid))]
        private async Task SliceAllBandsAsync()
        {
            await ExecuteSlicingAsync(singleBand: false);
        }

        [RelayCommand(CanExecute = nameof(CanSliceGrid))]
        private async Task SliceActiveBandAsync()
        {
            await ExecuteSlicingAsync(singleBand: true);
        }

        [RelayCommand(CanExecute = nameof(CanSliceGrid))]
        private async Task SliceGridAsync()
        {
            await ExecuteSlicingAsync(singleBand: false);
        }

        private async Task ExecuteSlicingAsync(bool singleBand)
        {
            if (_deskewedMat == null || _deskewedMat.IsDisposed || SelectedProfile == null)
            {
                return;
            }

            var folderDialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Sliced PNG Pieces",
                InitialDirectory = !string.IsNullOrEmpty(LoadedFilePath) ? Path.GetDirectoryName(LoadedFilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string targetDir = folderDialog.FolderName;

            var profileCopy = SelectedProfile.Clone();
            if (!string.IsNullOrWhiteSpace(LoadedFileName))
            {
                profileCopy.NamingPrefix = Path.GetFileNameWithoutExtension(LoadedFileName);
            }
            else if (!string.IsNullOrWhiteSpace(SlicerSourceFileName))
            {
                profileCopy.NamingPrefix = Path.GetFileNameWithoutExtension(SlicerSourceFileName);
            }
            else if (!string.IsNullOrWhiteSpace(NamingPrefix))
            {
                profileCopy.NamingPrefix = NamingPrefix;
            }
            else
            {
                profileCopy.NamingPrefix = "Piece";
            }
            string currentSide = SelectedSide;

            var expectedFilenames = IsBandedMode
                ? GridSlicerService.GetBandedOutputFilenames(profileCopy, currentSide, singleBand ? ActiveBandIndex : null, EffectiveBandRows, EffectiveBandColumns)
                : GridSlicerService.GetGridOutputFilenames(profileCopy, currentSide);

            var conflictingFiles = expectedFilenames
                .Where(fn => File.Exists(Path.Combine(targetDir, fn)))
                .ToList();

            bool skipExisting = false;
            if (conflictingFiles.Count > 0)
            {
                string sampleNames = string.Join(", ", conflictingFiles.Take(3));
                if (conflictingFiles.Count > 3)
                {
                    sampleNames += $", ... (+{conflictingFiles.Count - 3} more)";
                }

                string promptText = $"{conflictingFiles.Count} of {expectedFilenames.Count} sliced piece files already exist in:\n{targetDir}\n\nExisting files:\n{sampleNames}\n\n" +
                                   "• Click 'Yes' to Overwrite existing files.\n" +
                                   "• Click 'No' to Skip existing files (export only new pieces).\n" +
                                   "• Click 'Cancel' to Abort slicing without saving any files.";

                var dialogResult = MessageBox.Show(
                    promptText,
                    "Confirm Sliced Pieces Overwrite",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (dialogResult == MessageBoxResult.Cancel)
                {
                    StatusMessage = "Grid slicing cancelled by user. No files were written.";
                    return;
                }

                if (dialogResult == MessageBoxResult.No)
                {
                    skipExisting = true;
                }
            }

            int pieceCount = IsBandedMode ? (singleBand ? TotalBandChits : ((SelectedProfile.FrontBands?.Count ?? 5) * TotalBandChits)) : (SelectedProfile.Rows * SelectedProfile.Columns);

            IsSlicing = true;
            SliceProgressPercent = 0;
            SliceProgressText = $"Preparing to slice {pieceCount} pieces...";
            StatusMessage = IsBandedMode ? $"Slicing band into {pieceCount} PNG pieces..." : $"Slicing grid into {pieceCount} PNG pieces...";

            _sliceCts = new CancellationTokenSource();

            var progress = new Progress<SliceProgressReport>(report =>
            {
                SliceProgressPercent = report.Percent;
                SliceProgressText = report.StatusText;
                StatusMessage = report.StatusText;
            });

            try
            {
                if (IsBandedMode)
                {
                    int? bandIndex = singleBand ? ActiveBandIndex : null;
                    await GridSlicerService.SliceBandedGridAsync(
                        _deskewedMat,
                        profileCopy,
                        currentSide,
                        targetDir,
                        progress,
                        _sliceCts.Token,
                        bandIndex,
                        EffectiveBandRows,
                        EffectiveBandColumns,
                        skipExisting
                    );
                }
                else
                {
                    await GridSlicerService.SliceGridAsync(
                        _deskewedMat,
                        profileCopy,
                        currentSide,
                        targetDir,
                        progress,
                        _sliceCts.Token,
                        skipExisting
                    );
                }

                if (skipExisting && conflictingFiles.Count > 0)
                {
                    int written = Math.Max(0, pieceCount - conflictingFiles.Count);
                    StatusMessage = $"Successfully exported {written} pieces (skipped {conflictingFiles.Count} existing files) to: {targetDir}";
                    SliceProgressText = $"Completed! {written} pieces saved (skipped {conflictingFiles.Count} existing) to {targetDir}";
                }
                else if (conflictingFiles.Count > 0)
                {
                    StatusMessage = $"Successfully exported all {pieceCount} pieces (overwrote {conflictingFiles.Count} existing files) to: {targetDir}";
                    SliceProgressText = $"Completed! {pieceCount} pieces saved (overwrote {conflictingFiles.Count} existing) to {targetDir}";
                }
                else
                {
                    StatusMessage = $"Successfully exported all {pieceCount} pieces to: {targetDir}";
                    SliceProgressText = $"Completed! {pieceCount} pieces saved to {targetDir}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Grid slicing was cancelled by user.";
                SliceProgressText = "Slicing cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Grid slicing error: {ex.Message}";
                SliceProgressText = $"Error: {ex.Message}";
            }
            finally
            {
                IsSlicing = false;
                _sliceCts?.Dispose();
                _sliceCts = null;
            }
        }

        private bool CanSliceGrid() => HasSlicerImage && !IsSlicing && SelectedProfile != null;

        [RelayCommand]
        private void CancelSlice()
        {
            _sliceCts?.Cancel();
        }

        [RelayCommand(CanExecute = nameof(CanSliceGrid))]
        private async Task SendBandToChitEditorAsync()
        {
            if (_deskewedMat == null || _deskewedMat.IsDisposed || SelectedProfile == null)
            {
                return;
            }

            IsSlicing = true;
            SliceProgressPercent = 0;
            SliceProgressText = $"Slicing band into {TotalBandChits} tiles in memory for Tile Editor...";
            StatusMessage = $"Slicing band into {TotalBandChits} tiles in memory for Tile Editor...";

            _sliceCts = new CancellationTokenSource();
            var progress = new Progress<SliceProgressReport>(report =>
            {
                SliceProgressPercent = report.Percent;
                SliceProgressText = report.StatusText;
                StatusMessage = report.StatusText;
            });

            try
            {
                var profileCopy = SelectedProfile.Clone();
                if (!string.IsNullOrWhiteSpace(LoadedFileName))
                {
                    profileCopy.NamingPrefix = Path.GetFileNameWithoutExtension(LoadedFileName);
                }
                else if (!string.IsNullOrWhiteSpace(SlicerSourceFileName))
                {
                    profileCopy.NamingPrefix = Path.GetFileNameWithoutExtension(SlicerSourceFileName);
                }
                else if (!string.IsNullOrWhiteSpace(NamingPrefix))
                {
                    profileCopy.NamingPrefix = NamingPrefix;
                }
                else
                {
                    profileCopy.NamingPrefix = "Piece";
                }
                string currentSide = SelectedSide;
                int bandIdx = ActiveBandIndex;

                var chits = await GridSlicerService.SliceBandedGridToMemoryAsync(
                    _deskewedMat,
                    profileCopy,
                    currentSide,
                    bandIdx,
                    progress,
                    _sliceCts.Token,
                    EffectiveBandRows,
                    EffectiveBandColumns
                );

                // Dispose existing loaded chits to prevent memory leaks
                foreach (var oldChit in LoadedChits)
                {
                    oldChit.PropertyChanged -= OnChitItemPropertyChanged;
                    oldChit.Dispose();
                }
                LoadedChits.Clear();
                _batchEditHistory.Clear();
                _batchEditRedoHistory.Clear();

                foreach (var chit in chits)
                {
                    chit.IsSelected = true;
                    chit.PropertyChanged += OnChitItemPropertyChanged;
                    LoadedChits.Add(chit);
                }

                _isSyncingTileSelection = true;
                try
                {
                    IsAllTilesSelected = true;
                }
                finally
                {
                    _isSyncingTileSelection = false;
                }
                NotifySelectionChanges();

                if (LoadedChits.Count > 0)
                {
                    SelectedChit = LoadedChits[0];
                    SelectedChit.IsSelected = true;
                }

                SelectedTabIndex = 2; // Switch directly to Tab 3: Chit Editor
                StatusMessage = $"Loaded {LoadedChits.Count} tiles into Tile Editor.";
                SliceProgressText = $"Loaded {LoadedChits.Count} tiles into Tile Editor.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "In-memory slicing cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading tiles: {ex.Message}";
                SliceProgressText = $"Error: {ex.Message}";
            }
            finally
            {
                IsSlicing = false;
                _sliceCts?.Dispose();
                _sliceCts = null;
            }
        }

        #endregion

        #region Tab 3: Chit Editor Properties & Commands

        public ObservableCollection<ChitItem> LoadedChits { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedChit))]
        [NotifyCanExecuteChangedFor(nameof(UndoChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(RedoChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(CutChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopyChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplySharedToolCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplySharedToolAllCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyBevelCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyClipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyScaleImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyResizeImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(BumpChitLeftCommand))]
        [NotifyCanExecuteChangedFor(nameof(BumpChitRightCommand))]
        [NotifyCanExecuteChangedFor(nameof(BumpChitUpCommand))]
        [NotifyCanExecuteChangedFor(nameof(BumpChitDownCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyEdgeCleanCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetEdgeCleanCommand))]
        [NotifyCanExecuteChangedFor(nameof(AutoSampleEdgeColorCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetSelectedChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(RotateCurrentTileLeftCommand))]
        [NotifyCanExecuteChangedFor(nameof(RotateCurrentTileRightCommand))]
        [NotifyCanExecuteChangedFor(nameof(RotateAllTilesLeftCommand))]
        [NotifyCanExecuteChangedFor(nameof(RotateAllTilesRightCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportSelectedChitCommand))]
        [NotifyPropertyChangedFor(nameof(CanPasteImageToTile))]
        [NotifyPropertyChangedFor(nameof(CanDeleteCurrentTile))]
        [NotifyPropertyChangedFor(nameof(ChitStatusBarText))]
        private ChitItem? _selectedChit;

        /// <summary>
        /// File or piece name displayed in the lower-right status bar for Tab 3 (Chit Editor).
        /// Dynamically reflects the active loaded PDF file name (e.g. Leningrad.pdf),
        /// falling back to Slicer source image or selected piece name when no PDF is active.
        /// </summary>
        public string? ChitStatusBarText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LoadedFileName))
                {
                    return LoadedFileName;
                }

                if (!string.IsNullOrWhiteSpace(SlicerSourceFileName))
                {
                    return SlicerSourceFileName;
                }

                if (SelectedChit != null && !string.IsNullOrWhiteSpace(SelectedChit.PieceName) && !SelectedChit.PieceName.StartsWith("WorldKiller_", StringComparison.OrdinalIgnoreCase))
                {
                    return SelectedChit.PieceName;
                }

                return null;
            }
        }

        partial void OnSelectedChitChanging(ChitItem? oldValue, ChitItem? newValue)
        {
            if (oldValue != null)
            {
                oldValue.PropertyChanged -= SelectedChit_PropertyChanged;
            }
            oldValue?.SetPreviewMat(null);
        }

        partial void OnSelectedChitChanged(ChitItem? value)
        {
            if (value != null)
            {
                value.PropertyChanged += SelectedChit_PropertyChanged;
                value.IsSelected = true;

                ChitEditorWidth = value.PixelWidth;
                ChitEditorHeight = IsResizeSquare ? value.PixelWidth : value.PixelHeight;
                ClampCurrentEdgeCleanDepths();

                // 4% Bevel and Corner-Clip default calculation (BaseDimension = min(width, height))
                int default4Pct = ChitEditorService.CalculateDefaultBevelSize(value.PixelWidth, value.PixelHeight);
                ChitEditorClipSize = default4Pct;
                ChitEditorBevelSize = default4Pct;
                if (SelectedSharedToolMode == "Bevel" || SelectedSharedToolMode == "Clip")
                {
                    SharedToolValue = default4Pct;
                }

                // 5% Clone Stamp default calculation (BaseDimension = min(width, height))
                CloneStampRadius = ChitEditorService.CalculateDefaultCloneStampSize(value.PixelWidth, value.PixelHeight);
                ResetCloneStampAnchor();

                if (!LockEdgeCleanColor && value.WorkingMat != null && !value.WorkingMat.IsDisposed)
                {
                    AutoSampleEdgeColorInternal(value);
                }

                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                UpdateLiveChitPreview();
            }
            else
            {
                DeactivateCloneStamp();
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
            }

            NotifySelectionChanges();

            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            OnPropertyChanged(nameof(HasSelectedChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();
            RotateCurrentTileLeftCommand.NotifyCanExecuteChanged();
            RotateCurrentTileRightCommand.NotifyCanExecuteChanged();
            RotateAllTilesLeftCommand.NotifyCanExecuteChanged();
            RotateAllTilesRightCommand.NotifyCanExecuteChanged();
            ApplySharedToolCommand.NotifyCanExecuteChanged();
            ExportSelectedChitCommand.NotifyCanExecuteChanged();
            CutChitCommand.NotifyCanExecuteChanged();
            CopyChitCommand.NotifyCanExecuteChanged();
            PasteChitCommand.NotifyCanExecuteChanged();
            ResetSelectedChitCommand.NotifyCanExecuteChanged();
            BumpChitLeftCommand.NotifyCanExecuteChanged();
            BumpChitRightCommand.NotifyCanExecuteChanged();
            BumpChitUpCommand.NotifyCanExecuteChanged();
            BumpChitDownCommand.NotifyCanExecuteChanged();
            ChitEditorZoomResetCommand.NotifyCanExecuteChanged();
        }

        private void SelectedChit_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChitItem.CanUndo))
            {
                OnPropertyChanged(nameof(CanUndoChit));
                UndoChitCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(ChitItem.CanRedo))
            {
                OnPropertyChanged(nameof(CanRedoChit));
                RedoChitCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasSelectedChit => SelectedChit != null;

        [ObservableProperty]
        private int _chitEditorWidth = 50;

        [ObservableProperty]
        private int _chitEditorHeight = 50;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSquareChecked))]
        [NotifyPropertyChangedFor(nameof(CanEditHeight))]
        private bool _isResizeSquare = false;

        public bool IsSquareChecked
        {
            get => IsResizeSquare;
            set => IsResizeSquare = value;
        }

        public bool CanEditHeight => !IsResizeSquare;

        private bool _isSyncingDimensions = false;

        partial void OnChitEditorWidthChanged(int value)
        {
            if (IsResizeSquare && !_isSyncingDimensions)
            {
                _isSyncingDimensions = true;
                try
                {
                    ChitEditorHeight = value;
                }
                finally
                {
                    _isSyncingDimensions = false;
                }
            }
        }

        partial void OnChitEditorHeightChanged(int value)
        {
            if (IsResizeSquare && !_isSyncingDimensions)
            {
                _isSyncingDimensions = true;
                try
                {
                    ChitEditorWidth = value;
                }
                finally
                {
                    _isSyncingDimensions = false;
                }
            }
        }

        partial void OnIsResizeSquareChanged(bool value)
        {
            if (value && !_isSyncingDimensions)
            {
                _isSyncingDimensions = true;
                try
                {
                    ChitEditorHeight = ChitEditorWidth;
                }
                finally
                {
                    _isSyncingDimensions = false;
                }
            }
        }

        [ObservableProperty]
        private int _chitEditorBevelSize = 4;

        [ObservableProperty]
        private int _chitEditorClipSize = 4;

        // Shared Tool Mode (Bevel or Clip)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsClipModeSelected))]
        [NotifyPropertyChangedFor(nameof(IsBevelModeSelected))]
        [NotifyPropertyChangedFor(nameof(SharedToolActionName))]
        [NotifyPropertyChangedFor(nameof(SharedToolActionToolTip))]
        [NotifyPropertyChangedFor(nameof(SharedToolBatchActionName))]
        [NotifyPropertyChangedFor(nameof(SharedToolBatchActionToolTip))]
        private string _selectedSharedToolMode = "Bevel"; // "Clip" or "Bevel"

        partial void OnSelectedSharedToolModeChanged(string value)
        {
            switch (value)
            {
                case "Clip":
                    if (ChitEditorClipSize <= 0 && SelectedChit != null)
                    {
                        ChitEditorClipSize = ChitEditorService.CalculateDefaultBevelSize(SelectedChit.PixelWidth, SelectedChit.PixelHeight);
                    }
                    SharedToolValue = ChitEditorClipSize;
                    break;
                default:
                    if (ChitEditorBevelSize <= 0 && SelectedChit != null)
                    {
                        ChitEditorBevelSize = ChitEditorService.CalculateDefaultBevelSize(SelectedChit.PixelWidth, SelectedChit.PixelHeight);
                    }
                    SharedToolValue = ChitEditorBevelSize;
                    break;
            }
            UpdateLiveChitPreview();
        }

        public bool IsClipModeSelected
        {
            get => SelectedSharedToolMode == "Clip";
            set
            {
                if (value)
                {
                    DeactivateCloneStamp();
                    SelectedSharedToolMode = "Clip";
                }
                else
                {
                    DeactivateCloneStamp();
                    if (SelectedSharedToolMode == "Clip")
                    {
                        OnPropertyChanged(nameof(IsClipModeSelected));
                    }
                }
            }
        }

        public bool IsBevelModeSelected
        {
            get => SelectedSharedToolMode == "Bevel";
            set
            {
                if (value)
                {
                    DeactivateCloneStamp();
                    SelectedSharedToolMode = "Bevel";
                }
                else
                {
                    DeactivateCloneStamp();
                    if (SelectedSharedToolMode == "Bevel")
                    {
                        OnPropertyChanged(nameof(IsBevelModeSelected));
                    }
                }
            }
        }

        public string SharedToolActionName => SelectedSharedToolMode == "Clip" ? "Clip Current" : "Bevel Current";

        public string SharedToolActionToolTip => SelectedSharedToolMode == "Clip"
            ? $"Clip {SharedToolValue}px corners with transparent key color"
            : $"Apply {SharedToolValue}px 3D raised bevel around perimeter";

        public string SharedToolBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "All" : "Selected";

        public string SharedToolBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0) return "No tiles selected in Tile Strip";
                string tool = SelectedSharedToolMode.ToLowerInvariant();
                if (selectedCount == LoadedChits.Count)
                    return $"Apply {SharedToolValue}px {tool} to all {LoadedChits.Count} tiles";
                return $"Apply {SharedToolValue}px {tool} to {selectedCount} selected tiles";
            }
        }

        public string RotateBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "↻ All" : "↻ Selected";

        public string RotateBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0) return "No tiles selected in Tile Strip";
                if (selectedCount == LoadedChits.Count)
                    return $"Rotate all {LoadedChits.Count} loaded tiles 90° clockwise";
                return $"Rotate {selectedCount} selected tiles 90° clockwise";
            }
        }

        [ObservableProperty]
        private bool _isSmoothLight = true;

        [ObservableProperty]
        private bool _isSmoothMedium = false;

        [ObservableProperty]
        private bool _isSmoothStrong = false;

        public string SmoothBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "Smooth All" : (HasSelectedChits ? "Smooth Selected" : "Smooth All");

        public string SmoothBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0 || selectedCount == LoadedChits.Count)
                    return $"Smooth background for all {LoadedChits.Count} loaded tiles";
                return $"Smooth background for {selectedCount} selected tiles";
            }
        }

        private (int d, double sigmaColor, double sigmaSpace) GetCurrentSmoothParameters()
        {
            if (IsSmoothStrong)
            {
                return (5, 45.0, 45.0);
            }
            if (IsSmoothMedium)
            {
                return (5, 30.0, 30.0);
            }
            // Default: Light (preserves realistic subtle counter grain)
            return (3, 18.0, 18.0);
        }

        public string DespeckleBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "Despeckle All" : (HasSelectedChits ? "Despeckle Selected" : "Despeckle All");

        public string DespeckleBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0 || selectedCount == LoadedChits.Count)
                    return $"Despeckle all {LoadedChits.Count} loaded tiles";
                return $"Despeckle {selectedCount} selected tiles";
            }
        }

        public string SharpenBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "Sharpen All" : (HasSelectedChits ? "Sharpen Selected" : "Sharpen All");

        public string SharpenBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0 || selectedCount == LoadedChits.Count)
                    return $"Gentle sharpen all {LoadedChits.Count} loaded tiles";
                return $"Gentle sharpen {selectedCount} selected tiles";
            }
        }

        public string AutoTrimBatchActionName =>
            (LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected)) ? "Trim All" : (HasSelectedChits ? "Trim Selected" : "Trim All");

        public string AutoTrimBatchActionToolTip
        {
            get
            {
                int selectedCount = LoadedChits.Count(c => c.IsSelected);
                if (selectedCount == 0 || selectedCount == LoadedChits.Count)
                    return $"Auto-trim borders for all {LoadedChits.Count} loaded tiles";
                return $"Auto-trim borders for {selectedCount} selected tiles";
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SharedToolActionToolTip))]
        [NotifyPropertyChangedFor(nameof(SharedToolBatchActionToolTip))]
        private int _sharedToolValue = 2;

        partial void OnSharedToolValueChanged(int value)
        {
            if (SelectedSharedToolMode == "Clip")
            {
                _chitEditorClipSize = value;
            }
            else
            {
                _chitEditorBevelSize = value;
            }
            UpdateLiveChitPreview();
        }

        [ObservableProperty]
        private Color _transparentColor = Colors.Cyan;

        #region Clone Stamp Tool Properties & Commands

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkingChitToolTip))]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private bool _isCloneStampActive = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkingChitToolTip))]
        private bool _isPickingCloneAnchor = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneAnchorX = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneAnchorY = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkingChitToolTip))]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private bool _hasCloneAnchor = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneStampRadius = 4; // default radius 4px

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCloneModeFloating))]
        [NotifyPropertyChangedFor(nameof(IsCloneModeAnchored))]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private string _cloneMode = "Floating"; // "Floating" (default) or "Anchored"

        public bool IsCloneModeFloating
        {
            get => CloneMode == "Floating";
            set { if (value) CloneMode = "Floating"; }
        }

        public bool IsCloneModeAnchored
        {
            get => CloneMode == "Anchored";
            set { if (value) CloneMode = "Anchored"; }
        }

        private Mat? _cloneAnchorSourceMat;

        public void CaptureCloneAnchorSourceMat()
        {
            DisposeAnchoredSourceMat();
            if (SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed)
            {
                _cloneAnchorSourceMat = SelectedChit.WorkingMat.Clone();
            }
        }

        public Mat? GetCloneAnchorSourceMat() => _cloneAnchorSourceMat;

        public void DisposeAnchoredSourceMat()
        {
            if (_cloneAnchorSourceMat != null && !_cloneAnchorSourceMat.IsDisposed)
            {
                _cloneAnchorSourceMat.Dispose();
                _cloneAnchorSourceMat = null;
            }
        }

        public void ResetCloneStampAnchor()
        {
            HasCloneAnchor = false;
            HasDestinationAnchor = false;
            CloneAnchorX = 0;
            CloneAnchorY = 0;
            CloneDstAnchorX = 0;
            CloneDstAnchorY = 0;
            CloneOffsetDeltaX = 0;
            CloneOffsetDeltaY = 0;
            IsPickingCloneAnchor = false;
            IsCloneHovering = false;
            CloneHoverDstX = -1;
            CloneHoverDstY = -1;
            DisposeAnchoredSourceMat();
            OnPropertyChanged(nameof(CloneStampStatusText));
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCloneCircleShape))]
        [NotifyPropertyChangedFor(nameof(IsCloneSquareShape))]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private string _cloneStampShape = "Square"; // "Square" (default) or "Circle"

        public bool IsCloneCircleShape
        {
            get => CloneStampShape == "Circle";
            set { if (value) CloneStampShape = "Circle"; }
        }

        public bool IsCloneSquareShape
        {
            get => CloneStampShape == "Square";
            set { if (value) CloneStampShape = "Square"; }
        }

        public Services.CloneStampShape ActiveCloneStampShape =>
            CloneStampShape == "Square" ? Services.CloneStampShape.Square : Services.CloneStampShape.Circle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneDstAnchorX = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneDstAnchorY = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneOffsetDeltaX = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private int _cloneOffsetDeltaY = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CloneStampStatusText))]
        private bool _hasDestinationAnchor = false;

        // Dynamic hover position for active brush overlay
        [ObservableProperty]
        private int _cloneHoverDstX = -1;

        [ObservableProperty]
        private int _cloneHoverDstY = -1;

        [ObservableProperty]
        private bool _isCloneHovering = false;

        public string CloneStampStatusText
        {
            get
            {
                if (!IsCloneStampActive) return string.Empty;
                if (!HasCloneAnchor) return "Shift+Click on canvas to set Source Anchor";
                string shapeName = CloneStampShape == "Square" ? "Square" : "Circle";
                if (IsCloneModeAnchored)
                {
                    return $"Anchor: ({CloneAnchorX}, {CloneAnchorY}) | {shapeName} {CloneStampRadius * 2}px Anchored (Fixed)";
                }
                if (HasDestinationAnchor)
                    return $"Anchor: ({CloneAnchorX}, {CloneAnchorY}) | Offset: ({CloneOffsetDeltaX:+0;-0;0}, {CloneOffsetDeltaY:+0;-0;0}) | {shapeName} {CloneStampRadius * 2}px Lockstep";
                return $"Anchor set at ({CloneAnchorX}, {CloneAnchorY}) | {shapeName} {CloneStampRadius * 2}px Ready";
            }
        }

        public string WorkingChitToolTip
        {
            get
            {
                if (IsCloneStampActive)
                {
                    if (HasCloneAnchor)
                    {
                        return $"Clone Stamp ({CloneStampShape}, {CloneStampRadius * 2}px). Drag to stamp pixels in lockstep; Shift+Click to set new anchor.";
                    }
                    return "Clone Stamp active. Shift+Click on canvas to set the clone source anchor.";
                }
                return "Click to sample background color for Edge Cleanup";
            }
        }

        partial void OnIsCloneStampActiveChanged(bool value)
        {
            ResetCloneStampAnchor();
            if (value)
            {
                StatusMessage = "Clone Stamp active. Shift+Click on canvas to set source anchor point.";
            }
        }

        public void DeactivateCloneStamp()
        {
            if (IsCloneStampActive)
            {
                IsCloneStampActive = false;
                ResetCloneStampAnchor();
                StatusMessage = "Clone Stamp deactivated.";
            }
        }

        [RelayCommand]
        private void CloneStampRadiusStepUp()
        {
            CloneStampRadius = Math.Min(40, CloneStampRadius + 1);
        }

        [RelayCommand]
        private void CloneStampRadiusStepDown()
        {
            CloneStampRadius = Math.Max(1, CloneStampRadius - 1);
        }

        #endregion

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitEditorZoomPercentageText))]
        [NotifyCanExecuteChangedFor(nameof(ChitEditorZoomResetCommand))]
        private double _chitEditorZoomScale = 1.0;

        public string ChitEditorZoomPercentageText => $"{Math.Round(ChitEditorZoomScale * 100):F0}%";

        [ObservableProperty]
        private string _chitEditorZoomInputText = "100%";

        partial void OnChitEditorZoomScaleChanged(double value)
        {
            ChitEditorZoomInputText = $"{Math.Round(value * 100):F0}%";
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitInspectionZoomPercentageText))]
        private double _chitInspectionZoomScale = 2.0;

        public string ChitInspectionZoomPercentageText => $"{Math.Round(ChitInspectionZoomScale * 100):F0}%";

        [ObservableProperty]
        private bool _applyToAllChits = false;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SelectAllTilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeselectAllTilesCommand))]
        private bool _isAllTilesSelected = true;

        private bool _isApplyingBatchEdit = false;
        private bool _isSyncingTileSelection = false;

        partial void OnIsAllTilesSelectedChanged(bool value)
        {
            if (_isSyncingTileSelection) return;
            _isSyncingTileSelection = true;
            try
            {
                foreach (var c in LoadedChits)
                {
                    c.IsSelected = value;
                }
                if (value)
                {
                    if (SelectedChit == null)
                    {
                        SelectedChit = LoadedChits.FirstOrDefault();
                    }
                }
                else
                {
                    SelectedChit = null;
                }
                NotifySelectionChanges();
            }
            finally
            {
                _isSyncingTileSelection = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanSelectAllTiles))]
        private void SelectAllTiles()
        {
            if (_isSyncingTileSelection) return;
            _isSyncingTileSelection = true;
            try
            {
                foreach (var c in LoadedChits)
                {
                    c.IsSelected = true;
                }
                IsAllTilesSelected = true;
                if (SelectedChit == null)
                {
                    SelectedChit = LoadedChits.FirstOrDefault();
                }
                NotifySelectionChanges();
            }
            finally
            {
                _isSyncingTileSelection = false;
            }
        }

        public bool CanSelectAllTiles => HasChitsLoaded && !IsAllTilesSelected;

        [RelayCommand(CanExecute = nameof(CanDeselectAllTiles))]
        private void DeselectAllTiles()
        {
            if (_isSyncingTileSelection) return;
            _isSyncingTileSelection = true;
            try
            {
                foreach (var c in LoadedChits)
                {
                    c.IsSelected = false;
                }
                IsAllTilesSelected = false;
                SelectedChit = null;
                NotifySelectionChanges();
            }
            finally
            {
                _isSyncingTileSelection = false;
            }
        }

        public bool CanDeselectAllTiles => HasSelectedChits;

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void InvertTileSelection()
        {
            if (_isSyncingTileSelection) return;
            _isSyncingTileSelection = true;
            try
            {
                foreach (var c in LoadedChits)
                {
                    c.IsSelected = !c.IsSelected;
                }
                bool allSelected = LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected);
                IsAllTilesSelected = allSelected;
                if (SelectedChit != null && !SelectedChit.IsSelected)
                {
                    SelectedChit = LoadedChits.FirstOrDefault(c => c.IsSelected);
                }
                else if (SelectedChit == null)
                {
                    SelectedChit = LoadedChits.FirstOrDefault(c => c.IsSelected);
                }
                NotifySelectionChanges();
            }
            finally
            {
                _isSyncingTileSelection = false;
            }
        }

        private void NotifySelectionChanges()
        {
            OnPropertyChanged(nameof(HasSelectedChits));
            OnPropertyChanged(nameof(HasCheckedChits));
            OnPropertyChanged(nameof(SharedToolBatchActionName));
            OnPropertyChanged(nameof(SharedToolBatchActionToolTip));
            OnPropertyChanged(nameof(RotateBatchActionName));
            OnPropertyChanged(nameof(RotateBatchActionToolTip));
            OnPropertyChanged(nameof(SmoothBatchActionName));
            OnPropertyChanged(nameof(SmoothBatchActionToolTip));
            OnPropertyChanged(nameof(DespeckleBatchActionName));
            OnPropertyChanged(nameof(DespeckleBatchActionToolTip));
            OnPropertyChanged(nameof(SharpenBatchActionName));
            OnPropertyChanged(nameof(SharpenBatchActionToolTip));
            OnPropertyChanged(nameof(AutoTrimBatchActionName));
            OnPropertyChanged(nameof(AutoTrimBatchActionToolTip));
            ApplySharedToolAllCommand.NotifyCanExecuteChanged();
            RotateAllTilesRightCommand.NotifyCanExecuteChanged();
            RotateAllTilesLeftCommand.NotifyCanExecuteChanged();
            BevelAllCommand.NotifyCanExecuteChanged();
            ClipAllCommand.NotifyCanExecuteChanged();
            SmoothAllTilesCommand.NotifyCanExecuteChanged();
            DespeckleAllTilesCommand.NotifyCanExecuteChanged();
            SharpenAllTilesCommand.NotifyCanExecuteChanged();
            AutoTrimAllTilesCommand.NotifyCanExecuteChanged();
            ApplyScaleImageCommand.NotifyCanExecuteChanged();
            ApplyResizeImageCommand.NotifyCanExecuteChanged();
            BumpChitLeftCommand.NotifyCanExecuteChanged();
            BumpChitRightCommand.NotifyCanExecuteChanged();
            BumpChitUpCommand.NotifyCanExecuteChanged();
            BumpChitDownCommand.NotifyCanExecuteChanged();
            ChitEditorZoomResetCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanCutTile));
            OnPropertyChanged(nameof(CanCopyTile));
            OnPropertyChanged(nameof(CanPasteImageToTile));
            OnPropertyChanged(nameof(CanPasteImageToNewTile));
            OnPropertyChanged(nameof(CanDeleteCurrentTile));
            OnPropertyChanged(nameof(CanDeleteSelectedTiles));
            OnPropertyChanged(nameof(CanResetCurrentTile));
            OnPropertyChanged(nameof(CanResetAllTiles));
            SelectAllTilesCommand.NotifyCanExecuteChanged();
            DeselectAllTilesCommand.NotifyCanExecuteChanged();
            InvertTileSelectionCommand.NotifyCanExecuteChanged();
            CutChitCommand.NotifyCanExecuteChanged();
            CopyChitCommand.NotifyCanExecuteChanged();
            PasteImageToTileCommand.NotifyCanExecuteChanged();
            PasteImageToNewTileCommand.NotifyCanExecuteChanged();
            DeleteCurrentTileCommand.NotifyCanExecuteChanged();
            DeleteSelectedTilesCommand.NotifyCanExecuteChanged();
            ResetSelectedChitCommand.NotifyCanExecuteChanged();
            ResetAllChitsCommand.NotifyCanExecuteChanged();
        }

        private void OnChitItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChitItem.IsSelected))
            {
                if (sender is ChitItem chit)
                {
                    if (!chit.IsSelected)
                    {
                        if (ReferenceEquals(SelectedChit, chit))
                        {
                            SelectedChit = LoadedChits.FirstOrDefault(c => c.IsSelected);
                        }
                    }
                    else if (!_isSyncingTileSelection && SelectedChit == null)
                    {
                        SelectedChit = chit;
                    }
                }

                if (_isSyncingTileSelection) return;
                _isSyncingTileSelection = true;
                try
                {
                    bool allSelected = LoadedChits.Count > 0 && LoadedChits.All(c => c.IsSelected);
                    IsAllTilesSelected = allSelected;
                    NotifySelectionChanges();
                }
                finally
                {
                    _isSyncingTileSelection = false;
                }
            }
        }

        public bool HasSelectedChits => LoadedChits.Any(c => c.IsSelected);
        public bool HasCheckedChits => HasSelectedChits;

        private readonly Stack<List<ChitItem>> _batchEditHistory = new();
        private readonly Stack<List<ChitItem>> _batchEditRedoHistory = new();

        public void RecordEditHistory(List<ChitItem> affectedChits)
        {
            if (affectedChits.Count == 0) return;
            _batchEditHistory.Push(affectedChits);
            _batchEditRedoHistory.Clear();
            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();
        }

        public IReadOnlyList<int> AvailableBevelSizes { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        public IReadOnlyList<int> AvailableClipSizes { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        public IReadOnlyList<int> AvailableClearSizes { get; } = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        [ObservableProperty]
        private Color _chitEdgeCleanColor = Color.FromRgb(220, 40, 30);

        [ObservableProperty]
        private int _chitEdgeCleanDepth = 2;

        [ObservableProperty]
        private int _chitEdgeCleanTopDepth = 2;

        [ObservableProperty]
        private int _chitEdgeCleanBottomDepth = 2;

        [ObservableProperty]
        private int _chitEdgeCleanLeftDepth = 2;

        [ObservableProperty]
        private int _chitEdgeCleanRightDepth = 2;

        [ObservableProperty]
        private int _chitEdgeCleanTolerance = 35;

        [ObservableProperty]
        private string _selectedEdgeCleanMode = "Targeted Replace";

        [ObservableProperty]
        private bool _preserveTransparentCorners = true;

        [ObservableProperty]
        private bool _lockEdgeCleanColor = false;

        [ObservableProperty]
        private bool _linkEdgeCleanSides = false;

        partial void OnChitEdgeCleanTopDepthChanged(int value) => UpdateLiveChitPreview();
        partial void OnChitEdgeCleanBottomDepthChanged(int value) => UpdateLiveChitPreview();
        partial void OnChitEdgeCleanLeftDepthChanged(int value) => UpdateLiveChitPreview();
        partial void OnChitEdgeCleanRightDepthChanged(int value) => UpdateLiveChitPreview();
        partial void OnChitEdgeCleanToleranceChanged(int value) => UpdateLiveChitPreview();
        partial void OnSelectedEdgeCleanModeChanged(string value) => UpdateLiveChitPreview();
        partial void OnChitEdgeCleanColorChanged(Color value) => UpdateLiveChitPreview();
        partial void OnPreserveTransparentCornersChanged(bool value) => UpdateLiveChitPreview();
        partial void OnTransparentColorChanged(Color value) => UpdateLiveChitPreview();

        private string? _lastCommittedSignature;
        private string? _lastCommittedBevelSignature;
        private string? _lastCommittedClipSignature;

        private string GetCurrentEdgeCleanSignature()
        {
            return $"{ChitEdgeCleanTopDepth}_{ChitEdgeCleanBottomDepth}_{ChitEdgeCleanLeftDepth}_{ChitEdgeCleanRightDepth}_{ChitEdgeCleanTolerance}_{SelectedEdgeCleanMode}_{ChitEdgeCleanColor.R}_{ChitEdgeCleanColor.G}_{ChitEdgeCleanColor.B}_{PreserveTransparentCorners}_{TransparentColor.R}_{TransparentColor.G}_{TransparentColor.B}";
        }

        private string GetCurrentBevelPreviewSignature()
        {
            return $"{SelectedChit?.Index}_{ChitEditorClipSize}_{ChitEditorBevelSize}_{TransparentColor.R}_{TransparentColor.G}_{TransparentColor.B}";
        }

        private string GetCurrentClipPreviewSignature()
        {
            return $"{SelectedChit?.Index}_{ChitEditorClipSize}_{TransparentColor.R}_{TransparentColor.G}_{TransparentColor.B}";
        }

        public void UpdateLiveChitPreview()
        {
            if (SelectedTabIndex != 2 || SelectedChit == null || SelectedChit.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed)
            {
                SelectedChit?.SetPreviewMat(null);
                return;
            }

            // 1. If edge clean depths are set and active, edge clean preview takes priority
            if (ChitEdgeCleanTopDepth > 0 || ChitEdgeCleanBottomDepth > 0 || ChitEdgeCleanLeftDepth > 0 || ChitEdgeCleanRightDepth > 0)
            {
                UpdateLiveEdgeCleanPreview();
                return;
            }

            // 2. Handle Shared Tool Mode previews (Bevel, Clip, Clear)
            try
            {
                if (SelectedSharedToolMode == "Bevel")
                {
                    string sig = GetCurrentBevelPreviewSignature();
                    if (sig == _lastCommittedBevelSignature)
                    {
                        SelectedChit.SetPreviewMat(null);
                        return;
                    }

                    Vec4b transColor = ColorToVec4b(TransparentColor);
                    using var clipped = ChitEditorService.ApplyCornerClip(SelectedChit.WorkingMat, ChitEditorClipSize, transColor);
                    using var beveled = ChitEditorService.ApplyBevel(clipped, ChitEditorBevelSize, transparentColor: transColor);
                    SelectedChit.SetPreviewMat(beveled);
                }
                else if (SelectedSharedToolMode == "Clip")
                {
                    string sig = GetCurrentClipPreviewSignature();
                    if (sig == _lastCommittedClipSignature)
                    {
                        SelectedChit.SetPreviewMat(null);
                        return;
                    }

                    Vec4b transColor = ColorToVec4b(TransparentColor);
                    using var clipped = ChitEditorService.ApplyCornerClip(SelectedChit.WorkingMat, ChitEditorClipSize, transColor);
                    SelectedChit.SetPreviewMat(clipped);
                }
                else
                {
                    SelectedChit.SetPreviewMat(null);
                }
            }
            catch
            {
                SelectedChit.SetPreviewMat(null);
            }
        }

        private void UpdateLiveEdgeCleanPreview()
        {
            if (SelectedChit == null || SelectedChit.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed)
            {
                return;
            }

            int top = ChitEdgeCleanTopDepth;
            int bottom = ChitEdgeCleanBottomDepth;
            int left = ChitEdgeCleanLeftDepth;
            int right = ChitEdgeCleanRightDepth;

            string currentSig = GetCurrentEdgeCleanSignature();
            if ((top == 0 && bottom == 0 && left == 0 && right == 0) || currentSig == _lastCommittedSignature)
            {
                SelectedChit.SetPreviewMat(null);
                return;
            }

            try
            {
                Vec4b targetBg = ColorToVec4b(ChitEdgeCleanColor);
                Vec4b transColor = ColorToVec4b(TransparentColor);
                using Mat previewMat = ChitEditorService.ApplyEdgeCleanup(
                    SelectedChit.WorkingMat,
                    top,
                    bottom,
                    left,
                    right,
                    ChitEdgeCleanTolerance,
                    ChitEdgeCleanMode,
                    targetBg,
                    PreserveTransparentCorners,
                    transColor);

                SelectedChit.SetPreviewMat(previewMat);
            }
            catch
            {
                SelectedChit.SetPreviewMat(null);
            }
        }

        public int MaxEdgeCleanVerticalDepth => SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed
            ? Math.Max(0, SelectedChit.WorkingMat.Height / 2)
            : 50;

        public int MaxEdgeCleanHorizontalDepth => SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed
            ? Math.Max(0, SelectedChit.WorkingMat.Width / 2)
            : 50;

        private void ClampEdgeCleanDepthsForDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            int maxVert = Math.Max(0, height / 2);
            int maxHoriz = Math.Max(0, width / 2);

            int top = Math.Clamp(ChitEdgeCleanTopDepth, 0, maxVert);
            int bottom = Math.Clamp(ChitEdgeCleanBottomDepth, 0, maxVert);
            while (top + bottom > height - 1 && (top > 0 || bottom > 0))
            {
                if (top >= bottom && top > 0) top--;
                else if (bottom > 0) bottom--;
            }

            int left = Math.Clamp(ChitEdgeCleanLeftDepth, 0, maxHoriz);
            int right = Math.Clamp(ChitEdgeCleanRightDepth, 0, maxHoriz);
            while (left + right > width - 1 && (left > 0 || right > 0))
            {
                if (left >= right && left > 0) left--;
                else if (right > 0) right--;
            }

            ChitEdgeCleanTopDepth = top;
            ChitEdgeCleanBottomDepth = bottom;
            ChitEdgeCleanLeftDepth = left;
            ChitEdgeCleanRightDepth = right;
        }

        private void ClampCurrentEdgeCleanDepths()
        {
            if (SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed)
            {
                ClampEdgeCleanDepthsForDimensions(SelectedChit.WorkingMat.Width, SelectedChit.WorkingMat.Height);
            }
        }

        partial void OnLinkEdgeCleanSidesChanged(bool value)
        {
            if (value)
            {
                SetAllDepths(ChitEdgeCleanTopDepth);
            }
        }

        private void SetAllDepths(int val)
        {
            int maxV = MaxEdgeCleanVerticalDepth;
            int maxH = MaxEdgeCleanHorizontalDepth;
            int maxBoth = Math.Min(maxV, maxH);
            int clamped = Math.Clamp(val, 0, maxBoth);
            ChitEdgeCleanTopDepth = clamped;
            ChitEdgeCleanBottomDepth = clamped;
            ChitEdgeCleanLeftDepth = clamped;
            ChitEdgeCleanRightDepth = clamped;
            ClampCurrentEdgeCleanDepths();
        }

        public IReadOnlyList<int> AvailableEdgeCleanDepths { get; } = new[] { 1, 2, 3, 4, 5, 6, 8, 10 };

        public IReadOnlyList<string> AvailableEdgeCleanModes { get; } = new[]
        {
            "Targeted Replace",
            "Smart Inpaint",
            "Solid Flood"
        };

        public EdgeCleanMode ChitEdgeCleanMode => SelectedEdgeCleanMode switch
        {
            "Smart Inpaint" => EdgeCleanMode.SmartInpaint,
            "Solid Flood" => EdgeCleanMode.SolidFlood,
            _ => EdgeCleanMode.TargetedReplace
        };

        [RelayCommand]
        private void ChitEditorZoomIn()
        {
            ChitEditorZoomScale = Math.Min(Math.Round(ChitEditorZoomScale + 0.01, 2), 4.0);
        }

        [RelayCommand]
        private void ChitEditorZoomOut()
        {
            ChitEditorZoomScale = Math.Max(Math.Round(ChitEditorZoomScale - 0.01, 2), 0.10);
        }

        [RelayCommand(CanExecute = nameof(CanResetChitEditorZoom))]
        private void ChitEditorZoomReset()
        {
            if (!HasChitsLoaded) return;
            ChitEditorZoomScale = 1.0;
            ChitEditorZoomInputText = "100%";
        }

        public bool CanResetChitEditorZoom => HasChitsLoaded && Math.Abs(ChitEditorZoomScale - 1.0) > 0.001;

        [RelayCommand]
        public void ApplyChitEditorZoomInput(string? input = null)
        {
            string raw = (input ?? ChitEditorZoomInputText)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                ChitEditorZoomInputText = $"{Math.Round(ChitEditorZoomScale * 100):F0}%";
                return;
            }

            if (raw.EndsWith('%'))
            {
                raw = raw[..^1].Trim();
            }

            raw = raw.Replace(',', '.');

            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
            {
                if (!double.IsNaN(parsedVal) && !double.IsInfinity(parsedVal))
                {
                    double clampedPercent = Math.Clamp(parsedVal, 10.0, 400.0);
                    ChitEditorZoomScale = Math.Round(clampedPercent / 100.0, 2);
                }
            }

            ChitEditorZoomInputText = $"{Math.Round(ChitEditorZoomScale * 100):F0}%";
        }

        [RelayCommand]
        private void ChitEdgeCleanTopStepUp()
        {
            int maxV = MaxEdgeCleanVerticalDepth;
            int maxH = MaxEdgeCleanHorizontalDepth;

            if (LinkEdgeCleanSides)
            {
                int maxBoth = Math.Min(maxV, maxH);
                int target = Math.Min(ChitEdgeCleanTopDepth + 1, maxBoth);
                SetAllDepths(target);
            }
            else
            {
                int maxAllowed = SelectedChit?.WorkingMat != null
                    ? Math.Min(maxV, SelectedChit.WorkingMat.Height - ChitEdgeCleanBottomDepth - 1)
                    : maxV;
                ChitEdgeCleanTopDepth = Math.Clamp(ChitEdgeCleanTopDepth + 1, 0, Math.Max(0, maxAllowed));
            }
        }

        [RelayCommand]
        private void ChitEdgeCleanTopStepDown()
        {
            if (LinkEdgeCleanSides) SetAllDepths(ChitEdgeCleanTopDepth - 1);
            else ChitEdgeCleanTopDepth = Math.Max(ChitEdgeCleanTopDepth - 1, 0);
        }

        [RelayCommand]
        private void ChitEdgeCleanBottomStepUp()
        {
            int maxV = MaxEdgeCleanVerticalDepth;
            int maxH = MaxEdgeCleanHorizontalDepth;

            if (LinkEdgeCleanSides)
            {
                int maxBoth = Math.Min(maxV, maxH);
                int target = Math.Min(ChitEdgeCleanBottomDepth + 1, maxBoth);
                SetAllDepths(target);
            }
            else
            {
                int maxAllowed = SelectedChit?.WorkingMat != null
                    ? Math.Min(maxV, SelectedChit.WorkingMat.Height - ChitEdgeCleanTopDepth - 1)
                    : maxV;
                ChitEdgeCleanBottomDepth = Math.Clamp(ChitEdgeCleanBottomDepth + 1, 0, Math.Max(0, maxAllowed));
            }
        }

        [RelayCommand]
        private void ChitEdgeCleanBottomStepDown()
        {
            if (LinkEdgeCleanSides) SetAllDepths(ChitEdgeCleanBottomDepth - 1);
            else ChitEdgeCleanBottomDepth = Math.Max(ChitEdgeCleanBottomDepth - 1, 0);
        }

        [RelayCommand]
        private void ChitEdgeCleanLeftStepUp()
        {
            int maxV = MaxEdgeCleanVerticalDepth;
            int maxH = MaxEdgeCleanHorizontalDepth;

            if (LinkEdgeCleanSides)
            {
                int maxBoth = Math.Min(maxV, maxH);
                int target = Math.Min(ChitEdgeCleanLeftDepth + 1, maxBoth);
                SetAllDepths(target);
            }
            else
            {
                int maxAllowed = SelectedChit?.WorkingMat != null
                    ? Math.Min(maxH, SelectedChit.WorkingMat.Width - ChitEdgeCleanRightDepth - 1)
                    : maxH;
                ChitEdgeCleanLeftDepth = Math.Clamp(ChitEdgeCleanLeftDepth + 1, 0, Math.Max(0, maxAllowed));
            }
        }

        [RelayCommand]
        private void ChitEdgeCleanLeftStepDown()
        {
            if (LinkEdgeCleanSides) SetAllDepths(ChitEdgeCleanLeftDepth - 1);
            else ChitEdgeCleanLeftDepth = Math.Max(ChitEdgeCleanLeftDepth - 1, 0);
        }

        [RelayCommand]
        private void ChitEdgeCleanRightStepUp()
        {
            int maxV = MaxEdgeCleanVerticalDepth;
            int maxH = MaxEdgeCleanHorizontalDepth;

            if (LinkEdgeCleanSides)
            {
                int maxBoth = Math.Min(maxV, maxH);
                int target = Math.Min(ChitEdgeCleanRightDepth + 1, maxBoth);
                SetAllDepths(target);
            }
            else
            {
                int maxAllowed = SelectedChit?.WorkingMat != null
                    ? Math.Min(maxH, SelectedChit.WorkingMat.Width - ChitEdgeCleanLeftDepth - 1)
                    : maxH;
                ChitEdgeCleanRightDepth = Math.Clamp(ChitEdgeCleanRightDepth + 1, 0, Math.Max(0, maxAllowed));
            }
        }

        [RelayCommand]
        private void ChitEdgeCleanRightStepDown()
        {
            if (LinkEdgeCleanSides) SetAllDepths(ChitEdgeCleanRightDepth - 1);
            else ChitEdgeCleanRightDepth = Math.Max(ChitEdgeCleanRightDepth - 1, 0);
        }

        [RelayCommand]
        private void ChitEdgeCleanTolIncrement() => ChitEdgeCleanTolerance = Math.Min(ChitEdgeCleanTolerance + 5, 200);

        [RelayCommand]
        private void ChitEdgeCleanTolDecrement() => ChitEdgeCleanTolerance = Math.Max(ChitEdgeCleanTolerance - 5, 5);

        [RelayCommand]
        private void ChitInspectionZoomIn()
        {
            ChitInspectionZoomScale = Math.Min(Math.Round(ChitInspectionZoomScale + 0.25, 2), 4.0);
        }

        [RelayCommand]
        private void ChitInspectionZoomOut()
        {
            ChitInspectionZoomScale = Math.Max(Math.Round(ChitInspectionZoomScale - 0.25, 2), 1.0);
        }

        [RelayCommand]
        private void SetChitInspectionZoom(string? scaleStr)
        {
            if (double.TryParse(scaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double scale))
            {
                ChitInspectionZoomScale = Math.Clamp(Math.Round(scale, 2), 1.0, 4.0);
            }
        }

        private bool CanEditChit() => SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed && (SelectedChit?.IsSelected ?? false);
        private bool CanResizeChits() => HasSelectedChits || CanEditChit();
        private bool CanBumpChits() => CanEditChit();

        public IRelayCommand PasteChitCommand => PasteImageToTileCommand;

        [RelayCommand(CanExecute = nameof(CanCutTile))]
        private void CutChit()
        {
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;

            // 1. Copy active chit image to clipboard
            if (!CopyChitToClipboardInternal(SelectedChit.WorkingMat))
            {
                StatusMessage = "Cut failed: Unable to access Windows clipboard.";
                return;
            }
            UpdateClipboardState();

            int w = Math.Max(8, SelectedChit.WorkingMat.Width);
            int h = Math.Max(8, SelectedChit.WorkingMat.Height);

            // 2. Clear to transparent canvas
            var blankMat = new Mat(h, w, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
            SelectedChit.UpdateWorkingMat(blankMat, recordHistory: true, Colors.Transparent);

            _lastCommittedSignature = null;
            UpdateLiveEdgeCleanPreview();
            StatusMessage = $"Cut tile #{SelectedChit.Index} to clipboard ({w} × {h} px).";
        }

        [RelayCommand(CanExecute = nameof(CanCopyTile))]
        private void CopyChit()
        {
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;

            if (CopyChitToClipboardInternal(SelectedChit.WorkingMat))
            {
                UpdateClipboardState();
                StatusMessage = $"Copied tile #{SelectedChit.Index} to clipboard ({SelectedChit.WorkingMat.Width} × {SelectedChit.WorkingMat.Height} px).";
            }
            else
            {
                StatusMessage = "Copy failed: Unable to access Windows clipboard.";
            }
        }

        [RelayCommand(CanExecute = nameof(CanPasteImageToTile))]
        private void PasteImageToTile()
        {
            if (SelectedChit == null) return;

            Mat? rawMat = null;
            try
            {
                rawMat = GetMatFromClipboard();
                if (rawMat == null)
                {
                    StatusMessage = "Clipboard does not contain a supported image format.";
                    return;
                }

                if (rawMat.Width < 8 || rawMat.Height < 8)
                {
                    rawMat.Dispose();
                    StatusMessage = "Clipboard image is too small (minimum 8×8 px).";
                    return;
                }

                int targetW = SelectedChit.PixelWidth;
                int targetH = SelectedChit.PixelHeight;

                var result = MessageBox.Show(
                    $"Overwrite tile #{SelectedChit.Index} ({targetW}×{targetH} px) with the clipboard image ({rawMat.Width}×{rawMat.Height} px scaled to fit)?\n\nThis will replace the current tile artwork.",
                    "Confirm Paste Image",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    rawMat.Dispose();
                    return;
                }

                Mat scaledMat;
                if (rawMat.Width == targetW && rawMat.Height == targetH)
                {
                    scaledMat = rawMat;
                }
                else
                {
                    scaledMat = new Mat();
                    var flags = (rawMat.Width > targetW && rawMat.Height > targetH)
                        ? InterpolationFlags.Area
                        : InterpolationFlags.Lanczos4;
                    Cv2.Resize(rawMat, scaledMat, new OpenCvSharp.Size(targetW, targetH), 0, 0, flags);
                    rawMat.Dispose();
                }

                SelectedChit.UpdateWorkingMat(scaledMat, recordHistory: true, TransparentColor);
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                UpdateLiveEdgeCleanPreview();

                StatusMessage = $"Pasted clipboard image into tile #{SelectedChit.Index} ({targetW} × {targetH} px).";
            }
            catch (Exception ex)
            {
                rawMat?.Dispose();
                StatusMessage = $"Paste failed: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanPasteImageToNewTile))]
        private void PasteImageToNewTile()
        {
            Mat? rawMat = null;
            try
            {
                rawMat = GetMatFromClipboard();
                if (rawMat == null)
                {
                    StatusMessage = "Clipboard does not contain a supported image format.";
                    return;
                }

                if (rawMat.Width < 8 || rawMat.Height < 8)
                {
                    rawMat.Dispose();
                    StatusMessage = "Clipboard image is too small (minimum 8×8 px).";
                    return;
                }

                int newIndex = LoadedChits.Count > 0 ? LoadedChits.Max(c => c.Index) + 1 : 1;
                string side = IsRearSide(SelectedSideTag) ? "Rear" : "Front";
                string prefix = !string.IsNullOrWhiteSpace(NamingPrefix) ? NamingPrefix : "Tile";
                string pieceName = $"{prefix}_Custom_{newIndex:D2}_{side}.png";

                var newChit = new ChitItem(
                    index: newIndex,
                    row: 0,
                    col: 0,
                    side: side,
                    pieceName: pieceName,
                    bgraMat: rawMat
                );

                _isSyncingTileSelection = true;
                try
                {
                    foreach (var c in LoadedChits)
                    {
                        c.IsSelected = false;
                    }
                    newChit.IsSelected = true;
                }
                finally
                {
                    _isSyncingTileSelection = false;
                }

                newChit.PropertyChanged += OnChitItemPropertyChanged;
                LoadedChits.Add(newChit);

                SelectedChit = newChit;
                ChitEditorWidth = newChit.PixelWidth;
                ChitEditorHeight = newChit.PixelHeight;

                NotifySelectionChanges();
                StatusMessage = $"Created new tile #{newIndex} ({newChit.PixelWidth} × {newChit.PixelHeight} px) from clipboard image.";
            }
            catch (Exception ex)
            {
                rawMat?.Dispose();
                StatusMessage = $"Failed to paste new tile: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanDeleteCurrentTile))]
        private void DeleteCurrentTile()
        {
            if (SelectedChit == null) return;

            DeactivateCloneStamp();

            var target = SelectedChit;
            var result = MessageBox.Show(
                $"Are you sure you want to delete tile #{target.Index} ({target.PieceName})?\n\nThis cannot be undone.",
                "Confirm Delete Current Tile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            int idx = LoadedChits.IndexOf(target);

            target.PropertyChanged -= SelectedChit_PropertyChanged;
            target.PropertyChanged -= OnChitItemPropertyChanged;

            ScrubChitsFromHistory(new[] { target });

            LoadedChits.Remove(target);
            target.Dispose();

            if (LoadedChits.Count > 0)
            {
                int nextIdx = Math.Clamp(idx, 0, LoadedChits.Count - 1);
                SelectedChit = LoadedChits[nextIdx];
                SelectedChit.IsSelected = true;
                ChitEditorWidth = SelectedChit.PixelWidth;
                ChitEditorHeight = SelectedChit.PixelHeight;
            }
            else
            {
                SelectedChit = null;
                ChitEditorWidth = 0;
                ChitEditorHeight = 0;
            }

            NotifySelectionChanges();
            StatusMessage = LoadedChits.Count > 0
                ? $"Deleted tile #{target.Index}. {LoadedChits.Count} tile(s) remaining."
                : $"Deleted tile #{target.Index}. No tiles remaining in Tile Editor.";

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        [RelayCommand(CanExecute = nameof(CanDeleteSelectedTiles))]
        private void DeleteSelectedTiles()
        {
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0) return;

            DeactivateCloneStamp();

            string promptText = targets.Count == 1
                ? $"Are you sure you want to delete selected tile #{targets[0].Index} ({targets[0].PieceName})?\n\nThis cannot be undone."
                : $"Are you sure you want to delete {targets.Count} selected tiles?\n\nThis cannot be undone.";

            var result = MessageBox.Show(
                promptText,
                "Confirm Delete Selected Tiles",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            if (SelectedChit != null && targets.Contains(SelectedChit))
            {
                SelectedChit.PropertyChanged -= SelectedChit_PropertyChanged;
                SelectedChit = null;
            }

            foreach (var chit in targets)
            {
                chit.PropertyChanged -= SelectedChit_PropertyChanged;
                chit.PropertyChanged -= OnChitItemPropertyChanged;
                LoadedChits.Remove(chit);
            }

            ScrubChitsFromHistory(targets);

            foreach (var chit in targets)
            {
                chit.Dispose();
            }

            if (LoadedChits.Count > 0)
            {
                SelectedChit = LoadedChits[0];
                SelectedChit.IsSelected = true;
                ChitEditorWidth = SelectedChit.PixelWidth;
                ChitEditorHeight = SelectedChit.PixelHeight;
            }
            else
            {
                SelectedChit = null;
                ChitEditorWidth = 0;
                ChitEditorHeight = 0;
            }

            NotifySelectionChanges();
            StatusMessage = LoadedChits.Count > 0
                ? $"Deleted {targets.Count} selected tile(s). {LoadedChits.Count} tile(s) remaining."
                : $"Deleted {targets.Count} selected tile(s). No tiles remaining in Tile Editor.";

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void ScrubChitsFromHistory(IEnumerable<ChitItem> deletedChits)
        {
            var toRemoveSet = new HashSet<ChitItem>(deletedChits);

            // 1. Scrub undo history
            var undoList = _batchEditHistory.ToList();
            _batchEditHistory.Clear();
            for (int i = undoList.Count - 1; i >= 0; i--)
            {
                var list = undoList[i];
                list.RemoveAll(c => toRemoveSet.Contains(c));
                if (list.Count > 0)
                {
                    _batchEditHistory.Push(list);
                }
            }

            // 2. Scrub redo history
            var redoList = _batchEditRedoHistory.ToList();
            _batchEditRedoHistory.Clear();
            for (int i = redoList.Count - 1; i >= 0; i--)
            {
                var list = redoList[i];
                list.RemoveAll(c => toRemoveSet.Contains(c));
                if (list.Count > 0)
                {
                    _batchEditRedoHistory.Push(list);
                }
            }
        }

        private static bool CopyChitToClipboardInternal(Mat mat)
        {
            if (mat == null || mat.IsDisposed) return false;

            try
            {
                var bitmapSource = ChitItem.MatToFrozenBitmapSource(mat);
                byte[] pngBytes = mat.ToBytes(".png");

                var dataObj = new System.Windows.DataObject();
                dataObj.SetImage(bitmapSource);

                using (var ms = new MemoryStream(pngBytes))
                {
                    dataObj.SetData("PNG", ms, false);
                    SetClipboardDataSafe(dataObj);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetClipboardDataSafe(System.Windows.DataObject dataObj)
        {
            for (int retry = 0; retry < 5; retry++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(dataObj, true);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static Mat? GetMatFromClipboard()
        {
            for (int retry = 0; retry < 5; retry++)
            {
                try
                {
                    var dataObj = System.Windows.Clipboard.GetDataObject();
                    if (dataObj == null) return null;

                    // 1. First priority: Check for 32-bit PNG stream for lossless alpha
                    string[] pngFormats = { "PNG", "image/png", "image/x-png" };
                    foreach (var format in pngFormats)
                    {
                        if (dataObj.GetDataPresent(format))
                        {
                            var pngData = dataObj.GetData(format);
                            if (pngData is Stream stream)
                            {
                                using var ms = new MemoryStream();
                                stream.CopyTo(ms);
                                byte[] bytes = ms.ToArray();
                                if (bytes.Length > 0)
                                {
                                    var decoded = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
                                    if (decoded != null && !decoded.Empty())
                                    {
                                        return EnsureBgraMat(decoded);
                                    }
                                }
                            }
                            else if (pngData is byte[] bytes && bytes.Length > 0)
                            {
                                var decoded = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
                                if (decoded != null && !decoded.Empty())
                                {
                                    return EnsureBgraMat(decoded);
                                }
                            }
                        }
                    }

                    // 2. Second priority: Standard BitmapSource image
                    if (dataObj.GetDataPresent(System.Windows.DataFormats.Bitmap) || System.Windows.Clipboard.ContainsImage())
                    {
                        var imageSource = System.Windows.Clipboard.GetImage();
                        if (imageSource != null)
                        {
                            return BitmapSourceToBgraMat(imageSource);
                        }
                    }

                    // 3. Third priority: File drop list (image file copied in Windows Explorer)
                    if (dataObj.GetDataPresent(System.Windows.DataFormats.FileDrop))
                    {
                        if (dataObj.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                        {
                            string filePath = files[0];
                            string ext = Path.GetExtension(filePath).ToLowerInvariant();
                            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff")
                            {
                                var loaded = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                                if (loaded != null && !loaded.Empty())
                                {
                                    return EnsureBgraMat(loaded);
                                }
                            }
                        }
                    }

                    return null;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(50);
                }
            }
            return null;
        }

        private static Mat EnsureBgraMat(Mat src)
        {
            if (src.Type() == MatType.CV_8UC4)
            {
                return src;
            }

            var bgra = new Mat();
            if (src.Type() == MatType.CV_8UC3)
            {
                Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
            }
            else if (src.Type() == MatType.CV_8UC1)
            {
                Cv2.CvtColor(src, bgra, ColorConversionCodes.GRAY2BGRA);
            }
            else
            {
                src.ConvertTo(src, MatType.CV_8U);
                if (src.Channels() == 1) Cv2.CvtColor(src, bgra, ColorConversionCodes.GRAY2BGRA);
                else if (src.Channels() == 3) Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
                else src.CopyTo(bgra);
            }
            src.Dispose();
            return bgra;
        }

        private static Mat BitmapSourceToBgraMat(BitmapSource source)
        {
            FormatConvertedBitmap formatted = new FormatConvertedBitmap();
            formatted.BeginInit();
            formatted.Source = source;
            formatted.DestinationFormat = PixelFormats.Bgra32;
            formatted.EndInit();
            formatted.Freeze();

            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixelBytes = new byte[height * stride];
            formatted.CopyPixels(pixelBytes, stride, 0);

            Mat mat = new Mat(height, width, MatType.CV_8UC4);
            System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, mat.Data, pixelBytes.Length);
            return mat;
        }

        [RelayCommand]
        private void SharedToolValueStepDown()
        {
            SharedToolValue = Math.Max(0, SharedToolValue - 1);
        }

        [RelayCommand]
        private void SharedToolValueStepUp()
        {
            int maxLimit = SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed
                ? Math.Max(1, Math.Min(SelectedChit.WorkingMat.Width, SelectedChit.WorkingMat.Height) / 2)
                : 100;
            SharedToolValue = Math.Min(maxLimit, SharedToolValue + 1);
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplySharedTool()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;

            if (SelectedSharedToolMode == "Clip")
            {
                ChitEditorClipSize = SharedToolValue;
                ApplyClip();
            }
            else
            {
                ChitEditorBevelSize = SharedToolValue;
                ApplyBevel();
            }
        }

        [RelayCommand(CanExecute = nameof(HasCheckedChits))]
        private void ApplySharedToolAll()
        {
            DeactivateCloneStamp();
            if (LoadedChits.Count == 0) return;

            if (SelectedSharedToolMode == "Clip")
            {
                ClipAll();
            }
            else
            {
                BevelAll();
            }
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ResetEdgeClean()
        {
            // 1. Reset all edge cleanup side depths to 0px
            ChitEdgeCleanTopDepth = 0;
            ChitEdgeCleanBottomDepth = 0;
            ChitEdgeCleanLeftDepth = 0;
            ChitEdgeCleanRightDepth = 0;

            // 2. Clear preview overlay
            _lastCommittedSignature = null;
            SelectedChit?.SetPreviewMat(null);

            // 3. If an edge cleanup was previously committed onto WorkingMat, revert to pre-edge-clean state
            if (_preEdgeCleanMat != null && !_preEdgeCleanMat.IsDisposed && SelectedChit != null)
            {
                SelectedChit.UpdateWorkingMat(_preEdgeCleanMat.Clone(), recordHistory: true, TransparentColor);
                StatusMessage = $"Reset edge cleanup: reverted tile #{SelectedChit.Index} to pre-cleanup state and set depths to 0px.";
            }
            else
            {
                StatusMessage = "Reset edge cleanup: depths set to 0px.";
            }

            UpdateLiveChitPreview();
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void UndoChit()
        {
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;

            if (_batchEditHistory.Count > 0)
            {
                var affected = _batchEditHistory.Pop();
                _batchEditRedoHistory.Push(affected);
                foreach (var chit in affected)
                {
                    if (chit.CanUndo)
                    {
                        chit.Undo();
                    }
                }
            }
            else
            {
                SelectedChit.Undo();
            }

            ChitEditorWidth = SelectedChit.PixelWidth;
            ChitEditorHeight = SelectedChit.PixelHeight;
            UpdateLiveChitPreview();
            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void RedoChit()
        {
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;

            if (_batchEditRedoHistory.Count > 0)
            {
                var affected = _batchEditRedoHistory.Pop();
                _batchEditHistory.Push(affected);
                foreach (var chit in affected)
                {
                    if (chit.CanRedo)
                    {
                        chit.Redo();
                    }
                }
            }
            else
            {
                SelectedChit.Redo();
            }

            ChitEditorWidth = SelectedChit.PixelWidth;
            ChitEditorHeight = SelectedChit.PixelHeight;
            UpdateLiveChitPreview();
            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyBevel()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.Where(c => c.IsSelected).ToList() : new List<ChitItem> { SelectedChit };
            Vec4b transColor = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var beveled = ChitEditorService.ApplyBevel(chit.WorkingMat, ChitEditorBevelSize, transparentColor: transColor);
                chit.UpdateWorkingMat(beveled, recordHistory: true, TransparentColor);
            }
            RecordEditHistory(targets);
            _lastCommittedBevelSignature = GetCurrentBevelPreviewSignature();
            _lastCommittedSignature = null;
            SelectedChit.SetPreviewMat(null);
            StatusMessage = ApplyToAllChits
                ? $"Applied {ChitEditorBevelSize}px bevel to {targets.Count} tiles."
                : $"Applied {ChitEditorBevelSize}px bevel to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyClip()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.Where(c => c.IsSelected).ToList() : new List<ChitItem> { SelectedChit };
            Vec4b key = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var clipped = ChitEditorService.ApplyCornerClip(chit.WorkingMat, ChitEditorClipSize, key);
                chit.UpdateWorkingMat(clipped, recordHistory: true, TransparentColor);
            }
            RecordEditHistory(targets);
            _lastCommittedClipSignature = GetCurrentClipPreviewSignature();
            _lastCommittedBevelSignature = null;
            _lastCommittedSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();
            StatusMessage = ApplyToAllChits
                ? $"Applied {ChitEditorClipSize}px corner clip to {targets.Count} tiles."
                : $"Applied {ChitEditorClipSize}px corner clip to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(HasSelectedChits))]
        private void BevelAll()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Bevel operation.";
                    return;
                }
            }

            int bevelSize = SelectedSharedToolMode == "Bevel" ? SharedToolValue : (ChitEditorBevelSize > 0 ? ChitEditorBevelSize : SharedToolValue);
            if (bevelSize <= 0) bevelSize = 4;
            Vec4b transColor = ColorToVec4b(TransparentColor);
            int count = 0;

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                // Per-chit bounds clamping: bevel size cannot exceed 1/3 of width or height
                int clampedBevel = Math.Clamp(bevelSize, 1, Math.Min(chit.WorkingMat.Width / 3, chit.WorkingMat.Height / 3));
                var beveled = ChitEditorService.ApplyBevel(chit.WorkingMat, clampedBevel, transparentColor: transColor);
                chit.UpdateWorkingMat(beveled, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            _lastCommittedSignature = null;
            SelectedChit?.SetPreviewMat(null);
            UpdateLiveChitPreview();
            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Applied {bevelSize}px bevel to all {count} loaded tiles."
                : $"Applied {bevelSize}px bevel to {count} selected tiles{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(HasSelectedChits))]
        private void ClipAll()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Clip operation.";
                    return;
                }
            }

            int clipSize = SelectedSharedToolMode == "Clip" ? SharedToolValue : (ChitEditorClipSize > 0 ? ChitEditorClipSize : SharedToolValue);
            if (clipSize <= 0) clipSize = 4;
            Vec4b key = ColorToVec4b(TransparentColor);
            int count = 0;

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                // Per-chit bounds clamping: corner clip radius cannot exceed half width or height
                int clampedClip = Math.Clamp(clipSize, 1, Math.Min(chit.WorkingMat.Width / 2, chit.WorkingMat.Height / 2));
                var clipped = ChitEditorService.ApplyCornerClip(chit.WorkingMat, clampedClip, key);
                chit.UpdateWorkingMat(clipped, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            _lastCommittedSignature = null;
            SelectedChit?.SetPreviewMat(null);
            UpdateLiveChitPreview();
            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Applied {clipSize}px corner clip to all {count} loaded tiles."
                : $"Applied {clipSize}px corner clip to {count} selected tiles{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(CanResizeChits))]
        private void ApplyScaleImage()
        {
            DeactivateCloneStamp();
            if (_isApplyingBatchEdit) return;
            _isApplyingBatchEdit = true;
            try
            {
                var targets = LoadedChits.Where(c => c.IsSelected).ToList();
                if (targets.Count == 0)
                {
                    if (SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed)
                    {
                        targets = new List<ChitItem> { SelectedChit };
                    }
                    else
                    {
                        StatusMessage = "No tiles selected for Scale Image operation.";
                        return;
                    }
                }

                int targetW = ChitEditorWidth;
                int targetH = ChitEditorHeight;
                if (IsResizeSquare)
                {
                    int squareSize = ChitEditorService.CalculateSquareDimension(targetW, targetH);
                    targetW = squareSize;
                    targetH = squareSize;
                    ChitEditorWidth = squareSize;
                    ChitEditorHeight = squareSize;
                }

                // Check if any target tile will actually change dimensions
                bool anyChanged = targets.Any(c => c.WorkingMat != null && !c.WorkingMat.IsDisposed && (c.WorkingMat.Width != targetW || c.WorkingMat.Height != targetH));
                if (!anyChanged)
                {
                    StatusMessage = targets.Count > 1
                        ? (targets.Count == LoadedChits.Count
                            ? $"All {targets.Count} tiles are already {targetW}×{targetH} px."
                            : $"{targets.Count} selected tiles are already {targetW}×{targetH} px.")
                        : $"Tile #{targets[0].Index} is already {targetW}×{targetH} px.";
                    return;
                }

                var modifiedTargets = new List<ChitItem>();
                foreach (var chit in targets)
                {
                    if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                    if (chit.WorkingMat.Width == targetW && chit.WorkingMat.Height == targetH) continue;

                    var scaled = ChitEditorService.ApplyScale(chit.WorkingMat, targetW, targetH);
                    chit.UpdateWorkingMat(scaled, recordHistory: true, TransparentColor);
                    modifiedTargets.Add(chit);
                }

                if (modifiedTargets.Count > 0)
                {
                    RecordEditHistory(modifiedTargets);
                    _lastCommittedSignature = null;
                    _lastCommittedBevelSignature = null;
                    _lastCommittedClipSignature = null;
                    SelectedChit?.SetPreviewMat(null);
                    ClampCurrentEdgeCleanDepths();
                    UpdateLiveChitPreview();
                }

                string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                    ? $" (active tile #{SelectedChit.Index} was excluded)"
                    : string.Empty;

                StatusMessage = targets.Count > 1
                    ? (targets.Count == LoadedChits.Count
                        ? $"Scaled all {targets.Count} tiles to {targetW}×{targetH} px."
                        : $"Scaled {targets.Count} selected tiles to {targetW}×{targetH} px{excludedNote}.")
                    : $"Scaled tile #{targets[0].Index} to {targetW}×{targetH} px.";
            }
            finally
            {
                _isApplyingBatchEdit = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanResizeChits))]
        private void ApplyResizeImage()
        {
            DeactivateCloneStamp();
            if (_isApplyingBatchEdit) return;
            _isApplyingBatchEdit = true;
            try
            {
                var targets = LoadedChits.Where(c => c.IsSelected).ToList();
                if (targets.Count == 0)
                {
                    if (SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed)
                    {
                        targets = new List<ChitItem> { SelectedChit };
                    }
                    else
                    {
                        StatusMessage = "No tiles selected for Resize Image operation.";
                        return;
                    }
                }

                Vec4b key = ColorToVec4b(TransparentColor);
                int targetW = ChitEditorWidth;
                int targetH = ChitEditorHeight;
                if (IsResizeSquare)
                {
                    int squareSize = ChitEditorService.CalculateSquareDimension(targetW, targetH);
                    targetW = squareSize;
                    targetH = squareSize;
                    ChitEditorWidth = squareSize;
                    ChitEditorHeight = squareSize;
                }

                // Check if any target tile will actually change dimensions
                bool anyChanged = targets.Any(c => c.WorkingMat != null && !c.WorkingMat.IsDisposed && (c.WorkingMat.Width != targetW || c.WorkingMat.Height != targetH));
                if (!anyChanged)
                {
                    StatusMessage = targets.Count > 1
                        ? (targets.Count == LoadedChits.Count
                            ? $"All {targets.Count} tiles are already {targetW}×{targetH} px."
                            : $"{targets.Count} selected tiles are already {targetW}×{targetH} px.")
                        : $"Tile #{targets[0].Index} is already {targetW}×{targetH} px.";
                    return;
                }

                var modifiedTargets = new List<ChitItem>();
                foreach (var chit in targets)
                {
                    if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                    if (chit.WorkingMat.Width == targetW && chit.WorkingMat.Height == targetH) continue;

                    var resized = ChitEditorService.ApplyResize(chit.WorkingMat, targetW, targetH, key);
                    chit.UpdateWorkingMat(resized, recordHistory: true, TransparentColor);
                    modifiedTargets.Add(chit);
                }

                if (modifiedTargets.Count > 0)
                {
                    RecordEditHistory(modifiedTargets);
                    _lastCommittedSignature = null;
                    _lastCommittedBevelSignature = null;
                    _lastCommittedClipSignature = null;
                    SelectedChit?.SetPreviewMat(null);
                    ClampCurrentEdgeCleanDepths();
                    UpdateLiveChitPreview();
                }

                string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                    ? $" (active tile #{SelectedChit.Index} was excluded)"
                    : string.Empty;

                StatusMessage = targets.Count > 1
                    ? (targets.Count == LoadedChits.Count
                        ? $"Resized canvas for all {targets.Count} tiles to {targetW}×{targetH} px."
                        : $"Resized canvas for {targets.Count} selected tiles to {targetW}×{targetH} px{excludedNote}.")
                    : $"Resized canvas for tile #{targets[0].Index} to {targetW}×{targetH} px.";
            }
            finally
            {
                _isApplyingBatchEdit = false;
            }
        }

        #region Chit Directional Bump / Shift Commands

        [ObservableProperty]
        private int _chitBumpStepPx = 1;

        [RelayCommand]
        private void BumpStepUp()
        {
            ChitBumpStepPx = Math.Min(50, ChitBumpStepPx + 1);
        }

        [RelayCommand]
        private void BumpStepDown()
        {
            ChitBumpStepPx = Math.Max(1, ChitBumpStepPx - 1);
        }

        [RelayCommand(CanExecute = nameof(CanBumpChits))]
        private void BumpChitLeft() => ApplyBump(-ChitBumpStepPx, 0);

        [RelayCommand(CanExecute = nameof(CanBumpChits))]
        private void BumpChitRight() => ApplyBump(ChitBumpStepPx, 0);

        [RelayCommand(CanExecute = nameof(CanBumpChits))]
        private void BumpChitUp() => ApplyBump(0, -ChitBumpStepPx);

        [RelayCommand(CanExecute = nameof(CanBumpChits))]
        private void BumpChitDown() => ApplyBump(0, ChitBumpStepPx);

        private void ApplyBump(int dx, int dy)
        {
            DeactivateCloneStamp();
            if (_isApplyingBatchEdit) return;
            _isApplyingBatchEdit = true;
            try
            {
                var targets = LoadedChits.Where(c => c.IsSelected).ToList();
                if (targets.Count == 0)
                {
                    if (SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed)
                    {
                        targets = new List<ChitItem> { SelectedChit };
                    }
                    else
                    {
                        StatusMessage = "No tiles selected for Bump operation.";
                        return;
                    }
                }

                Vec4b fill = ColorToVec4b(TransparentColor);

                foreach (var chit in targets)
                {
                    if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                    var bumped = ChitEditorService.ApplyBump(chit.WorkingMat, dx, dy, fill);
                    chit.UpdateWorkingMat(bumped, recordHistory: true, TransparentColor);
                }

                RecordEditHistory(targets);
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit?.SetPreviewMat(null);
                UpdateLiveChitPreview();

                string dir = (dx < 0) ? $"{Math.Abs(dx)}px Left"
                           : (dx > 0) ? $"{dx}px Right"
                           : (dy < 0) ? $"{Math.Abs(dy)}px Up"
                           : $"{dy}px Down";

                string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                    ? $" (active tile #{SelectedChit.Index} was excluded)"
                    : string.Empty;

                StatusMessage = targets.Count > 1
                    ? (targets.Count == LoadedChits.Count
                        ? $"Bumped all {targets.Count} tiles {dir}."
                        : $"Bumped {targets.Count} selected tiles {dir}{excludedNote}.")
                    : $"Bumped tile #{targets[0].Index} {dir}.";
            }
            finally
            {
                _isApplyingBatchEdit = false;
            }
        }

        #endregion

        private void AutoSampleEdgeColorInternal(ChitItem chit)
        {
            if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) return;
            Vec4b dominant = ChitEditorService.SampleDominantBackgroundColor(
                chit.WorkingMat,
                ChitEdgeCleanTopDepth,
                ChitEdgeCleanBottomDepth,
                ChitEdgeCleanLeftDepth,
                ChitEdgeCleanRightDepth,
                ColorToVec4b(TransparentColor));
            ChitEdgeCleanColor = Color.FromRgb(dominant.Item2, dominant.Item1, dominant.Item0);
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void AutoSampleEdgeColor()
        {
            if (SelectedChit?.WorkingMat == null) return;
            AutoSampleEdgeColorInternal(SelectedChit);
            StatusMessage = $"Auto-sampled background color #{ChitEdgeCleanColor.R:X2}{ChitEdgeCleanColor.G:X2}{ChitEdgeCleanColor.B:X2} from tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyEdgeClean()
        {
            if (SelectedChit?.WorkingMat == null) return;

            // Save pre-edge-cleanup snapshot so ResetEdgeClean can revert cleanly
            _preEdgeCleanMat?.Dispose();
            _preEdgeCleanMat = SelectedChit.WorkingMat.Clone();

            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };
            Vec4b transColor = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;

                // When applying to all chits, dynamically auto-sample each chit's background color per chit (unless locked)
                Vec4b targetBg = (!ApplyToAllChits || LockEdgeCleanColor)
                    ? ColorToVec4b(ChitEdgeCleanColor)
                    : ChitEditorService.SampleDominantBackgroundColor(
                        chit.WorkingMat,
                        ChitEdgeCleanTopDepth,
                        ChitEdgeCleanBottomDepth,
                        ChitEdgeCleanLeftDepth,
                        ChitEdgeCleanRightDepth,
                        transColor);

                var cleaned = ChitEditorService.ApplyEdgeCleanup(
                    chit.WorkingMat,
                    ChitEdgeCleanTopDepth,
                    ChitEdgeCleanBottomDepth,
                    ChitEdgeCleanLeftDepth,
                    ChitEdgeCleanRightDepth,
                    ChitEdgeCleanTolerance,
                    ChitEdgeCleanMode,
                    targetBg,
                    PreserveTransparentCorners,
                    transColor);

                chit.UpdateWorkingMat(cleaned, recordHistory: true, TransparentColor);
            }

            // Mark current settings as committed into WorkingMat to avoid double-application
            _lastCommittedSignature = GetCurrentEdgeCleanSignature();

            // Refresh preview display so it points to the newly committed WorkingMat without double-applying
            SelectedChit.SetPreviewMat(null);

            StatusMessage = ApplyToAllChits
                ? $"Applied {SelectedEdgeCleanMode} edge clean (T:{ChitEdgeCleanTopDepth} B:{ChitEdgeCleanBottomDepth} L:{ChitEdgeCleanLeftDepth} R:{ChitEdgeCleanRightDepth} px) to all {targets.Count} tiles."
                : $"Applied {SelectedEdgeCleanMode} edge clean (T:{ChitEdgeCleanTopDepth} B:{ChitEdgeCleanBottomDepth} L:{ChitEdgeCleanLeftDepth} R:{ChitEdgeCleanRightDepth} px) to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ResetSelectedChit()
        {
            DeactivateCloneStamp();
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.ResetToOriginal();
            ChitEditorWidth = SelectedChit.PixelWidth;
            ChitEditorHeight = SelectedChit.PixelHeight;
            int default4Pct = ChitEditorService.CalculateDefaultBevelSize(SelectedChit.PixelWidth, SelectedChit.PixelHeight);
            ChitEditorClipSize = default4Pct;
            ChitEditorBevelSize = default4Pct;
            if (SelectedSharedToolMode == "Bevel" || SelectedSharedToolMode == "Clip")
            {
                SharedToolValue = default4Pct;
            }
            CloneStampRadius = ChitEditorService.CalculateDefaultCloneStampSize(SelectedChit.PixelWidth, SelectedChit.PixelHeight);
            HasCloneAnchor = false;
            HasDestinationAnchor = false;
            IsPickingCloneAnchor = false;
            CloneOffsetDeltaX = 0;
            CloneOffsetDeltaY = 0;
            CloneHoverDstX = -1;
            CloneHoverDstY = -1;
            IsCloneHovering = false;
            ClampCurrentEdgeCleanDepths();
            UpdateLiveChitPreview();
            StatusMessage = $"Reset tile #{SelectedChit.Index} to original slice.";
        }

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void ResetAllChits()
        {
            DeactivateCloneStamp();
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            foreach (var chit in LoadedChits)
            {
                chit.ResetToOriginal();
            }
            if (SelectedChit != null)
            {
                ChitEditorWidth = SelectedChit.PixelWidth;
                ChitEditorHeight = SelectedChit.PixelHeight;
            }
            UpdateLiveChitPreview();
            StatusMessage = $"Reset all {LoadedChits.Count} tiles to original slices.";
        }

        #region Tile Rotation Commands

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void RotateCurrentTileLeft()
        {
            RotateCurrentTileInternal(clockwise: false);
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void RotateCurrentTileRight()
        {
            RotateCurrentTileInternal(clockwise: true);
        }

        private void RotateCurrentTileInternal(bool clockwise)
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;
            if (SelectedChit.WorkingMat.Width < 8 || SelectedChit.WorkingMat.Height < 8) return;

            var rotated = ChitEditorService.Rotate(SelectedChit.WorkingMat, clockwise);
            SelectedChit.UpdateWorkingMat(rotated, recordHistory: true, TransparentColor);

            ChitEditorWidth = SelectedChit.PixelWidth;
            ChitEditorHeight = SelectedChit.PixelHeight;
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();

            string dir = clockwise ? "90° right (clockwise)" : "90° left (counter-clockwise)";
            StatusMessage = $"Rotated tile #{SelectedChit.Index} {dir}.";
        }

        [RelayCommand(CanExecute = nameof(HasSelectedChits))]
        private void RotateAllTilesLeft()
        {
            RotateAllTilesInternal(clockwise: false);
        }

        [RelayCommand(CanExecute = nameof(HasSelectedChits))]
        private void RotateAllTilesRight()
        {
            RotateAllTilesInternal(clockwise: true);
        }

        private void RotateAllTilesInternal(bool clockwise)
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Rotate operation.";
                    return;
                }
            }

            int count = 0;
            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                var rotated = ChitEditorService.Rotate(chit.WorkingMat, clockwise);
                chit.UpdateWorkingMat(rotated, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            if (SelectedChit != null)
            {
                ChitEditorWidth = SelectedChit.PixelWidth;
                ChitEditorHeight = SelectedChit.PixelHeight;
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit.SetPreviewMat(null);
                UpdateLiveChitPreview();
            }

            string dir = clockwise ? "90° right (clockwise)" : "90° left (counter-clockwise)";
            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Rotated all {count} tiles {dir}."
                : $"Rotated {count} selected tiles {dir}{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void SmoothCurrentTile()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;
            if (SelectedChit.WorkingMat.Width < 8 || SelectedChit.WorkingMat.Height < 8) return;

            var (d, sigmaColor, sigmaSpace) = GetCurrentSmoothParameters();
            Vec4b key = ColorToVec4b(TransparentColor);

            var smoothed = ChitEditorService.ApplySmoothBackground(SelectedChit.WorkingMat, d, sigmaColor, sigmaSpace, key);
            SelectedChit.UpdateWorkingMat(smoothed, recordHistory: true, TransparentColor);

            RecordEditHistory(new List<ChitItem> { SelectedChit });
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();

            string presetName = IsSmoothStrong ? "Strong" : (IsSmoothMedium ? "Medium" : "Light");
            StatusMessage = $"Applied {presetName} smooth background to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void SmoothAllTiles()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Smooth operation.";
                    return;
                }
            }

            var (d, sigmaColor, sigmaSpace) = GetCurrentSmoothParameters();
            Vec4b key = ColorToVec4b(TransparentColor);
            int count = 0;

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                var smoothed = ChitEditorService.ApplySmoothBackground(chit.WorkingMat, d, sigmaColor, sigmaSpace, key);
                chit.UpdateWorkingMat(smoothed, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            if (SelectedChit != null)
            {
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit.SetPreviewMat(null);
                UpdateLiveChitPreview();
            }

            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string presetName = IsSmoothStrong ? "Strong" : (IsSmoothMedium ? "Medium" : "Light");
            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Applied {presetName} smooth background to all {count} loaded tiles."
                : $"Applied {presetName} smooth background to {count} selected tiles{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void DespeckleCurrentTile()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;
            if (SelectedChit.WorkingMat.Width < 8 || SelectedChit.WorkingMat.Height < 8) return;

            Vec4b key = ColorToVec4b(TransparentColor);
            var despeckled = ChitEditorService.ApplyDespeckle(SelectedChit.WorkingMat, 25, key);
            SelectedChit.UpdateWorkingMat(despeckled, recordHistory: true, TransparentColor);

            RecordEditHistory(new List<ChitItem> { SelectedChit });
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();

            StatusMessage = $"Applied despeckle to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void DespeckleAllTiles()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Despeckle operation.";
                    return;
                }
            }

            Vec4b key = ColorToVec4b(TransparentColor);
            int count = 0;

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                var despeckled = ChitEditorService.ApplyDespeckle(chit.WorkingMat, 25, key);
                chit.UpdateWorkingMat(despeckled, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            if (SelectedChit != null)
            {
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit.SetPreviewMat(null);
                UpdateLiveChitPreview();
            }

            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Applied despeckle to all {count} loaded tiles."
                : $"Applied despeckle to {count} selected tiles{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void SharpenCurrentTile()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;
            if (SelectedChit.WorkingMat.Width < 8 || SelectedChit.WorkingMat.Height < 8) return;

            Vec4b key = ColorToVec4b(TransparentColor);
            var sharpened = ChitEditorService.ApplySharpen(SelectedChit.WorkingMat, 0.35, 1.0, key);
            SelectedChit.UpdateWorkingMat(sharpened, recordHistory: true, TransparentColor);

            RecordEditHistory(new List<ChitItem> { SelectedChit });
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();

            StatusMessage = $"Applied gentle sharpen to tile #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void SharpenAllTiles()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Sharpen operation.";
                    return;
                }
            }

            Vec4b key = ColorToVec4b(TransparentColor);
            int count = 0;

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 8 || chit.WorkingMat.Height < 8) continue;

                var sharpened = ChitEditorService.ApplySharpen(chit.WorkingMat, 0.35, 1.0, key);
                chit.UpdateWorkingMat(sharpened, recordHistory: true, TransparentColor);
                count++;
            }

            RecordEditHistory(targets);
            if (SelectedChit != null)
            {
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit.SetPreviewMat(null);
                UpdateLiveChitPreview();
            }

            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = targets.Count == LoadedChits.Count
                ? $"Applied gentle sharpen to all {count} loaded tiles."
                : $"Applied gentle sharpen to {count} selected tiles{excludedNote}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void AutoTrimCurrentTile()
        {
            DeactivateCloneStamp();
            if (SelectedChit?.WorkingMat == null || SelectedChit.WorkingMat.IsDisposed) return;
            if (SelectedChit.WorkingMat.Width < 16 || SelectedChit.WorkingMat.Height < 16) return;

            Vec4b key = ColorToVec4b(TransparentColor);
            int oldW = SelectedChit.PixelWidth;
            int oldH = SelectedChit.PixelHeight;

            var trimmed = ChitEditorService.ApplyAutoTrim(SelectedChit.WorkingMat, 15, 1, IsSquareChecked, key);
            if (trimmed.Width == oldW && trimmed.Height == oldH)
            {
                trimmed.Dispose();
                StatusMessage = $"Tile #{SelectedChit.Index} has no detectable uniform border to trim.";
                return;
            }

            SelectedChit.UpdateWorkingMat(trimmed, recordHistory: true, TransparentColor);
            ChitEditorWidth = SelectedChit.PixelWidth;
            ChitEditorHeight = SelectedChit.PixelHeight;

            RecordEditHistory(new List<ChitItem> { SelectedChit });
            _lastCommittedSignature = null;
            _lastCommittedBevelSignature = null;
            _lastCommittedClipSignature = null;
            SelectedChit.SetPreviewMat(null);
            UpdateLiveChitPreview();

            StatusMessage = $"Auto-trimmed tile #{SelectedChit.Index} from {oldW}×{oldH} px to {SelectedChit.PixelWidth}×{SelectedChit.PixelHeight} px.";
        }

        [RelayCommand(CanExecute = nameof(HasChitsLoaded))]
        private void AutoTrimAllTiles()
        {
            DeactivateCloneStamp();
            var targets = LoadedChits.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0)
            {
                if (SelectedChit != null)
                {
                    targets = new List<ChitItem> { SelectedChit };
                }
                else
                {
                    StatusMessage = "No tiles selected for Auto-Trim operation.";
                    return;
                }
            }

            Vec4b key = ColorToVec4b(TransparentColor);
            int trimmedCount = 0;
            var affectedTargets = new List<ChitItem>();

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                if (chit.WorkingMat.Width < 16 || chit.WorkingMat.Height < 16) continue;

                int oldW = chit.PixelWidth;
                int oldH = chit.PixelHeight;
                var trimmed = ChitEditorService.ApplyAutoTrim(chit.WorkingMat, 15, 1, IsSquareChecked, key);
                if (trimmed.Width == oldW && trimmed.Height == oldH)
                {
                    trimmed.Dispose();
                    continue;
                }

                chit.UpdateWorkingMat(trimmed, recordHistory: true, TransparentColor);
                affectedTargets.Add(chit);
                trimmedCount++;
            }

            if (affectedTargets.Count > 0)
            {
                RecordEditHistory(affectedTargets);
            }

            if (SelectedChit != null)
            {
                ChitEditorWidth = SelectedChit.PixelWidth;
                ChitEditorHeight = SelectedChit.PixelHeight;
                _lastCommittedSignature = null;
                _lastCommittedBevelSignature = null;
                _lastCommittedClipSignature = null;
                SelectedChit.SetPreviewMat(null);
                UpdateLiveChitPreview();
            }

            OnPropertyChanged(nameof(CanUndoChit));
            OnPropertyChanged(nameof(CanRedoChit));
            UndoChitCommand.NotifyCanExecuteChanged();
            RedoChitCommand.NotifyCanExecuteChanged();

            string excludedNote = (SelectedChit != null && !SelectedChit.IsSelected && targets.Count > 1)
                ? $" (active tile #{SelectedChit.Index} was excluded)"
                : string.Empty;

            StatusMessage = trimmedCount > 0
                ? $"Auto-trimmed {trimmedCount} of {targets.Count} tiles{excludedNote}."
                : $"No tiles required border trimming{excludedNote}.";
        }

        #endregion

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ExportSelectedChit()
        {
            if (SelectedChit?.WorkingMat == null) return;

            string initialDir = !string.IsNullOrEmpty(LoadedFilePath)
                ? Path.GetDirectoryName(LoadedFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save Tile as PNG",
                Filter = "PNG Image (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = initialDir,
                FileName = SelectedChit.PieceName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    Cv2.ImWrite(saveFileDialog.FileName, SelectedChit.WorkingMat);
                    StatusMessage = $"Saved tile #{SelectedChit.Index} ({SelectedChit.PieceName}) to {saveFileDialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to save tile: {ex.Message}";
                    MessageBox.Show(
                        $"Failed to save tile:\n{ex.Message}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task ExportAllEditedChitsAsync()
        {
            if (LoadedChits.Count == 0) return;

            var folderDialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Edited PNG Tiles",
                InitialDirectory = !string.IsNullOrEmpty(LoadedFilePath) ? Path.GetDirectoryName(LoadedFilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string targetDir = folderDialog.FolderName;

            var chitsToExport = LoadedChits
                .Where(c => c.WorkingMat != null && !c.WorkingMat.IsDisposed)
                .ToList();

            if (chitsToExport.Count == 0)
            {
                StatusMessage = "No valid tiles available to export.";
                return;
            }

            var conflictingFiles = chitsToExport
                .Where(c => File.Exists(Path.Combine(targetDir, c.PieceName)))
                .Select(c => c.PieceName)
                .ToList();

            bool skipExisting = false;
            if (conflictingFiles.Count > 0)
            {
                string sampleNames = string.Join(", ", conflictingFiles.Take(3));
                if (conflictingFiles.Count > 3)
                {
                    sampleNames += $", ... (+{conflictingFiles.Count - 3} more)";
                }

                string promptText = $"{conflictingFiles.Count} of {chitsToExport.Count} tile files already exist in:\n{targetDir}\n\nExisting files:\n{sampleNames}\n\n" +
                                   "• Click 'Yes' to Overwrite existing files.\n" +
                                   "• Click 'No' to Skip existing files (export only new tiles).\n" +
                                   "• Click 'Cancel' to Abort export without saving any files.";

                var dialogResult = MessageBox.Show(
                    promptText,
                    "Confirm Tile Export Overwrite",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (dialogResult == MessageBoxResult.Cancel)
                {
                    StatusMessage = "Tile export cancelled by user. No files were written.";
                    return;
                }

                if (dialogResult == MessageBoxResult.No)
                {
                    skipExisting = true;
                }
            }

            int exportedCount = 0;
            int skippedCount = 0;
            int overwrittenCount = 0;
            var writeErrors = new List<string>();

            await Task.Run(() =>
            {
                foreach (var chit in chitsToExport)
                {
                    if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed)
                    {
                        continue;
                    }

                    string outPath = Path.Combine(targetDir, chit.PieceName);
                    bool fileExisted = File.Exists(outPath);

                    if (fileExisted && skipExisting)
                    {
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        Cv2.ImWrite(outPath, chit.WorkingMat);
                        exportedCount++;
                        if (fileExisted)
                        {
                            overwrittenCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        writeErrors.Add($"{chit.PieceName}: {ex.Message}");
                    }
                }
            });

            if (writeErrors.Count > 0)
            {
                StatusMessage = $"Export finished with errors: {exportedCount} exported, {skippedCount} skipped, {writeErrors.Count} failed.";
                MessageBox.Show(
                    $"Completed tile export with {writeErrors.Count} error(s):\n\n" + string.Join("\n", writeErrors.Take(5)),
                    "Export Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else if (skipExisting)
            {
                StatusMessage = $"Successfully exported {exportedCount} new tiles (skipped {skippedCount} existing files) to: {targetDir}";
            }
            else if (overwrittenCount > 0)
            {
                StatusMessage = $"Successfully exported {exportedCount} tiles (overwrote {overwrittenCount} existing files) to: {targetDir}";
            }
            else
            {
                StatusMessage = $"Successfully exported all {exportedCount} tiles to: {targetDir}";
            }
        }

        [RelayCommand]
        private void SelectNextChit()
        {
            DeactivateCloneStamp();
            if (LoadedChits.Count == 0)
            {
                SelectedChit = null;
                return;
            }
            int idx = SelectedChit != null ? LoadedChits.IndexOf(SelectedChit) : -1;
            int nextIdx = (idx + 1) % LoadedChits.Count;
            SelectedChit = LoadedChits[nextIdx];
            SelectedChit.IsSelected = true;
        }

        [RelayCommand]
        private void SelectPrevChit()
        {
            DeactivateCloneStamp();
            if (LoadedChits.Count == 0)
            {
                SelectedChit = null;
                return;
            }
            int idx = SelectedChit != null ? LoadedChits.IndexOf(SelectedChit) : 0;
            int prevIdx = (idx - 1 + LoadedChits.Count) % LoadedChits.Count;
            SelectedChit = LoadedChits[prevIdx];
            SelectedChit.IsSelected = true;
        }

        [RelayCommand]
        private void SetTransparentColor(string colorHex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                TransparentColor = color;
                StatusMessage = $"Transparent color set to {colorHex}";
            }
            catch
            {
                // Ignore parse errors
            }
        }

        [RelayCommand]
        private void CycleTransparentColor()
        {
            // Cycles between common keying colors: Cyan, Magenta, Green, Black, Transparent White
            if (TransparentColor == Colors.Cyan) TransparentColor = Colors.Magenta;
            else if (TransparentColor == Colors.Magenta) TransparentColor = Color.FromRgb(0, 255, 0); // Lime
            else if (TransparentColor == Color.FromRgb(0, 255, 0)) TransparentColor = Colors.Black;
            else if (TransparentColor == Colors.Black) TransparentColor = Colors.White;
            else TransparentColor = Colors.Cyan;

            StatusMessage = $"Transparent key color set to {TransparentColor}";
        }

        private static Vec4b ColorToVec4b(Color color)
        {
            return new Vec4b(color.B, color.G, color.R, color.A);
        }

        #endregion
    }

    /// <summary>
    /// Represents a recently opened PDF rulebook or counter sheet file.
    /// </summary>
    public class RecentPdfItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string DisplayName => !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath) : string.Empty;
    }

    /// <summary>
    /// Represents a selectable color swatch option for the grid overlay.
    /// </summary>
    public partial class OverlayColorOption : ObservableObject
    {
        public Color Color { get; init; }
        public string Name { get; init; } = string.Empty;

        [ObservableProperty]
        private bool _isSelected;
    }
}
