using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private const string AppBaseTitle = "PDF to Tiles 2026-23-11.10";
        private byte[]? _pdfBytes;

        // Native OpenCV Mat references for Slicer workspace
        private Mat? _masterMat;
        private Mat? _deskewedMat;
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
        [NotifyCanExecuteChangedFor(nameof(OpenPdfCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private bool _isLoading = false;

        #endregion

        #region Phase 1: PDF Viewer Properties

        [ObservableProperty]
        private string? _loadedFilePath;

        [ObservableProperty]
        private string? _loadedFileName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendPageToSlicerCommand))]
        private int _totalPages = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
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
        private string _selectedSideTag = "Front";

        public IReadOnlyList<int> AvailableDpis { get; } = new List<int> { 300, 450, 600 };
        public IReadOnlyList<string> AvailableSideTags { get; } = new List<string> { "Front", "Back", "Page" };

        public string ZoomPercentageText => $"{Math.Round(ZoomScale * 100)}%";
        public bool HasLoadedPdf => _pdfBytes != null && _pdfBytes.Length > 0 && TotalPages > 0;

        #endregion

        #region Phase 2 & 2.5: Deskew & Grid Slicer Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSlicerImage))]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectTiltCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceGridCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceAllBandsCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceActiveBandCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendBandToChitEditorCommand))]
        private BitmapSource? _slicerImage;

        [ObservableProperty]
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

        // Column Divider Fine-Tuning
        [ObservableProperty]
        private bool _isFineTuningDividers = false;

        // Interactive Corner Picking
        [ObservableProperty]
        private bool _isSettingUpperLeftCorner = false;

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
        private int _previewChitNumber = 1; // 1 to 20

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        private int _previewRowIndex = 0; // 0 = Row 1, 1 = Row 2

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewCellText))]
        private int _previewColIndex = 0; // 0 = Col 1 ... 9 = Col 10

        public IReadOnlyList<int> AvailableChitNumbers { get; } = Enumerable.Range(1, 20).ToList();
        public IReadOnlyList<int> AvailablePreviewRows { get; } = new List<int> { 1, 2 };
        public IReadOnlyList<int> AvailablePreviewCols { get; } = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        public int SelectedPreviewRowDisplay
        {
            get => PreviewRowIndex + 1;
            set
            {
                if (value >= 1 && value <= 2)
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
                if (value >= 1 && value <= 10)
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
                    int row = ((PreviewChitNumber - 1) / 10) + 1;
                    int col = ((PreviewChitNumber - 1) % 10) + 1;
                    return $"Chit {PreviewChitNumber} of 20 — Row {row}, Col {col}";
                }
                return "Cell (Row 1, Col 1) with red cut boundary";
            }
        }

        [ObservableProperty]
        private double _inwardInsetPx = 4.0;

        [ObservableProperty]
        private string _selectedSide = "Front";

        [ObservableProperty]
        private BackOrientationMode _selectedOrientation = BackOrientationMode.HorizontalFlip;

        [ObservableProperty]
        private string _namingPrefix = "WorldKiller";

        public IReadOnlyList<string> SlicerAvailableSides { get; } = new List<string> { "Front", "Back" };
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
        }

        #region PDF Viewer Handlers

        partial void OnCurrentPageNumberChanged(int value)
        {
            if (HasLoadedPdf && value >= 1 && value <= TotalPages)
            {
                _ = LoadPageAsync(value);
            }
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

            string selectedPath = openFileDialog.FileName;
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

                CurrentPageNumber = 1;
                await LoadPageAsync(1);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open PDF: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasLoadedPdf));
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
        private void PreviousPage()
        {
            if (CurrentPageNumber > 1)
            {
                CurrentPageNumber--;
            }
        }

        private bool CanGoPrevious() => !IsLoading && HasLoadedPdf && CurrentPageNumber > 1;

        [RelayCommand(CanExecute = nameof(CanGoNext))]
        private void NextPage()
        {
            if (CurrentPageNumber < TotalPages)
            {
                CurrentPageNumber++;
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
            IsFitToPage = false;
            ZoomScale = 1.0;
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
                DefaultExt = ".png"
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

        #endregion

        #region Phase 2 & 2.5: Slicer & Deskew Handlers

        [RelayCommand(CanExecute = nameof(CanCapturePage))]
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

                    _masterMat = Cv2.ImRead(filePath, ImreadModes.Color);
                    if (_masterMat == null || _masterMat.Empty())
                    {
                        throw new InvalidOperationException("Failed to read image buffer.");
                    }

                    if (isTempFile)
                    {
                        try { File.Delete(filePath); } catch { /* ignore temp cleanup */ }
                    }
                });

                SlicerSourceFileName = Path.GetFileName(filePath);
                SlicerPixelWidth = _masterMat!.Width;
                SlicerPixelHeight = _masterMat!.Height;

                DeskewAngle = 0.0;
                await ApplyDeskewRotationAsync();

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
            }
        }

        public Band2x10Definition? GetActiveBand()
        {
            if (SelectedProfile == null) return null;
            var bands = SelectedSide.Equals("Back", StringComparison.OrdinalIgnoreCase)
                ? SelectedProfile.BackBands
                : SelectedProfile.FrontBands;

            if (bands == null || bands.Count == 0) return null;
            int idx = Math.Clamp(ActiveBandIndex, 0, bands.Count - 1);
            return bands[idx];
        }

        private void SyncFromActiveBand()
        {
            var bands = SelectedSide.Equals("Back", StringComparison.OrdinalIgnoreCase)
                ? SelectedProfile?.BackBands
                : SelectedProfile?.FrontBands;

            TotalBands = bands != null && bands.Count > 0 ? bands.Count : 5;
            if (ActiveBandIndex >= TotalBands)
            {
                ActiveBandIndex = Math.Max(0, TotalBands - 1);
            }

            var band = GetActiveBand();
            if (band == null) return;

            _isSyncingBand = true;
            try
            {
                ActiveBandName = band.Name;
                ActiveBandLeftPercent = Math.Round(band.Bounds.Left * 100, 2);
                ActiveBandTopPercent = Math.Round(band.Bounds.Top * 100, 2);
                ActiveBandWidthPercent = Math.Round(band.Bounds.Width * 100, 2);
                ActiveBandHeightPercent = Math.Round(band.Bounds.Height * 100, 2);
                ActiveBandRowSplitPercent = Math.Round(band.RowSplitRatio * 100, 1);
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

            band.Bounds.Left = Math.Clamp(ActiveBandLeftPercent / 100.0, 0.0, 0.99);
            band.Bounds.Top = Math.Clamp(ActiveBandTopPercent / 100.0, 0.0, 0.99);
            band.Bounds.Width = Math.Clamp(ActiveBandWidthPercent / 100.0, 0.01, 1.0 - band.Bounds.Left);
            band.Bounds.Height = Math.Clamp(ActiveBandHeightPercent / 100.0, 0.01, 1.0 - band.Bounds.Top);
            band.RowSplitRatio = Math.Clamp(ActiveBandRowSplitPercent / 100.0, 0.1, 0.9);

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
                NamingPrefix = value.NamingPrefix;

                var bounds = SelectedSide.Equals("Back", StringComparison.OrdinalIgnoreCase) ? value.BackBounds : value.FrontBounds;
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
                var bounds = value.Equals("Back", StringComparison.OrdinalIgnoreCase)
                    ? SelectedProfile.BackBounds
                    : SelectedProfile.FrontBounds;

                GridLeftPercent = Math.Round(bounds.Left * 100, 2);
                GridTopPercent = Math.Round(bounds.Top * 100, 2);
                GridWidthPercent = Math.Round(bounds.Width * 100, 2);
                GridHeightPercent = Math.Round(bounds.Height * 100, 2);

                SyncFromActiveBand();
            }
        }

        partial void OnDeskewAngleChanged(double value)
        {
            _ = ApplyDeskewRotationAsync();
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            if (value != 1 && IsSettingUpperLeftCorner)
            {
                IsSettingUpperLeftCorner = false;
            }
        }

        partial void OnIsSettingUpperLeftCornerChanged(bool value)
        {
            if (value)
            {
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

        partial void OnActiveBandIndexChanged(int value) => SyncFromActiveBand();
        partial void OnActiveBandLeftPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandTopPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandWidthPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandHeightPercentChanged(double value) => SyncToActiveBand();
        partial void OnActiveBandRowSplitPercentChanged(double value) => SyncToActiveBand();

        partial void OnSelectedDividerIndexChanged(int value) => OnPropertyChanged(nameof(SelectedDividerPositionText));
        partial void OnPreviewRowIndexChanged(int value) => UpdateChitPreview();
        partial void OnPreviewColIndexChanged(int value) => UpdateChitPreview();

        partial void OnPreviewChitNumberChanged(int value)
        {
            int clamped = Math.Clamp(value, 1, 20);
            if (clamped != value)
            {
                PreviewChitNumber = clamped;
                return;
            }

            _previewRowIndex = (clamped - 1) / 10;
            _previewColIndex = (clamped - 1) % 10;
            OnPropertyChanged(nameof(PreviewRowIndex));
            OnPropertyChanged(nameof(PreviewColIndex));
            OnPropertyChanged(nameof(PreviewCellText));
            UpdateChitPreview();
        }

        [RelayCommand]
        private void PreviousChit()
        {
            if (PreviewChitNumber > 1)
            {
                PreviewChitNumber--;
            }
            else
            {
                PreviewChitNumber = 20; // wrap-around to 20
            }
        }

        [RelayCommand]
        private void NextChit()
        {
            if (PreviewChitNumber < 20)
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
                var targetBounds = SelectedSide.Equals("Back", StringComparison.OrdinalIgnoreCase)
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
            ActiveBandLeftPercent = Math.Clamp(Math.Round(ActiveBandLeftPercent - 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeActiveBandLeftPositive()
        {
            ActiveBandLeftPercent = Math.Clamp(Math.Round(ActiveBandLeftPercent + 0.1, 1), 0.0, 50.0);
        }

        [RelayCommand]
        private void NudgeActiveBandTopUp()
        {
            // Up arrow moves band UP -> Top decreases
            ActiveBandTopPercent = Math.Clamp(Math.Round(ActiveBandTopPercent - 0.1, 1), 0.0, 90.0);
        }

        [RelayCommand]
        private void NudgeActiveBandTopDown()
        {
            // Down arrow moves band DOWN -> Top increases
            ActiveBandTopPercent = Math.Clamp(Math.Round(ActiveBandTopPercent + 0.1, 1), 0.0, 90.0);
        }

        [RelayCommand]
        private void NudgeActiveBandWidthLeft()
        {
            // Left arrow moves right boundary LEFT -> Width decreases
            ActiveBandWidthPercent = Math.Clamp(Math.Round(ActiveBandWidthPercent - 0.1, 1), 50.0, 100.0);
        }

        [RelayCommand]
        private void NudgeActiveBandWidthRight()
        {
            // Right arrow moves right boundary RIGHT -> Width increases
            ActiveBandWidthPercent = Math.Clamp(Math.Round(ActiveBandWidthPercent + 0.1, 1), 50.0, 100.0);
        }

        [RelayCommand]
        private void NudgeActiveBandHeightUp()
        {
            // Up arrow moves bottom boundary UP -> Height decreases
            ActiveBandHeightPercent = Math.Clamp(Math.Round(ActiveBandHeightPercent - 0.1, 1), 5.0, 35.0);
        }

        [RelayCommand]
        private void NudgeActiveBandHeightDown()
        {
            // Down arrow moves bottom boundary DOWN -> Height increases
            ActiveBandHeightPercent = Math.Clamp(Math.Round(ActiveBandHeightPercent + 0.1, 1), 5.0, 35.0);
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
                        _deskewedMat, SelectedProfile, SelectedSide, ActiveBandIndex, PreviewRowIndex, PreviewColIndex);
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

            int pieceCount = IsBandedMode ? 20 : (SelectedProfile.Rows * SelectedProfile.Columns);

            IsSlicing = true;
            SliceProgressPercent = 0;
            SliceProgressText = $"Preparing to slice {pieceCount} pieces...";
            StatusMessage = IsBandedMode ? $"Slicing 2×10 band into 20 PNG pieces..." : $"Slicing grid into {pieceCount} PNG pieces...";

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
                string currentSide = SelectedSide;

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
                        bandIndex
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
                        _sliceCts.Token
                    );
                }

                StatusMessage = $"Successfully exported all {pieceCount} pieces to: {targetDir}";
                SliceProgressText = $"Completed! {pieceCount} pieces saved to {targetDir}";
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
            SliceProgressText = "Slicing 2×10 band into memory for Chit Editor...";
            StatusMessage = "Slicing 2×10 band into memory for Chit Editor...";

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
                string currentSide = SelectedSide;
                int bandIdx = ActiveBandIndex;

                var chits = await GridSlicerService.SliceBandedGridToMemoryAsync(
                    _deskewedMat,
                    profileCopy,
                    currentSide,
                    bandIdx,
                    progress,
                    _sliceCts.Token
                );

                // Dispose existing loaded chits to prevent memory leaks
                foreach (var oldChit in LoadedChits)
                {
                    oldChit.Dispose();
                }
                LoadedChits.Clear();

                foreach (var chit in chits)
                {
                    LoadedChits.Add(chit);
                }

                if (LoadedChits.Count > 0)
                {
                    SelectedChit = LoadedChits[0];
                    SelectedChit.IsSelected = true;
                }

                SelectedTabIndex = 2; // Switch directly to Tab 3: Chit Editor
                StatusMessage = $"Loaded {LoadedChits.Count} chits into Chit Editor.";
                SliceProgressText = $"Loaded {LoadedChits.Count} chits into Chit Editor.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "In-memory slicing cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading chits: {ex.Message}";
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
        [NotifyCanExecuteChangedFor(nameof(ApplyBevelCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyClipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyClearCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyStretchCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyResizeCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyEdgeCleanCommand))]
        [NotifyCanExecuteChangedFor(nameof(AutoSampleEdgeColorCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetSelectedChitCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportSelectedChitCommand))]
        private ChitItem? _selectedChit;

        partial void OnSelectedChitChanging(ChitItem? oldValue, ChitItem? newValue)
        {
            oldValue?.SetPreviewMat(null);
        }

        partial void OnSelectedChitChanged(ChitItem? value)
        {
            if (value != null)
            {
                foreach (var chit in LoadedChits)
                {
                    chit.IsSelected = ReferenceEquals(chit, value);
                }

                ChitEditorWidth = value.PixelWidth;
                ChitEditorHeight = value.PixelHeight;
                ClampCurrentEdgeCleanDepths();

                if (!LockEdgeCleanColor && value.WorkingMat != null && !value.WorkingMat.IsDisposed)
                {
                    AutoSampleEdgeColorInternal(value);
                }

                _lastCommittedSignature = null;
                UpdateLiveEdgeCleanPreview();
            }
        }

        public bool HasSelectedChit => SelectedChit != null;

        [ObservableProperty]
        private int _chitEditorWidth = 50;

        [ObservableProperty]
        private int _chitEditorHeight = 50;

        [ObservableProperty]
        private int _chitEditorBevelSize = 4;

        [ObservableProperty]
        private int _chitEditorClipSize = 2;

        [ObservableProperty]
        private int _chitEditorClearSize = 1;

        [ObservableProperty]
        private Color _transparentColor = Colors.Cyan;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitEditorZoomPercentageText))]
        private double _chitEditorZoomScale = 1.0;

        public string ChitEditorZoomPercentageText => $"{Math.Round(ChitEditorZoomScale * 100):F0}%";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChitInspectionZoomPercentageText))]
        private double _chitInspectionZoomScale = 2.0;

        public string ChitInspectionZoomPercentageText => $"{Math.Round(ChitInspectionZoomScale * 100):F0}%";

        [ObservableProperty]
        private bool _applyToAllChits = false;

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

        partial void OnChitEdgeCleanTopDepthChanged(int value) => UpdateLiveEdgeCleanPreview();
        partial void OnChitEdgeCleanBottomDepthChanged(int value) => UpdateLiveEdgeCleanPreview();
        partial void OnChitEdgeCleanLeftDepthChanged(int value) => UpdateLiveEdgeCleanPreview();
        partial void OnChitEdgeCleanRightDepthChanged(int value) => UpdateLiveEdgeCleanPreview();
        partial void OnChitEdgeCleanToleranceChanged(int value) => UpdateLiveEdgeCleanPreview();
        partial void OnSelectedEdgeCleanModeChanged(string value) => UpdateLiveEdgeCleanPreview();
        partial void OnChitEdgeCleanColorChanged(Color value) => UpdateLiveEdgeCleanPreview();
        partial void OnPreserveTransparentCornersChanged(bool value) => UpdateLiveEdgeCleanPreview();

        private string? _lastCommittedSignature;

        private string GetCurrentEdgeCleanSignature()
        {
            return $"{ChitEdgeCleanTopDepth}_{ChitEdgeCleanBottomDepth}_{ChitEdgeCleanLeftDepth}_{ChitEdgeCleanRightDepth}_{ChitEdgeCleanTolerance}_{SelectedEdgeCleanMode}_{ChitEdgeCleanColor.R}_{ChitEdgeCleanColor.G}_{ChitEdgeCleanColor.B}_{PreserveTransparentCorners}";
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
                using Mat previewMat = ChitEditorService.ApplyEdgeCleanup(
                    SelectedChit.WorkingMat,
                    top,
                    bottom,
                    left,
                    right,
                    ChitEdgeCleanTolerance,
                    ChitEdgeCleanMode,
                    targetBg,
                    PreserveTransparentCorners);

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

        [RelayCommand]
        private void ChitEditorZoomReset()
        {
            ChitEditorZoomScale = 1.0;
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

        private bool CanEditChit() => SelectedChit?.WorkingMat != null && !SelectedChit.WorkingMat.IsDisposed;

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void UndoChit()
        {
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            SelectedChit.Undo();
            UpdateLiveEdgeCleanPreview();
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void RedoChit()
        {
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            SelectedChit.Redo();
            UpdateLiveEdgeCleanPreview();
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyBevel()
        {
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var beveled = ChitEditorService.ApplyBevel(chit.WorkingMat, ChitEditorBevelSize);
                chit.UpdateWorkingMat(beveled, recordHistory: true, TransparentColor);
            }
            _lastCommittedSignature = null;
            UpdateLiveEdgeCleanPreview();
            StatusMessage = ApplyToAllChits ? $"Applied {ChitEditorBevelSize}px bevel to all {targets.Count} chits." : $"Applied {ChitEditorBevelSize}px bevel to chit #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyClip()
        {
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };
            Vec4b key = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var clipped = ChitEditorService.ApplyCornerClip(chit.WorkingMat, ChitEditorClipSize, key);
                chit.UpdateWorkingMat(clipped, recordHistory: true, TransparentColor);
            }
            _lastCommittedSignature = null;
            UpdateLiveEdgeCleanPreview();
            StatusMessage = ApplyToAllChits ? $"Applied {ChitEditorClipSize}px corner clip to all {targets.Count} chits." : $"Applied {ChitEditorClipSize}px corner clip to chit #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyClear()
        {
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };
            Vec4b key = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var cleared = ChitEditorService.ApplyClearBorder(chit.WorkingMat, ChitEditorClearSize, key);
                chit.UpdateWorkingMat(cleared, recordHistory: true, TransparentColor);
            }
            _lastCommittedSignature = null;
            UpdateLiveEdgeCleanPreview();
            StatusMessage = ApplyToAllChits ? $"Cleared {ChitEditorClearSize}px outer border on all {targets.Count} chits." : $"Cleared {ChitEditorClearSize}px outer border on chit #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyStretch()
        {
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var stretched = ChitEditorService.ApplyStretch(chit.WorkingMat, ChitEditorWidth, ChitEditorHeight);
                chit.UpdateWorkingMat(stretched, recordHistory: true, TransparentColor);
            }
            _lastCommittedSignature = null;
            ClampCurrentEdgeCleanDepths();
            UpdateLiveEdgeCleanPreview();
            StatusMessage = ApplyToAllChits ? $"Stretched all {targets.Count} chits to {ChitEditorWidth}×{ChitEditorHeight} px." : $"Stretched chit #{SelectedChit.Index} to {ChitEditorWidth}×{ChitEditorHeight} px.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyResize()
        {
            if (SelectedChit?.WorkingMat == null) return;
            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };
            Vec4b key = ColorToVec4b(TransparentColor);

            foreach (var chit in targets)
            {
                if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;
                var resized = ChitEditorService.ApplyResize(chit.WorkingMat, ChitEditorWidth, ChitEditorHeight, key);
                chit.UpdateWorkingMat(resized, recordHistory: true, TransparentColor);
            }
            _lastCommittedSignature = null;
            ClampCurrentEdgeCleanDepths();
            UpdateLiveEdgeCleanPreview();
            StatusMessage = ApplyToAllChits ? $"Resized canvas for all {targets.Count} chits to {ChitEditorWidth}×{ChitEditorHeight} px." : $"Resized canvas for chit #{SelectedChit.Index} to {ChitEditorWidth}×{ChitEditorHeight} px.";
        }

        private void AutoSampleEdgeColorInternal(ChitItem chit)
        {
            if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) return;
            Vec4b dominant = ChitEditorService.SampleDominantBackgroundColor(
                chit.WorkingMat,
                ChitEdgeCleanTopDepth,
                ChitEdgeCleanBottomDepth,
                ChitEdgeCleanLeftDepth,
                ChitEdgeCleanRightDepth);
            ChitEdgeCleanColor = Color.FromRgb(dominant.Item2, dominant.Item1, dominant.Item0);
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void AutoSampleEdgeColor()
        {
            if (SelectedChit?.WorkingMat == null) return;
            AutoSampleEdgeColorInternal(SelectedChit);
            StatusMessage = $"Auto-sampled background color #{ChitEdgeCleanColor.R:X2}{ChitEdgeCleanColor.G:X2}{ChitEdgeCleanColor.B:X2} from chit #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ApplyEdgeClean()
        {
            if (SelectedChit?.WorkingMat == null) return;

            var targets = ApplyToAllChits ? LoadedChits.ToList() : new List<ChitItem> { SelectedChit };

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
                        ChitEdgeCleanRightDepth);

                var cleaned = ChitEditorService.ApplyEdgeCleanup(
                    chit.WorkingMat,
                    ChitEdgeCleanTopDepth,
                    ChitEdgeCleanBottomDepth,
                    ChitEdgeCleanLeftDepth,
                    ChitEdgeCleanRightDepth,
                    ChitEdgeCleanTolerance,
                    ChitEdgeCleanMode,
                    targetBg,
                    PreserveTransparentCorners);

                chit.UpdateWorkingMat(cleaned, recordHistory: true, TransparentColor);
            }

            // Mark current settings as committed into WorkingMat to avoid double-application
            _lastCommittedSignature = GetCurrentEdgeCleanSignature();

            // Refresh preview display so it points to the newly committed WorkingMat without double-applying
            SelectedChit.SetPreviewMat(null);

            StatusMessage = ApplyToAllChits
                ? $"Applied {SelectedEdgeCleanMode} edge clean (T:{ChitEdgeCleanTopDepth} B:{ChitEdgeCleanBottomDepth} L:{ChitEdgeCleanLeftDepth} R:{ChitEdgeCleanRightDepth} px) to all {targets.Count} chits."
                : $"Applied {SelectedEdgeCleanMode} edge clean (T:{ChitEdgeCleanTopDepth} B:{ChitEdgeCleanBottomDepth} L:{ChitEdgeCleanLeftDepth} R:{ChitEdgeCleanRightDepth} px) to chit #{SelectedChit.Index}.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ResetSelectedChit()
        {
            if (SelectedChit == null) return;
            _lastCommittedSignature = null;
            SelectedChit.ResetToOriginal();
            UpdateLiveEdgeCleanPreview();
            StatusMessage = $"Reset chit #{SelectedChit.Index} to original slice.";
        }

        [RelayCommand]
        private void ResetAllChits()
        {
            _lastCommittedSignature = null;
            foreach (var chit in LoadedChits)
            {
                chit.ResetToOriginal();
            }
            UpdateLiveEdgeCleanPreview();
            StatusMessage = $"Reset all {LoadedChits.Count} chits to original slices.";
        }

        [RelayCommand(CanExecute = nameof(CanEditChit))]
        private void ExportSelectedChit()
        {
            if (SelectedChit?.WorkingMat == null) return;

            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save Chit as PNG",
                Filter = "PNG Image (*.png)|*.png",
                FileName = SelectedChit.PieceName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                Cv2.ImWrite(saveFileDialog.FileName, SelectedChit.WorkingMat);
                StatusMessage = $"Saved chit #{SelectedChit.Index} to {saveFileDialog.FileName}";
            }
        }

        [RelayCommand]
        private async Task ExportAllEditedChitsAsync()
        {
            if (LoadedChits.Count == 0) return;

            var folderDialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Edited PNG Chits",
                InitialDirectory = !string.IsNullOrEmpty(LoadedFilePath) ? Path.GetDirectoryName(LoadedFilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string targetDir = folderDialog.FolderName;
            int total = LoadedChits.Count;
            int count = 0;

            await Task.Run(() =>
            {
                foreach (var chit in LoadedChits)
                {
                    if (chit.WorkingMat != null && !chit.WorkingMat.IsDisposed)
                    {
                        string outPath = Path.Combine(targetDir, chit.PieceName);
                        Cv2.ImWrite(outPath, chit.WorkingMat);
                        count++;
                    }
                }
            });

            StatusMessage = $"Successfully exported all {count} edited chits to: {targetDir}";
        }

        [RelayCommand]
        private void SelectNextChit()
        {
            if (LoadedChits.Count == 0) return;
            int idx = SelectedChit != null ? LoadedChits.IndexOf(SelectedChit) : -1;
            int nextIdx = (idx + 1) % LoadedChits.Count;
            SelectedChit = LoadedChits[nextIdx];
        }

        [RelayCommand]
        private void SelectPrevChit()
        {
            if (LoadedChits.Count == 0) return;
            int idx = SelectedChit != null ? LoadedChits.IndexOf(SelectedChit) : 0;
            int prevIdx = (idx - 1 + LoadedChits.Count) % LoadedChits.Count;
            SelectedChit = LoadedChits[prevIdx];
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
}
