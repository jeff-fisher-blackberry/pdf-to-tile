using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private const string AppBaseTitle = "PDF to Tiles 2026-22-11.2";
        private byte[]? _pdfBytes;

        // Native OpenCV Mat references for Slicer workspace
        private Mat? _masterMat;
        private Mat? _deskewedMat;
        private CancellationTokenSource? _sliceCts;

        public Func<(double Width, double Height)>? GetPdfViewportSize { get; set; }
        public Func<(double Width, double Height)>? GetSlicerViewportSize { get; set; }

        #region App & Mode Properties

        [ObservableProperty]
        private string _windowTitle = AppBaseTitle;

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = PDF Viewer, 1 = Deskew & Grid Slicer

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

        #region Phase 2: Deskew & Grid Slicer Properties

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSlicerImage))]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectTiltCommand))]
        [NotifyCanExecuteChangedFor(nameof(SliceGridCommand))]
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
        private bool _isSlicerFitToPage = true;

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
        private SheetProfile? _selectedProfile;

        [ObservableProperty]
        private int _profileRows = 10;

        [ObservableProperty]
        private int _profileColumns = 10;

        // Relative Grid bounds (0 to 100%)
        [ObservableProperty]
        private double _gridLeftPercent = 5.0;

        [ObservableProperty]
        private double _gridTopPercent = 8.0;

        [ObservableProperty]
        private double _gridWidthPercent = 90.0;

        [ObservableProperty]
        private double _gridHeightPercent = 84.0;

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
            ZoomScale = Math.Min(Math.Round(ZoomScale * 1.25, 4), 10.0);
        }

        [RelayCommand]
        private void ZoomOut()
        {
            IsFitToPage = false;
            ZoomScale = Math.Max(Math.Round(ZoomScale / 1.25, 4), 0.05);
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

        #region Phase 2: Slicer & Deskew Handlers

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

                // Render high-DPI image in memory
                string tempPngPath = Path.Combine(Path.GetTempPath(), $"pdf_to_tiles_p{CurrentPageNumber}_{Guid.NewGuid():N}.png");
                await Task.Run(() => PdfPageRenderer.ExportPageToPng(_pdfBytes, pageIndex, dpi, tempPngPath));

                // Load into Mat
                await LoadImageFileForSlicingAsync(tempPngPath, isTempFile: true);

                // Switch to Deskew & Slicer tab
                SelectedTabIndex = 1;
                SelectedSide = SelectedSideTag.Equals("Back", StringComparison.OrdinalIgnoreCase) ? "Back" : "Front";
                StatusMessage = $"Page {CurrentPageNumber} transferred to Slicer ({SlicerPixelWidth} × {SlicerPixelHeight} px @ {dpi} DPI).";
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
                Title = "Open Counter Sheet Image for Slicing",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            await LoadImageFileForSlicingAsync(openFileDialog.FileName, isTempFile: false);
        }

        private async Task LoadImageFileForSlicingAsync(string filePath, bool isTempFile)
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

                if (IsSlicerFitToPage && GetSlicerViewportSize != null)
                {
                    var (width, height) = GetSlicerViewportSize();
                    RecalculateSlicerFitToPage(width, height);
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

                UpdateChitPreview();
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

                UpdateChitPreview();
            }
        }

        partial void OnDeskewAngleChanged(double value)
        {
            _ = ApplyDeskewRotationAsync();
        }

        partial void OnGridLeftPercentChanged(double value) => SyncProfileBoundsAndPreview();
        partial void OnGridTopPercentChanged(double value) => SyncProfileBoundsAndPreview();
        partial void OnGridWidthPercentChanged(double value) => SyncProfileBoundsAndPreview();
        partial void OnGridHeightPercentChanged(double value) => SyncProfileBoundsAndPreview();
        partial void OnInwardInsetPxChanged(double value) => SyncProfileBoundsAndPreview();
        partial void OnProfileRowsChanged(int value) => SyncProfileBoundsAndPreview();
        partial void OnProfileColumnsChanged(int value) => SyncProfileBoundsAndPreview();

        private void SyncProfileBoundsAndPreview()
        {
            if (SelectedProfile != null)
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
                using var previewMat = GridSlicerService.ExtractCellPreview(_deskewedMat, SelectedProfile, SelectedSide, 0, 0);
                ChitPreviewImage = DeskewService.MatToBitmapSource(previewMat);
            }
            catch
            {
                ChitPreviewImage = null;
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
        private void SlicerFitToPage()
        {
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
            IsSlicerFitToPage = false;
            SlicerZoomScale = 1.0;
        }

        [RelayCommand]
        private void SlicerZoomIn()
        {
            IsSlicerFitToPage = false;
            SlicerZoomScale = Math.Min(Math.Round(SlicerZoomScale * 1.25, 4), 10.0);
        }

        [RelayCommand]
        private void SlicerZoomOut()
        {
            IsSlicerFitToPage = false;
            SlicerZoomScale = Math.Max(Math.Round(SlicerZoomScale / 1.25, 4), 0.05);
        }

        [RelayCommand(CanExecute = nameof(CanSliceGrid))]
        private async Task SliceGridAsync()
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
            int pieceCount = SelectedProfile.Rows * SelectedProfile.Columns;

            IsSlicing = true;
            SliceProgressPercent = 0;
            SliceProgressText = $"Preparing to slice {pieceCount} pieces...";
            StatusMessage = $"Slicing grid into {pieceCount} individual PNG pieces...";

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

                await GridSlicerService.SliceGridAsync(
                    _deskewedMat,
                    profileCopy,
                    currentSide,
                    targetDir,
                    progress,
                    _sliceCts.Token
                );

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

        #endregion
    }
}
