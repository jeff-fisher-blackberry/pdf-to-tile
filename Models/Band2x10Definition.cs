using System;
using System.Collections.Generic;

namespace pdf_to_Tiles.Models
{
    /// <summary>
    /// Specifies the slicing strategy applied to a counter sheet.
    /// </summary>
    public enum GridSlicingMode
    {
        /// <summary>
        /// Default: Slices 2 rows x 10 columns across discrete horizontal bands,
        /// skipping header bars and allowing fine-tuned column dividers within a fixed total width.
        /// </summary>
        Banded2x10,

        /// <summary>
        /// Single continuous rectangular grid of N rows x M columns (legacy / generic sheets).
        /// </summary>
        UniformGrid
    }

    /// <summary>
    /// Represents a discrete 2 rows x 10 columns counter band on a counter sheet.
    /// Anchors the total width across all 10 columns and total height across 2 rows,
    /// with internal dividers for variable column widths and row heights.
    /// </summary>
    public class Band2x10Definition
    {
        public string Name { get; set; } = "Band 1";

        /// <summary>
        /// Normalized outer bounding box (0.0 to 1.0) of the entire 2x10 band.
        /// Anchors the total width across all 10 columns and total height of both rows.
        /// </summary>
        public GridRelativeBounds Bounds { get; set; } = new GridRelativeBounds
        {
            Left = 0.05,
            Top = 0.08,
            Width = 0.90,
            Height = 0.14
        };

        /// <summary>
        /// Vertical split ratio between Row 1 and Row 2 inside this band (default 0.50 = 50% split).
        /// </summary>
        public double RowSplitRatio { get; set; } = 0.50;

        /// <summary>
        /// Default horizontal shift in pixels applied to tiles in this band.
        /// </summary>
        public int ShiftX { get; set; } = 0;

        /// <summary>
        /// Default vertical shift in pixels applied to tiles in this band.
        /// </summary>
        public int ShiftY { get; set; } = 0;

        /// <summary>
        /// Relative column divider positions (0.0 to 1.0 within the band width).
        /// For 10 columns, contains exactly 9 internal dividers: [0.10, 0.20, ..., 0.90].
        /// Allows individual column widths to vary while the outer left and right boundaries remain fixed.
        /// </summary>
        public List<double> ColumnDividers { get; set; } = GetDefaultEvenDividers(10);

        public static List<double> GetDefaultEvenDividers(int columnCount = 10)
        {
            var dividers = new List<double>();
            for (int i = 1; i < columnCount; i++)
            {
                dividers.Add(Math.Round(i / (double)columnCount, 4));
            }
            return dividers;
        }

        public Band2x10Definition Clone()
        {
            return new Band2x10Definition
            {
                Name = Name,
                Bounds = Bounds.Clone(),
                RowSplitRatio = RowSplitRatio,
                ShiftX = ShiftX,
                ShiftY = ShiftY,
                ColumnDividers = new List<double>(ColumnDividers)
            };
        }
    }
}
