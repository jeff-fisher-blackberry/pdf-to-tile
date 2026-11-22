using System;
using System.IO;
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
    /// Supports inward bleed insets and front/back coordinate orientation mapping.
    /// </summary>
    public static class GridSlicerService
    {
        /// <summary>
        /// Slices a deskewed counter sheet Mat into individual playing piece PNG files asynchronously.
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

            // Create a preview copy and draw inset guideline (red dashed or solid rectangle)
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
