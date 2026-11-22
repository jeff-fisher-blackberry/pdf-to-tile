using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using pdf_to_Tiles.Models;

namespace pdf_to_Tiles.Services
{
    public class SliceProgressReport
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentPieceName { get; set; } = string.Empty;
        public int Percent => TotalCount > 0 ? (int)Math.Round((CurrentIndex / (double)TotalCount) * 100) : 0;
        public string StatusText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Executes grid slicing into individual PNG counter images using zero-copy OpenCvSharp ROI sub-matrices.
    /// Supports discrete 2x10 bands (with total-width anchoring & variable column dividers) as well as uniform grids.
    /// </summary>
    public static class GridSlicerService
    {
        /// <summary>
        /// Slices a deskewed counter sheet Mat using discrete 2x10 bands asynchronously.
        /// If singleBandIndex is provided, slices only that specific band (20 pieces); otherwise slices all bands (e.g. 100 pieces).
        /// </summary>
        public static Task SliceBandedGridAsync(
            Mat masterMat,
            SheetProfile profile,
            string side,
            string outputDirectory,
            IProgress<SliceProgressReport>? progress,
            CancellationToken cancellationToken,
            int? singleBandIndex = null)
        {
            return Task.Run(() =>
            {
                if (masterMat == null || masterMat.IsDisposed || masterMat.Width <= 0 || masterMat.Height <= 0)
                {
                    throw new ArgumentException("Invalid master image Mat.", nameof(masterMat));
                }

                if (profile == null)
                {
                    throw new ArgumentNullException(nameof(profile));
                }

                var bands = side.Equals("Back", StringComparison.OrdinalIgnoreCase)
                    ? profile.BackBands
                    : profile.FrontBands;

                if (bands == null || bands.Count == 0)
                {
                    throw new InvalidOperationException("Profile does not have any bands defined for the selected side.");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                int totalSheetBands = bands.Count;
                int startBand = singleBandIndex.HasValue ? Math.Clamp(singleBandIndex.Value, 0, bands.Count - 1) : 0;
                int endBand = singleBandIndex.HasValue ? startBand + 1 : bands.Count;
                int totalPieces = (endBand - startBand) * 2 * 10;
                int currentPiece = 0;

                int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));
                string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix;

                for (int b = startBand; b < endBand; b++)
                {
                    var band = bands[b];
                    int xStart = (int)Math.Round(band.Bounds.Left * masterMat.Width);
                    int yStart = (int)Math.Round(band.Bounds.Top * masterMat.Height);
                    int wTotal = (int)Math.Round(band.Bounds.Width * masterMat.Width);
                    int hTotal = (int)Math.Round(band.Bounds.Height * masterMat.Height);

                    int h0 = (int)Math.Round(band.RowSplitRatio * hTotal);
                    int h1 = Math.Max(1, hTotal - h0);

                    // Build strictly monotonic divider list: d0 = 0.0, d1..d9, d10 = 1.0
                    var dividers = new List<double> { 0.0 };
                    if (band.ColumnDividers != null && band.ColumnDividers.Count == 9)
                    {
                        dividers.AddRange(band.ColumnDividers);
                    }
                    else
                    {
                        dividers.AddRange(Band2x10Definition.GetDefaultEvenDividers(10));
                    }
                    dividers.Add(1.0);

                    for (int r = 0; r < 2; r++)
                    {
                        int rowY = (r == 0 ? yStart : yStart + h0);
                        int rowH = (r == 0 ? h0 : h1);

                        for (int c = 0; c < 10; c++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int xLeft = xStart + (int)Math.Round(dividers[c] * wTotal);
                            int xRight = xStart + (int)Math.Round(dividers[c + 1] * wTotal);
                            int colW = Math.Max(1, xRight - xLeft);

                            int cellX = xLeft + inset;
                            int cellY = rowY + inset;
                            int pieceW = colW - (2 * inset);
                            int pieceH = rowH - (2 * inset);

                            // Clamp strictly inside master image boundaries
                            cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
                            cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
                            pieceW = Math.Max(1, Math.Min(pieceW, masterMat.Width - cellX));
                            pieceH = Math.Max(1, Math.Min(pieceH, masterMat.Height - cellY));

                            // Global Row and Column mapping
                            int globalRow = (b * 2) + r + 1; // 1 to 10 for 5 bands
                            int mappedCol = c;
                            int mappedRow = globalRow;

                            if (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                            {
                                switch (profile.BackOrientation)
                                {
                                    case BackOrientationMode.HorizontalFlip:
                                        mappedCol = 9 - c;
                                        break;
                                    case BackOrientationMode.VerticalFlip:
                                        mappedRow = (totalSheetBands * 2) - globalRow + 1;
                                        break;
                                    case BackOrientationMode.Rotate180:
                                        mappedCol = 9 - c;
                                        mappedRow = (totalSheetBands * 2) - globalRow + 1;
                                        break;
                                    case BackOrientationMode.Direct:
                                    default:
                                        break;
                                }
                            }

                            string filename = $"{prefix}_R{mappedRow:D2}_C{mappedCol + 1:D2}_{side}.png";
                            string outPath = Path.Combine(outputDirectory, filename);

                            // Zero-copy sub-Mat view for memory efficiency with dimension safety guard
                            if (pieceW > 0 && pieceH > 0)
                            {
                                Rect roi = new Rect(cellX, cellY, pieceW, pieceH);
                                using (var pieceMat = new Mat(masterMat, roi))
                                {
                                    Cv2.ImWrite(outPath, pieceMat);
                                }
                            }

                            currentPiece++;

                            progress?.Report(new SliceProgressReport
                            {
                                CurrentIndex = currentPiece,
                                TotalCount = totalPieces,
                                CurrentPieceName = filename,
                                StatusText = $"Exporting piece {currentPiece} of {totalPieces}: {filename}"
                            });
                        }
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Slices a deskewed counter sheet Mat into individual playing piece PNG files using a uniform grid asynchronously.
        /// </summary>
        public static Task SliceGridAsync(
            Mat masterMat,
            SheetProfile profile,
            string side,
            string outputDirectory,
            IProgress<SliceProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (masterMat == null || masterMat.IsDisposed || masterMat.Width <= 0 || masterMat.Height <= 0)
                {
                    throw new ArgumentException("Invalid master image Mat.", nameof(masterMat));
                }

                if (profile == null)
                {
                    throw new ArgumentNullException(nameof(profile));
                }

                if (profile.Rows <= 0 || profile.Columns <= 0)
                {
                    throw new ArgumentException("Rows and Columns must be greater than zero.");
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var bounds = (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                    ? profile.BackBounds
                    : profile.FrontBounds;

                int gridX = (int)Math.Round(bounds.Left * masterMat.Width);
                int gridY = (int)Math.Round(bounds.Top * masterMat.Height);
                int gridW = (int)Math.Round(bounds.Width * masterMat.Width);
                int gridH = (int)Math.Round(bounds.Height * masterMat.Height);

                double cellW = (double)gridW / profile.Columns;
                double cellH = (double)gridH / profile.Rows;

                int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));

                if ((int)cellW - (2 * inset) <= 0 || (int)cellH - (2 * inset) <= 0)
                {
                    throw new InvalidOperationException(
                        $"Cell dimensions ({cellW:F1} × {cellH:F1} px) are too small for an inward inset of {inset} px.");
                }

                int totalPieces = profile.Rows * profile.Columns;
                int currentPiece = 0;

                for (int r = 0; r < profile.Rows; r++)
                {
                    for (int c = 0; c < profile.Columns; c++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Resolve logical row/col coordinates based on side and orientation mapping
                        int mappedRow = r;
                        int mappedCol = c;

                        if (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                        {
                            switch (profile.BackOrientation)
                            {
                                case BackOrientationMode.HorizontalFlip:
                                    mappedCol = profile.Columns - 1 - c;
                                    mappedRow = r;
                                    break;

                                case BackOrientationMode.VerticalFlip:
                                    mappedRow = profile.Rows - 1 - r;
                                    mappedCol = c;
                                    break;

                                case BackOrientationMode.Rotate180:
                                    mappedRow = profile.Rows - 1 - r;
                                    mappedCol = profile.Columns - 1 - c;
                                    break;

                                case BackOrientationMode.Direct:
                                default:
                                    mappedRow = r;
                                    mappedCol = c;
                                    break;
                            }
                        }

                        // Physical pixel coordinates for this cell
                        int cellX = (int)Math.Round(gridX + c * cellW) + inset;
                        int cellY = (int)Math.Round(gridY + r * cellH) + inset;
                        int pieceW = (int)Math.Round(cellW) - (2 * inset);
                        int pieceH = (int)Math.Round(cellH) - (2 * inset);

                        // Clamp strictly inside master image boundary
                        cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
                        cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
                        pieceW = Math.Max(1, Math.Min(pieceW, masterMat.Width - cellX));
                        pieceH = Math.Max(1, Math.Min(pieceH, masterMat.Height - cellY));

                        // Generate filename
                        string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix;
                        string filename = $"{prefix}_R{mappedRow + 1:D2}_C{mappedCol + 1:D2}_{side}.png";
                        string outPath = Path.Combine(outputDirectory, filename);

                        // Zero-copy sub-Mat view for memory efficiency
                        Rect roi = new Rect(cellX, cellY, pieceW, pieceH);
                        using (var pieceMat = new Mat(masterMat, roi))
                        {
                            Cv2.ImWrite(outPath, pieceMat);
                        }

                        currentPiece++;

                        progress?.Report(new SliceProgressReport
                        {
                            CurrentIndex = currentPiece,
                            TotalCount = totalPieces,
                            CurrentPieceName = filename,
                            StatusText = $"Exporting piece {currentPiece} of {totalPieces}: {filename}"
                        });
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Extracts a zoomed preview of a single cell in a discrete band with cut line for visual inset tuning.
        /// </summary>
        public static Mat ExtractBandedCellPreview(
            Mat masterMat,
            SheetProfile profile,
            string side,
            int bandIndex,
            int localRow,
            int col)
        {
            if (masterMat == null || masterMat.IsDisposed || masterMat.Width <= 0 || masterMat.Height <= 0)
            {
                throw new ArgumentException("Invalid master image Mat.", nameof(masterMat));
            }

            var bands = side.Equals("Back", StringComparison.OrdinalIgnoreCase)
                ? profile.BackBands
                : profile.FrontBands;

            if (bands == null || bands.Count == 0)
            {
                return ExtractCellPreview(masterMat, profile, side, 0, 0);
            }

            int bIdx = Math.Clamp(bandIndex, 0, bands.Count - 1);
            var band = bands[bIdx];

            int xStart = (int)Math.Round(band.Bounds.Left * masterMat.Width);
            int yStart = (int)Math.Round(band.Bounds.Top * masterMat.Height);
            int wTotal = (int)Math.Round(band.Bounds.Width * masterMat.Width);
            int hTotal = (int)Math.Round(band.Bounds.Height * masterMat.Height);

            int h0 = (int)Math.Round(band.RowSplitRatio * hTotal);
            int h1 = Math.Max(1, hTotal - h0);

            int r = Math.Clamp(localRow, 0, 1);
            int c = Math.Clamp(col, 0, 9);

            var dividers = new List<double> { 0.0 };
            if (band.ColumnDividers != null && band.ColumnDividers.Count == 9)
            {
                dividers.AddRange(band.ColumnDividers);
            }
            else
            {
                dividers.AddRange(Band2x10Definition.GetDefaultEvenDividers(10));
            }
            dividers.Add(1.0);

            int rowY = (r == 0 ? yStart : yStart + h0);
            int rowH = (r == 0 ? h0 : h1);

            int xLeft = xStart + (int)Math.Round(dividers[c] * wTotal);
            int xRight = xStart + (int)Math.Round(dividers[c + 1] * wTotal);
            int colW = Math.Max(1, xRight - xLeft);

            int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));

            // Clamp
            int cellX = Math.Max(0, Math.Min(xLeft, masterMat.Width - 1));
            int cellY = Math.Max(0, Math.Min(rowY, masterMat.Height - 1));
            int cellWidthInt = Math.Max(1, Math.Min(colW, masterMat.Width - cellX));
            int cellHeightInt = Math.Max(1, Math.Min(rowH, masterMat.Height - cellY));

            Rect cellRoi = new Rect(cellX, cellY, cellWidthInt, cellHeightInt);
            using var cellMat = new Mat(masterMat, cellRoi);

            // Create a preview copy and draw inset guideline (red rectangle)
            var previewMat = cellMat.Clone();
            if (inset > 0 && cellWidthInt > 2 * inset && cellHeightInt > 2 * inset)
            {
                Rect insetRect = new Rect(inset, inset, cellWidthInt - 2 * inset, cellHeightInt - 2 * inset);
                Cv2.Rectangle(previewMat, insetRect, new Scalar(0, 0, 255), thickness: 2); // Red border showing crop line
            }

            return previewMat;
        }

        /// <summary>
        /// Extracts a zoomed preview of a single cell (e.g. Row 0, Col 0) with cut line for visual inset tuning.
        /// </summary>
        public static Mat ExtractCellPreview(Mat masterMat, SheetProfile profile, string side, int row = 0, int col = 0)
        {
            if (masterMat == null || masterMat.IsDisposed || masterMat.Width <= 0 || masterMat.Height <= 0)
            {
                throw new ArgumentException("Invalid master image Mat.", nameof(masterMat));
            }

            var bounds = (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                ? profile.BackBounds
                : profile.FrontBounds;

            int gridX = (int)Math.Round(bounds.Left * masterMat.Width);
            int gridY = (int)Math.Round(bounds.Top * masterMat.Height);
            int gridW = (int)Math.Round(bounds.Width * masterMat.Width);
            int gridH = (int)Math.Round(bounds.Height * masterMat.Height);

            double cellW = (double)gridW / profile.Columns;
            double cellH = (double)gridH / profile.Rows;

            int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));

            int cellX = (int)Math.Round(gridX + col * cellW);
            int cellY = (int)Math.Round(gridY + row * cellH);
            int cellWidthInt = (int)Math.Round(cellW);
            int cellHeightInt = (int)Math.Round(cellH);

            // Clamp
            cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
            cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
            cellWidthInt = Math.Max(1, Math.Min(cellWidthInt, masterMat.Width - cellX));
            cellHeightInt = Math.Max(1, Math.Min(cellHeightInt, masterMat.Height - cellY));

            Rect cellRoi = new Rect(cellX, cellY, cellWidthInt, cellHeightInt);
            using var cellMat = new Mat(masterMat, cellRoi);

            // Create a preview copy and draw inset guideline (red border)
            var previewMat = cellMat.Clone();
            if (inset > 0 && cellWidthInt > 2 * inset && cellHeightInt > 2 * inset)
            {
                Rect insetRect = new Rect(inset, inset, cellWidthInt - 2 * inset, cellHeightInt - 2 * inset);
                Cv2.Rectangle(previewMat, insetRect, new Scalar(0, 0, 255), thickness: 2); // Red border showing crop line
            }

            return previewMat;
        }
    }
}
