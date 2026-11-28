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
        public static bool IsRearSide(string? side) =>
            string.Equals(side, "Rear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(side, "Back", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Computes the list of output filenames that will be generated for a banded slice operation without writing to disk.
        /// </summary>
        public static List<string> GetBandedOutputFilenames(
            SheetProfile profile,
            string side,
            int? singleBandIndex = null,
            int? bandRows = null,
            int? bandCols = null)
        {
            if (profile == null) return new List<string>();

            var bands = side.Equals("Back", StringComparison.OrdinalIgnoreCase) && profile.BackBands != null && profile.BackBands.Count > 0
                ? profile.BackBands
                : profile.FrontBands;

            if (bands == null || bands.Count == 0) return new List<string>();

            int effectiveRows = Math.Max(1, bandRows ?? profile.Rows);
            int effectiveCols = Math.Max(1, bandCols ?? profile.Columns);
            int totalSheetBands = bands.Count;
            int startBand = singleBandIndex.HasValue ? Math.Clamp(singleBandIndex.Value, 0, bands.Count - 1) : 0;
            int endBand = singleBandIndex.HasValue ? startBand + 1 : bands.Count;
            string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix;

            var filenames = new List<string>();
            for (int b = startBand; b < endBand; b++)
            {
                for (int r = 0; r < effectiveRows; r++)
                {
                    for (int c = 0; c < effectiveCols; c++)
                    {
                        int globalRow = (b * effectiveRows) + r + 1;
                        int mappedCol = c;
                        int mappedRow = globalRow;

                        if (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                        {
                            switch (profile.BackOrientation)
                            {
                                case BackOrientationMode.HorizontalFlip:
                                    mappedCol = (effectiveCols - 1) - c;
                                    break;
                                case BackOrientationMode.VerticalFlip:
                                    mappedRow = (totalSheetBands * effectiveRows) - globalRow + 1;
                                    break;
                                case BackOrientationMode.Rotate180:
                                    mappedCol = (effectiveCols - 1) - c;
                                    mappedRow = (totalSheetBands * effectiveRows) - globalRow + 1;
                                    break;
                                case BackOrientationMode.Direct:
                                default:
                                    break;
                            }
                        }

                        string filename = $"{prefix}_R{mappedRow:D2}_C{mappedCol + 1:D2}_{side}.png";
                        filenames.Add(filename);
                    }
                }
            }
            return filenames;
        }

        /// <summary>
        /// Computes the list of output filenames that will be generated for a standard grid slice operation without writing to disk.
        /// </summary>
        public static List<string> GetGridOutputFilenames(
            SheetProfile profile,
            string side)
        {
            if (profile == null) return new List<string>();

            string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix;
            var filenames = new List<string>();
            for (int r = 0; r < profile.Rows; r++)
            {
                for (int c = 0; c < profile.Columns; c++)
                {
                    int mappedCol = c;
                    int mappedRow = r;

                    if (side.Equals("Back", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (profile.BackOrientation)
                        {
                            case BackOrientationMode.HorizontalFlip:
                                mappedCol = (profile.Columns - 1) - c;
                                break;
                            case BackOrientationMode.VerticalFlip:
                                mappedRow = (profile.Rows - 1) - r;
                                break;
                            case BackOrientationMode.Rotate180:
                                mappedCol = (profile.Columns - 1) - c;
                                mappedRow = (profile.Rows - 1) - r;
                                break;
                            case BackOrientationMode.Direct:
                            default:
                                break;
                        }
                    }

                    string filename = $"{prefix}_R{mappedRow + 1:D2}_C{mappedCol + 1:D2}_{side}.png";
                    filenames.Add(filename);
                }
            }
            return filenames;
        }

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
            int? singleBandIndex = null,
            int bandRows = 2,
            int bandCols = 10,
            bool skipExisting = false,
            Mat? patternMat = null,
            bool fillBuffer = false,
            Func<int, int, (int shiftX, int shiftY)>? getBandTileShift = null,
            Func<int, (int shiftX, int shiftY)>? getTileShift = null)
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

                var bands = IsRearSide(side)
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

                int effectiveRows = Math.Max(1, bandRows);
                int effectiveCols = Math.Max(1, bandCols);

                int totalSheetBands = bands.Count;
                int startBand = singleBandIndex.HasValue ? Math.Clamp(singleBandIndex.Value, 0, bands.Count - 1) : 0;
                int endBand = singleBandIndex.HasValue ? startBand + 1 : bands.Count;
                int totalPieces = (endBand - startBand) * effectiveRows * effectiveCols;
                int currentPiece = 0;

                int insetTop = Math.Max(0, (int)Math.Round(profile.InwardInsetTopPx));
                int insetBottom = Math.Max(0, (int)Math.Round(profile.InwardInsetBottomPx));
                int insetLeft = Math.Max(0, (int)Math.Round(profile.InwardInsetLeftPx));
                int insetRight = Math.Max(0, (int)Math.Round(profile.InwardInsetRightPx));
                int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));
                if (insetTop == 0 && insetBottom == 0 && insetLeft == 0 && insetRight == 0 && inset > 0)
                {
                    insetTop = insetBottom = insetLeft = insetRight = inset;
                }
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

                    // Build strictly monotonic divider list: d0 = 0.0, d1..d(cols-1), d(cols) = 1.0
                    var dividers = new List<double> { 0.0 };
                    if (effectiveCols == 10 && band.ColumnDividers != null && band.ColumnDividers.Count == 9)
                    {
                        dividers.AddRange(band.ColumnDividers);
                    }
                    else
                    {
                        dividers.AddRange(Band2x10Definition.GetDefaultEvenDividers(effectiveCols));
                    }
                    dividers.Add(1.0);

                    for (int r = 0; r < effectiveRows; r++)
                    {
                        int rowY;
                        int rowH;
                        if (effectiveRows == 2)
                        {
                            rowY = (r == 0 ? yStart : yStart + h0);
                            rowH = (r == 0 ? h0 : h1);
                        }
                        else
                        {
                            rowY = yStart + (int)Math.Round(hTotal * (r / (double)effectiveRows));
                            int nextY = yStart + (int)Math.Round(hTotal * ((r + 1) / (double)effectiveRows));
                            rowH = Math.Max(1, nextY - rowY);
                        }

                        for (int c = 0; c < effectiveCols; c++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int xLeft = xStart + (int)Math.Round(dividers[c] * wTotal);
                            int xRight = xStart + (int)Math.Round(dividers[c + 1] * wTotal);
                            int colW = Math.Max(1, xRight - xLeft);

                            int chitNum = (r * effectiveCols) + c + 1;
                            var (shiftX, shiftY) = getBandTileShift?.Invoke(b, chitNum) ?? getTileShift?.Invoke(chitNum) ?? (0, 0);

                            bool hasAnyInset = insetTop > 0 || insetBottom > 0 || insetLeft > 0 || insetRight > 0;
                            bool usePattern = fillBuffer && patternMat != null && !patternMat.IsDisposed && hasAnyInset;
                            bool applyFillOrShift = usePattern || shiftX != 0 || shiftY != 0;

                            int cellX = applyFillOrShift ? xLeft : (xLeft + insetLeft);
                            int cellY = applyFillOrShift ? rowY : (rowY + insetTop);
                            int pieceW = applyFillOrShift ? colW : (colW - insetLeft - insetRight);
                            int pieceH = applyFillOrShift ? rowH : (rowH - insetTop - insetBottom);

                            // Clamp strictly inside master image boundaries
                            cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
                            cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
                            pieceW = Math.Max(1, Math.Min(pieceW, masterMat.Width - cellX));
                            pieceH = Math.Max(1, Math.Min(pieceH, masterMat.Height - cellY));

                            // Global Row and Column mapping
                            int globalRow = (b * effectiveRows) + r + 1;
                            int mappedCol = c;
                            int mappedRow = globalRow;

                            if (IsRearSide(side))
                            {
                                switch (profile.BackOrientation)
                                {
                                    case BackOrientationMode.HorizontalFlip:
                                        mappedCol = (effectiveCols - 1) - c;
                                        break;
                                    case BackOrientationMode.VerticalFlip:
                                        mappedRow = (totalSheetBands * effectiveRows) - globalRow + 1;
                                        break;
                                    case BackOrientationMode.Rotate180:
                                        mappedCol = (effectiveCols - 1) - c;
                                        mappedRow = (totalSheetBands * effectiveRows) - globalRow + 1;
                                        break;
                                    case BackOrientationMode.Direct:
                                    default:
                                        break;
                                }
                            }

                            string filename = $"{prefix}_R{mappedRow:D2}_C{mappedCol + 1:D2}_{side}.png";
                            string outPath = Path.Combine(outputDirectory, filename);

                            if (skipExisting && File.Exists(outPath))
                            {
                                currentPiece++;
                                progress?.Report(new SliceProgressReport
                                {
                                    CurrentIndex = currentPiece,
                                    TotalCount = totalPieces,
                                    CurrentPieceName = filename,
                                    StatusText = $"Skipping existing piece {currentPiece} of {totalPieces}: {filename}"
                                });
                                continue;
                            }

                            // Zero-copy sub-Mat view for memory efficiency with dimension safety guard
                            if (pieceW > 0 && pieceH > 0)
                            {
                                Rect roi = new Rect(cellX, cellY, pieceW, pieceH);
                                using (var pieceMat = new Mat(masterMat, roi))
                                {
                                    try
                                    {
                                        if (applyFillOrShift)
                                        {
                                            using var filledMat = ApplyBufferPatternFill(pieceMat, fillBuffer ? patternMat : null, insetTop, insetBottom, insetLeft, insetRight, shiftX, shiftY);
                                            Cv2.ImWrite(outPath, filledMat);
                                        }
                                        else
                                        {
                                            Cv2.ImWrite(outPath, pieceMat);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Failed to write piece {outPath}: {ex.Message}");
                                        throw;
                                    }
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
        /// Slices a calibrated discrete band directly into in-memory ChitItem instances (CV_8UC4 BGRA)
        /// for real-time editing in the Chit Editor (Tab 3). Supports dynamic bandRows and bandCols.
        /// </summary>
        public static Task<List<ChitItem>> SliceBandedGridToMemoryAsync(
            Mat masterMat,
            SheetProfile profile,
            string side,
            int? bandIndex,
            IProgress<SliceProgressReport>? progress,
            CancellationToken cancellationToken,
            int bandRows = 2,
            int bandCols = 10,
            Mat? patternMat = null,
            bool fillBuffer = false,
            Func<int, (int shiftX, int shiftY)>? getTileShift = null)
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

                var bands = IsRearSide(side)
                    ? profile.BackBands
                    : profile.FrontBands;

                if (bands == null || bands.Count == 0)
                {
                    throw new InvalidOperationException("No 2×10 band definitions found for the active profile.");
                }

                int effectiveRows = Math.Max(1, bandRows);
                int effectiveCols = Math.Max(1, bandCols);

                int totalSheetBands = bands.Count;
                int startBand = bandIndex.HasValue ? Math.Clamp(bandIndex.Value, 0, bands.Count - 1) : 0;
                int endBand = bandIndex.HasValue ? startBand + 1 : bands.Count;

                int totalPieces = (endBand - startBand) * effectiveRows * effectiveCols;
                int currentPiece = 0;
                string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix.Trim();

                var chitList = new List<ChitItem>();

                for (int b = startBand; b < endBand; b++)
                {
                    var band = bands[b];
                    int bandX = (int)Math.Round(band.Bounds.Left * masterMat.Width);
                    int bandY = (int)Math.Round(band.Bounds.Top * masterMat.Height);
                    int bandW = (int)Math.Round(band.Bounds.Width * masterMat.Width);
                    int bandH = (int)Math.Round(band.Bounds.Height * masterMat.Height);

                    int h0 = (int)Math.Round(band.RowSplitRatio * bandH);
                    int h1 = Math.Max(1, bandH - h0);

                    // Build strictly monotonic divider list: d0 = 0.0, d1..d(cols-1), d(cols) = 1.0
                    var dividers = new List<double> { 0.0 };
                    if (effectiveCols == 10 && band.ColumnDividers != null && band.ColumnDividers.Count == 9)
                    {
                        dividers.AddRange(band.ColumnDividers);
                    }
                    else
                    {
                        dividers.AddRange(Band2x10Definition.GetDefaultEvenDividers(effectiveCols));
                    }
                    dividers.Add(1.0);

                    bool isBack = IsRearSide(side);

                    for (int r = 0; r < effectiveRows; r++)
                    {
                        int rowTop;
                        int rowHeight;
                        if (effectiveRows == 2)
                        {
                            rowTop = (r == 0) ? bandY : bandY + h0;
                            rowHeight = (r == 0) ? h0 : h1;
                        }
                        else
                        {
                            rowTop = bandY + (int)Math.Round(bandH * (r / (double)effectiveRows));
                            int nextY = bandY + (int)Math.Round(bandH * ((r + 1) / (double)effectiveRows));
                            rowHeight = Math.Max(1, nextY - rowTop);
                        }

                        for (int colStep = 0; colStep < effectiveCols; colStep++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Front: left-to-right (0 to cols-1). Back: right-to-left (cols-1 down to 0)
                            int c = isBack ? (effectiveCols - 1 - colStep) : colStep;

                            double leftRatio = dividers[c];
                            double rightRatio = dividers[c + 1];

                            int colLeft = bandX + (int)Math.Round(leftRatio * bandW);
                            int colRight = bandX + (int)Math.Round(rightRatio * bandW);
                            int colWidth = colRight - colLeft;

                            int chitNum = currentPiece + 1;
                            var (shiftX, shiftY) = getTileShift?.Invoke(chitNum) ?? (0, 0);

                            // Slicing applies profile inward inset / buffer
                            int insetTop = Math.Max(0, (int)Math.Round(profile.InwardInsetTopPx));
                            int insetBottom = Math.Max(0, (int)Math.Round(profile.InwardInsetBottomPx));
                            int insetLeft = Math.Max(0, (int)Math.Round(profile.InwardInsetLeftPx));
                            int insetRight = Math.Max(0, (int)Math.Round(profile.InwardInsetRightPx));
                            int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));
                            if (insetTop == 0 && insetBottom == 0 && insetLeft == 0 && insetRight == 0 && inset > 0)
                            {
                                insetTop = insetBottom = insetLeft = insetRight = inset;
                            }
                            bool hasAnyInset = insetTop > 0 || insetBottom > 0 || insetLeft > 0 || insetRight > 0;
                            bool usePattern = fillBuffer && patternMat != null && !patternMat.IsDisposed && hasAnyInset;
                            bool applyFillOrShift = usePattern || shiftX != 0 || shiftY != 0;

                            int cellX = applyFillOrShift ? colLeft : (colLeft + insetLeft);
                            int cellY = applyFillOrShift ? rowTop : (rowTop + insetTop);
                            int pieceW = applyFillOrShift ? colWidth : (colWidth - insetLeft - insetRight);
                            int pieceH = applyFillOrShift ? rowHeight : (rowHeight - insetTop - insetBottom);

                            cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
                            cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
                            pieceW = Math.Max(1, Math.Min(pieceW, masterMat.Width - cellX));
                            pieceH = Math.Max(1, Math.Min(pieceH, masterMat.Height - cellY));

                            int globalRow = (b * effectiveRows) + r + 1;
                            // File naming preserves sequential tile number matching front tiles
                            string filename = $"{prefix}_R{globalRow:D2}_C{currentPiece + 1:D2}_{side}.png";

                            if (pieceW > 0 && pieceH > 0)
                            {
                                Rect roi = new Rect(cellX, cellY, pieceW, pieceH);
                                using var cropMat = new Mat(masterMat, roi);

                                Mat bgraMat;
                                if (applyFillOrShift)
                                {
                                    bgraMat = ApplyBufferPatternFill(cropMat, fillBuffer ? patternMat : null, insetTop, insetBottom, insetLeft, insetRight, shiftX, shiftY);
                                }
                                else
                                {
                                    bgraMat = new Mat();
                                    if (cropMat.Channels() == 3)
                                    {
                                        Cv2.CvtColor(cropMat, bgraMat, ColorConversionCodes.BGR2BGRA);
                                    }
                                    else
                                    {
                                        cropMat.CopyTo(bgraMat);
                                    }
                                }

                                var chit = new ChitItem(
                                    index: currentPiece + 1,
                                    row: r + 1,
                                    col: c + 1,
                                    side: side,
                                    pieceName: filename,
                                    bgraMat: bgraMat
                                );
                                chitList.Add(chit);
                            }

                            currentPiece++;

                            progress?.Report(new SliceProgressReport
                            {
                                CurrentIndex = currentPiece,
                                TotalCount = totalPieces,
                                CurrentPieceName = filename,
                                StatusText = $"Slicing in-memory piece {currentPiece} of {totalPieces}: {filename}"
                            });
                        }
                    }
                }

                return chitList;
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
            CancellationToken cancellationToken,
            bool skipExisting = false,
            Mat? patternMat = null,
            bool fillBuffer = false,
            Func<int, (int shiftX, int shiftY)>? getTileShift = null)
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

                var bounds = (IsRearSide(side))
                    ? profile.BackBounds
                    : profile.FrontBounds;

                int gridX = (int)Math.Round(bounds.Left * masterMat.Width);
                int gridY = (int)Math.Round(bounds.Top * masterMat.Height);
                int gridW = (int)Math.Round(bounds.Width * masterMat.Width);
                int gridH = (int)Math.Round(bounds.Height * masterMat.Height);

                double cellW = (double)gridW / profile.Columns;
                double cellH = (double)gridH / profile.Rows;

                int insetTop = Math.Max(0, (int)Math.Round(profile.InwardInsetTopPx));
                int insetBottom = Math.Max(0, (int)Math.Round(profile.InwardInsetBottomPx));
                int insetLeft = Math.Max(0, (int)Math.Round(profile.InwardInsetLeftPx));
                int insetRight = Math.Max(0, (int)Math.Round(profile.InwardInsetRightPx));
                int inset = Math.Max(0, (int)Math.Round(profile.InwardInsetPx));
                if (insetTop == 0 && insetBottom == 0 && insetLeft == 0 && insetRight == 0 && inset > 0)
                {
                    insetTop = insetBottom = insetLeft = insetRight = inset;
                }

                if ((int)cellW - (insetLeft + insetRight) <= 0 || (int)cellH - (insetTop + insetBottom) <= 0)
                {
                    throw new InvalidOperationException(
                        $"Cell dimensions ({cellW:F1} × {cellH:F1} px) are too small for inward insets (L:{insetLeft}, R:{insetRight}, T:{insetTop}, B:{insetBottom}).");
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

                        if (IsRearSide(side))
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
                        int tileIndex = (r * profile.Columns) + c + 1;
                        var (shiftX, shiftY) = getTileShift?.Invoke(tileIndex) ?? (0, 0);

                        bool hasAnyInset = insetTop > 0 || insetBottom > 0 || insetLeft > 0 || insetRight > 0;
                        bool usePattern = fillBuffer && patternMat != null && !patternMat.IsDisposed && hasAnyInset;
                        bool applyFillOrShift = usePattern || shiftX != 0 || shiftY != 0;

                        int cellX = applyFillOrShift ? (int)Math.Round(gridX + c * cellW) : ((int)Math.Round(gridX + c * cellW) + insetLeft);
                        int cellY = applyFillOrShift ? (int)Math.Round(gridY + r * cellH) : ((int)Math.Round(gridY + r * cellH) + insetTop);
                        int pieceW = applyFillOrShift ? (int)Math.Round(cellW) : ((int)Math.Round(cellW) - (insetLeft + insetRight));
                        int pieceH = applyFillOrShift ? (int)Math.Round(cellH) : ((int)Math.Round(cellH) - (insetTop + insetBottom));

                        // Clamp strictly inside master image boundary
                        cellX = Math.Max(0, Math.Min(cellX, masterMat.Width - 1));
                        cellY = Math.Max(0, Math.Min(cellY, masterMat.Height - 1));
                        pieceW = Math.Max(1, Math.Min(pieceW, masterMat.Width - cellX));
                        pieceH = Math.Max(1, Math.Min(pieceH, masterMat.Height - cellY));

                        // Generate filename
                        string prefix = string.IsNullOrWhiteSpace(profile.NamingPrefix) ? "Piece" : profile.NamingPrefix;
                        string filename = $"{prefix}_R{mappedRow + 1:D2}_C{mappedCol + 1:D2}_{side}.png";
                        string outPath = Path.Combine(outputDirectory, filename);

                        if (skipExisting && File.Exists(outPath))
                        {
                            currentPiece++;
                            progress?.Report(new SliceProgressReport
                            {
                                CurrentIndex = currentPiece,
                                TotalCount = totalPieces,
                                CurrentPieceName = filename,
                                StatusText = $"Skipping existing piece {currentPiece} of {totalPieces}: {filename}"
                            });
                            continue;
                        }

                        // Zero-copy sub-Mat view for memory efficiency
                        Rect roi = new Rect(cellX, cellY, pieceW, pieceH);
                        using (var pieceMat = new Mat(masterMat, roi))
                        {
                            try
                            {
                                if (applyFillOrShift)
                                {
                                    using var filledMat = ApplyBufferPatternFill(pieceMat, fillBuffer ? patternMat : null, insetTop, insetBottom, insetLeft, insetRight, shiftX, shiftY);
                                    Cv2.ImWrite(outPath, filledMat);
                                }
                                else
                                {
                                    Cv2.ImWrite(outPath, pieceMat);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to write piece {outPath}: {ex.Message}");
                                throw;
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
            int col,
            int bandRows = 2,
            int bandCols = 10)
        {
            if (masterMat == null || masterMat.IsDisposed || masterMat.Width <= 0 || masterMat.Height <= 0)
            {
                throw new ArgumentException("Invalid master image Mat.", nameof(masterMat));
            }

            var bands = IsRearSide(side)
                ? profile.BackBands
                : profile.FrontBands;

            if (bands == null || bands.Count == 0)
            {
                return ExtractCellPreview(masterMat, profile, side, 0, 0);
            }

            int bIdx = Math.Clamp(bandIndex, 0, bands.Count - 1);
            var band = bands[bIdx];

            int effectiveRows = Math.Max(1, bandRows);
            int effectiveCols = Math.Max(1, bandCols);

            int xStart = (int)Math.Round(band.Bounds.Left * masterMat.Width);
            int yStart = (int)Math.Round(band.Bounds.Top * masterMat.Height);
            int wTotal = (int)Math.Round(band.Bounds.Width * masterMat.Width);
            int hTotal = (int)Math.Round(band.Bounds.Height * masterMat.Height);

            int h0 = (int)Math.Round(band.RowSplitRatio * hTotal);
            int h1 = Math.Max(1, hTotal - h0);

            int r = Math.Clamp(localRow, 0, effectiveRows - 1);
            int c = Math.Clamp(col, 0, effectiveCols - 1);

            var dividers = new List<double> { 0.0 };
            if (effectiveCols == 10 && band.ColumnDividers != null && band.ColumnDividers.Count == 9)
            {
                dividers.AddRange(band.ColumnDividers);
            }
            else
            {
                dividers.AddRange(Band2x10Definition.GetDefaultEvenDividers(effectiveCols));
            }
            dividers.Add(1.0);

            int rowY;
            int rowH;
            if (effectiveRows == 2)
            {
                rowY = (r == 0 ? yStart : yStart + h0);
                rowH = (r == 0 ? h0 : h1);
            }
            else
            {
                rowY = yStart + (int)Math.Round(hTotal * (r / (double)effectiveRows));
                int nextY = yStart + (int)Math.Round(hTotal * ((r + 1) / (double)effectiveRows));
                rowH = Math.Max(1, nextY - rowY);
            }

            int xLeft = xStart + (int)Math.Round(dividers[c] * wTotal);
            int xRight = xStart + (int)Math.Round(dividers[c + 1] * wTotal);
            int colW = Math.Max(1, xRight - xLeft);

            // Clamp
            int cellX = Math.Max(0, Math.Min(xLeft, masterMat.Width - 1));
            int cellY = Math.Max(0, Math.Min(rowY, masterMat.Height - 1));
            int cellWidthInt = Math.Max(1, Math.Min(colW, masterMat.Width - cellX));
            int cellHeightInt = Math.Max(1, Math.Min(rowH, masterMat.Height - cellY));

            Rect cellRoi = new Rect(cellX, cellY, cellWidthInt, cellHeightInt);
            using var cellMat = new Mat(masterMat, cellRoi);

            return cellMat.Clone();
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

            var bounds = (IsRearSide(side))
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

        /// <summary>
        /// Tiles patternMat across the full (W, H) cell (or initializes a transparent canvas if pattern is absent),
        /// blits the inner [inset..W-inset, inset..H-inset] core artwork shifted by (shiftX, shiftY) with safe clipping,
        /// and applies a 1-pixel linear blend along the boundary.
        /// Returns a new Mat of size (W, H) in CV_8UC4 (BGRA).
        /// </summary>
        public static Mat ApplyBufferPatternFill(Mat cellMat, Mat? patternMat, int inset, int shiftX = 0, int shiftY = 0)
        {
            return ApplyBufferPatternFill(cellMat, patternMat, inset, inset, inset, inset, shiftX, shiftY);
        }

        /// <summary>
        /// Tiles patternMat across the full (W, H) cell (or initializes a transparent canvas if pattern is absent),
        /// blits the inner [insetLeft..W-insetRight, insetTop..H-insetBottom] core artwork shifted by (shiftX, shiftY) with safe clipping,
        /// and applies a 1-pixel linear blend along the boundary.
        /// Returns a new Mat of size (W, H) in CV_8UC4 (BGRA).
        /// </summary>
        public static Mat ApplyBufferPatternFill(
            Mat cellMat,
            Mat? patternMat,
            int insetTop,
            int insetBottom,
            int insetLeft,
            int insetRight,
            int shiftX = 0,
            int shiftY = 0)
        {
            if (cellMat == null || cellMat.IsDisposed) throw new ArgumentNullException(nameof(cellMat));

            int w = cellMat.Width;
            int h = cellMat.Height;
            int safeTop = Math.Max(0, insetTop);
            int safeBottom = Math.Max(0, insetBottom);
            int safeLeft = Math.Max(0, insetLeft);
            int safeRight = Math.Max(0, insetRight);

            if (safeTop <= 0 && safeBottom <= 0 && safeLeft <= 0 && safeRight <= 0 && shiftX == 0 && shiftY == 0)
            {
                var copy = new Mat();
                if (cellMat.Channels() == 3)
                    Cv2.CvtColor(cellMat, copy, ColorConversionCodes.BGR2BGRA);
                else
                    cellMat.CopyTo(copy);
                return copy;
            }

            // Ensure source cell is BGRA
            using var bgraCell = new Mat();
            if (cellMat.Channels() == 3)
                Cv2.CvtColor(cellMat, bgraCell, ColorConversionCodes.BGR2BGRA);
            else
                cellMat.CopyTo(bgraCell);

            Mat result;
            bool hasPattern = patternMat != null && !patternMat.IsDisposed;
            if (hasPattern)
            {
                using var bgraPattern = new Mat();
                if (patternMat!.Channels() == 3)
                    Cv2.CvtColor(patternMat, bgraPattern, ColorConversionCodes.BGR2BGRA);
                else
                    patternMat.CopyTo(bgraPattern);

                int patW = Math.Max(1, bgraPattern.Width);
                int patH = Math.Max(1, bgraPattern.Height);
                int repeatX = (w / patW) + 1;
                int repeatY = (h / patH) + 1;

                using var repeatedPattern = new Mat();
                Cv2.Repeat(bgraPattern, repeatY, repeatX, repeatedPattern);
                using var croppedBg = new Mat(repeatedPattern, new Rect(0, 0, w, h));
                result = croppedBg.Clone();
            }
            else
            {
                result = new Mat(h, w, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
            }

            int coreX = safeLeft;
            int coreY = safeTop;
            int coreW = Math.Max(0, w - safeLeft - safeRight);
            int coreH = Math.Max(0, h - safeTop - safeBottom);

            if (coreW > 0 && coreH > 0)
            {
                // Destination target coordinates with shift applied
                int destX = safeLeft + shiftX;
                int destY = safeTop + shiftY;

                // Clip destination rectangle against result boundaries [0, 0, w, h]
                int clipDstX = Math.Max(0, destX);
                int clipDstY = Math.Max(0, destY);
                int clipDstRight = Math.Min(w, destX + coreW);
                int clipDstBottom = Math.Min(h, destY + coreH);

                int blitW = clipDstRight - clipDstX;
                int blitH = clipDstBottom - clipDstY;

                if (blitW > 0 && blitH > 0)
                {
                    // Corresponding source rectangle inside bgraCell
                    int srcX = coreX + (clipDstX - destX);
                    int srcY = coreY + (clipDstY - destY);

                    srcX = Math.Max(0, Math.Min(srcX, bgraCell.Width - blitW));
                    srcY = Math.Max(0, Math.Min(srcY, bgraCell.Height - blitH));

                    var srcRoi = new Rect(srcX, srcY, blitW, blitH);
                    var dstRoi = new Rect(clipDstX, clipDstY, blitW, blitH);

                    using (var innerCell = new Mat(bgraCell, srcRoi))
                    using (var targetSlot = new Mat(result, dstRoi))
                    {
                        innerCell.CopyTo(targetSlot);
                    }

                    // 1-pixel linear seam blend along perimeter if pattern is present and blit region is large enough
                    if (hasPattern && blitW > 2 && blitH > 2)
                    {
                        // Top & bottom edge rows inside blit region
                        BlendHorizontalSeam(result, bgraCell, clipDstX, clipDstY, srcX, srcY, blitW);
                        BlendHorizontalSeam(result, bgraCell, clipDstX, clipDstY + blitH - 1, srcX, srcY + blitH - 1, blitW);
                        // Left & right edge columns inside blit region
                        BlendVerticalSeam(result, bgraCell, clipDstX, clipDstY, srcX, srcY, blitH);
                        BlendVerticalSeam(result, bgraCell, clipDstX + blitW - 1, clipDstY, srcX + blitW - 1, srcY, blitH);
                    }
                }
            }

            return result;
        }

        private static void BlendHorizontalSeam(Mat dst, Mat src, int dstStartX, int dstY, int srcStartX, int srcY, int width)
        {
            if (dstY < 0 || dstY >= dst.Height || srcY < 0 || srcY >= src.Height) return;
            for (int i = 0; i < width; i++)
            {
                int dx = dstStartX + i;
                int sx = srcStartX + i;
                if (dx < 0 || dx >= dst.Width || sx < 0 || sx >= src.Width) continue;
                Vec4b cDst = dst.Get<Vec4b>(dstY, dx);
                Vec4b cSrc = src.Get<Vec4b>(srcY, sx);
                byte b = (byte)((cDst.Item0 + cSrc.Item0) / 2);
                byte g = (byte)((cDst.Item1 + cSrc.Item1) / 2);
                byte r = (byte)((cDst.Item2 + cSrc.Item2) / 2);
                byte a = (byte)((cDst.Item3 + cSrc.Item3) / 2);
                dst.Set(dstY, dx, new Vec4b(b, g, r, a));
            }
        }

        private static void BlendVerticalSeam(Mat dst, Mat src, int dstX, int dstStartY, int srcX, int srcStartY, int height)
        {
            if (dstX < 0 || dstX >= dst.Width || srcX < 0 || srcX >= src.Width) return;
            for (int i = 0; i < height; i++)
            {
                int dy = dstStartY + i;
                int sy = srcStartY + i;
                if (dy < 0 || dy >= dst.Height || sy < 0 || sy >= src.Height) continue;
                Vec4b cDst = dst.Get<Vec4b>(dy, dstX);
                Vec4b cSrc = src.Get<Vec4b>(sy, srcX);
                byte b = (byte)((cDst.Item0 + cSrc.Item0) / 2);
                byte g = (byte)((cDst.Item1 + cSrc.Item1) / 2);
                byte r = (byte)((cDst.Item2 + cSrc.Item2) / 2);
                byte a = (byte)((cDst.Item3 + cSrc.Item3) / 2);
                dst.Set(dy, dstX, new Vec4b(b, g, r, a));
            }
        }
    }
}
