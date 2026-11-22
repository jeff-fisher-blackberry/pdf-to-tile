using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using pdf_to_Tiles.Services;

namespace pdf_to_Tiles.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string AppBaseTitle = "PDF to Tiles 2026-22-11.1";
        private byte[]? _pdfBytes;

        public Func<(double Width, double Height)>? GetViewportSize { get; set; }

        [ObservableProperty]
        private string _windowTitle = AppBaseTitle;

        [ObservableProperty]
        private string? _loadedFilePath;

        [ObservableProperty]
        private string? _loadedFileName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        private int _totalPages = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
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

        [ObservableProperty]
        private string _statusMessage = "Ready. Click 'Open PDF' to select a rulebook or counter sheet.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenPdfCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        [NotifyCanExecuteChangedFor(nameof(CapturePageCommand))]
        private bool _isLoading = false;

        public IReadOnlyList<int> AvailableDpis { get; } = new List<int> { 300, 450, 600 };
        public IReadOnlyList<string> AvailableSideTags { get; } = new List<string> { "Front", "Back", "Page" };

        public string ZoomPercentageText => $"{Math.Round(ZoomScale * 100)}%";

        public bool HasLoadedPdf => _pdfBytes != null && _pdfBytes.Length > 0 && TotalPages > 0;

        public MainViewModel()
        {
        }

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

                // Load initial page (Page 1)
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

                if (IsFitToPage && GetViewportSize != null)
                {
                    var (width, height) = GetViewportSize();
                    RecalculateFitToPage(width, height);
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

        public void RecalculateFitToPage(double viewportWidth, double viewportHeight)
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
            if (GetViewportSize != null)
            {
                var (width, height) = GetViewportSize();
                RecalculateFitToPage(width, height);
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
    }
}
