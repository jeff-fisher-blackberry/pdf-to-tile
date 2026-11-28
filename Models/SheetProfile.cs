using System;
using System.Collections.Generic;
using System.Linq;

namespace pdf_to_Tiles.Models
{
    /// <summary>
    /// Relative bounding box coordinates (normalized 0.0 to 1.0) so profiles apply reliably across 300/450/600 DPI scans.
    /// </summary>
    public class GridRelativeBounds
    {
        public double Left { get; set; } = 0.05;
        public double Top { get; set; } = 0.05;
        public double Width { get; set; } = 0.90;
        public double Height { get; set; } = 0.90;

        public GridRelativeBounds Clone()
        {
            return new GridRelativeBounds
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height
            };
        }
    }

    /// <summary>
    /// Specifies how back-side counter grid coordinates map to the front-side piece coordinates.
    /// </summary>
    public enum BackOrientationMode
    {
        /// <summary>
        /// Back side shares the exact same row and column as the front side.
        /// </summary>
        Direct,

        /// <summary>
        /// Back side is flipped horizontally (common for bookfold/magazine sheets: Col = MaxCol - Col + 1).
        /// </summary>
        HorizontalFlip,

        /// <summary>
        /// Back side is flipped vertically (Row = MaxRow - Row + 1).
        /// </summary>
        VerticalFlip,

        /// <summary>
        /// Back side is rotated 180 degrees relative to front.
        /// </summary>
        Rotate180
    }

    /// <summary>
    /// JSON-serializable profile defining counter grid parameters, independent front/back press bounds,
    /// bleed insets, and naming formatting.
    /// </summary>
    public class SheetProfile
    {
        public string Name { get; set; } = "SPI WorldKiller 1980 (10x10)";

        /// <summary>
        /// Strategy used for slicing: Banded2x10 (discrete 2x10 strips) or UniformGrid (legacy continuous grid).
        /// </summary>
        public GridSlicingMode SlicingMode { get; set; } = GridSlicingMode.Banded2x10;

        // Legacy Uniform Grid fields (preserved for generic sheets and backward compatibility)
        public int Rows { get; set; } = 10;
        public int Columns { get; set; } = 10;

        public GridRelativeBounds FrontBounds { get; set; } = new GridRelativeBounds
        {
            Left = 0.05,
            Top = 0.08,
            Width = 0.90,
            Height = 0.84
        };

        public GridRelativeBounds BackBounds { get; set; } = new GridRelativeBounds
        {
            Left = 0.05,
            Top = 0.08,
            Width = 0.90,
            Height = 0.84
        };

        // Discrete 2x10 bands (independent front and back press alignment)
        public List<Band2x10Definition> FrontBands { get; set; } = new();
        public List<Band2x10Definition> BackBands { get; set; } = new();

        /// <summary>
        /// Inward pixel inset (e.g. 2-6 px at 600 DPI) to prevent adjacent chit artwork or die-cut line bleed.
        /// When locked, represents uniform buffer inset across all sides.
        /// </summary>
        public double InwardInsetPx { get; set; } = 4.0;

        /// <summary>
        /// Individual buffer margin insets in pixels (top, bottom, left, right).
        /// Default to 4.0 px matching InwardInsetPx for seamless backward compatibility.
        /// </summary>
        public double InwardInsetTopPx { get; set; } = 4.0;
        public double InwardInsetBottomPx { get; set; } = 4.0;
        public double InwardInsetLeftPx { get; set; } = 4.0;
        public double InwardInsetRightPx { get; set; } = 4.0;

        /// <summary>
        /// Whether buffer adjustments are locked across all four edges simultaneously (default true).
        /// </summary>
        public bool IsBufferLocked { get; set; } = true;

        /// <summary>
        /// Orientation mapping for the reverse side.
        /// </summary>
        public BackOrientationMode BackOrientation { get; set; } = BackOrientationMode.HorizontalFlip;

        /// <summary>
        /// Naming prefix for sliced PNGs (e.g. "WorldKiller").
        /// </summary>
        public string NamingPrefix { get; set; } = "WorldKiller";

        /// <summary>
        /// Formatting pattern for piece filenames. Tokens: {Prefix}, {Row}, {Col}, {Side}.
        /// </summary>
        public string NamingFormat { get; set; } = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}";

        /// <summary>
        /// Optional base64-encoded PNG swatch of clean counter background texture.
        /// </summary>
        public string? PatternBase64 { get; set; }

        /// <summary>
        /// Size of the pattern capture square (15 to 30 px, default 20).
        /// </summary>
        public int PatternSquareSize { get; set; } = 20;

        /// <summary>
        /// Whether buffer margin is filled with seamless pattern texture instead of cropped.
        /// </summary>
        public bool IsPatternFillEnabled { get; set; } = false;

        public SheetProfile Clone()
        {
            return new SheetProfile
            {
                Name = Name,
                SlicingMode = SlicingMode,
                Rows = Rows,
                Columns = Columns,
                FrontBounds = FrontBounds.Clone(),
                BackBounds = BackBounds.Clone(),
                FrontBands = FrontBands.Select(b => b.Clone()).ToList(),
                BackBands = BackBands.Select(b => b.Clone()).ToList(),
                InwardInsetPx = InwardInsetPx,
                InwardInsetTopPx = InwardInsetTopPx,
                InwardInsetBottomPx = InwardInsetBottomPx,
                InwardInsetLeftPx = InwardInsetLeftPx,
                InwardInsetRightPx = InwardInsetRightPx,
                IsBufferLocked = IsBufferLocked,
                BackOrientation = BackOrientation,
                NamingPrefix = NamingPrefix,
                NamingFormat = NamingFormat,
                PatternBase64 = PatternBase64,
                PatternSquareSize = PatternSquareSize,
                IsPatternFillEnabled = IsPatternFillEnabled
            };
        }
    }
}
