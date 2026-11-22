using System;

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

        /// <summary>
        /// Inward pixel inset (e.g. 2-6 px at 600 DPI) to prevent adjacent chit artwork or die-cut line bleed.
        /// </summary>
        public double InwardInsetPx { get; set; } = 4.0;

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

        public SheetProfile Clone()
        {
            return new SheetProfile
            {
                Name = Name,
                Rows = Rows,
                Columns = Columns,
                FrontBounds = FrontBounds.Clone(),
                BackBounds = BackBounds.Clone(),
                InwardInsetPx = InwardInsetPx,
                BackOrientation = BackOrientation,
                NamingPrefix = NamingPrefix,
                NamingFormat = NamingFormat
            };
        }
    }
}
