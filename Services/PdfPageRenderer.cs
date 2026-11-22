using System;
using System.IO;
using System.Windows.Media.Imaging;
using PDFtoImage;
using SkiaSharp;

namespace pdf_to_Tiles.Services
{
    /// <summary>
    /// Handles non-locking PDF loading, low-DPI UI preview rendering, and lossless high-DPI page capture.
    /// </summary>
    public static class PdfPageRenderer
    {
        /// <summary>
        /// Reads all PDF bytes into memory using FileShare.ReadWrite so the file is never locked on disk.
        /// </summary>
        public static byte[] LoadPdfBytesNonLocking(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF file not found.", filePath);
            }

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Retrieves the total number of pages in the given PDF byte array.
        /// </summary>
        public static int GetPageCount(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                return 0;
            }

            return Conversion.GetPageCount(pdfBytes);
        }

        /// <summary>
        /// Renders a single page to a frozen WPF BitmapSource at the specified display DPI for UI preview.
        /// Wraps all SkiaSharp native bitmaps, images, and data streams in deterministic using blocks.
        /// </summary>
        public static BitmapSource RenderPagePreview(byte[] pdfBytes, int pageIndex, int dpi = 150)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                throw new ArgumentException("PDF data is empty.", nameof(pdfBytes));
            }

            var renderOptions = new RenderOptions
            {
                Dpi = dpi,
                WithAnnotations = true,
                WithFormFill = true
            };

            // Index is 0-based in PDFtoImage
            using var skBitmap = Conversion.ToImage(pdfBytes, page: pageIndex, options: renderOptions);
            if (skBitmap == null || skBitmap.Width <= 0 || skBitmap.Height <= 0)
            {
                throw new InvalidOperationException($"Failed to render preview for page {pageIndex + 1}.");
            }

            using var skImage = SKImage.FromBitmap(skBitmap);
            using var skData = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var imageStream = skData.AsStream();

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = imageStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // Makes BitmapSource thread-safe for WPF UI binding

            return bitmapImage;
        }

        /// <summary>
        /// Renders the specified page at high DPI (e.g., 300, 450, 600 DPI) directly to a lossless PNG file.
        /// Uses PDFtoImage tiling for ultra-high resolutions to prevent memory exhaustion and corrupt buffers.
        /// </summary>
        public static void ExportPageToPng(byte[] pdfBytes, int pageIndex, int dpi, string outputPath)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                throw new ArgumentException("PDF data is empty.", nameof(pdfBytes));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var renderOptions = new RenderOptions
            {
                Dpi = dpi,
                UseTiling = dpi >= 450, // Enable segment tiling for 450-600+ DPI to safeguard memory
                WithAnnotations = true,
                WithFormFill = true
            };

            Conversion.SavePng(outputPath, pdfBytes, page: pageIndex, options: renderOptions);
        }
    }
}
