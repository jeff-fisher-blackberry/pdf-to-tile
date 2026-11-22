using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using pdf_to_Tiles.Models;

namespace pdf_to_Tiles.Services
{
    /// <summary>
    /// Manages built-in preset sheet profiles and user-saved custom profiles.
    /// </summary>
    public static class ProfileRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static List<SheetProfile> GetDefaultProfiles()
        {
            return new List<SheetProfile>
            {
                CreateWorldKillerProfile(),
                new SheetProfile
                {
                    Name = "Generic 10x10 (Square Chits)",
                    SlicingMode = GridSlicingMode.UniformGrid,
                    Rows = 10,
                    Columns = 10,
                    FrontBounds = new GridRelativeBounds { Left = 0.05, Top = 0.05, Width = 0.90, Height = 0.90 },
                    BackBounds = new GridRelativeBounds { Left = 0.05, Top = 0.05, Width = 0.90, Height = 0.90 },
                    InwardInsetPx = 3.0,
                    BackOrientation = BackOrientationMode.HorizontalFlip,
                    NamingPrefix = "Counter",
                    NamingFormat = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}"
                },
                new SheetProfile
                {
                    Name = "Generic 12x15 (Rectangular Sheet)",
                    SlicingMode = GridSlicingMode.UniformGrid,
                    Rows = 12,
                    Columns = 15,
                    FrontBounds = new GridRelativeBounds { Left = 0.04, Top = 0.05, Width = 0.92, Height = 0.90 },
                    BackBounds = new GridRelativeBounds { Left = 0.04, Top = 0.05, Width = 0.92, Height = 0.90 },
                    InwardInsetPx = 3.0,
                    BackOrientation = BackOrientationMode.HorizontalFlip,
                    NamingPrefix = "Counter",
                    NamingFormat = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}"
                },
                new SheetProfile
                {
                    Name = "Generic 8x8 (Large Chits)",
                    SlicingMode = GridSlicingMode.UniformGrid,
                    Rows = 8,
                    Columns = 8,
                    FrontBounds = new GridRelativeBounds { Left = 0.06, Top = 0.06, Width = 0.88, Height = 0.88 },
                    BackBounds = new GridRelativeBounds { Left = 0.06, Top = 0.06, Width = 0.88, Height = 0.88 },
                    InwardInsetPx = 4.0,
                    BackOrientation = BackOrientationMode.HorizontalFlip,
                    NamingPrefix = "Counter",
                    NamingFormat = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}"
                }
            };
        }

        private static SheetProfile CreateWorldKillerProfile()
        {
            var frontBands = new List<Band2x10Definition>
            {
                new Band2x10Definition
                {
                    Name = "Band 1: Red Intruders (Rows 1-2)",
                    Bounds = new GridRelativeBounds { Left = 0.050, Top = 0.065, Width = 0.921, Height = 0.142 },
                    RowSplitRatio = 0.50,
                    ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10)
                },
                new Band2x10Definition
                {
                    Name = "Band 2: White Intruders & Shields (Rows 3-4)",
                    Bounds = new GridRelativeBounds { Left = 0.050, Top = 0.211, Width = 0.921, Height = 0.142 },
                    RowSplitRatio = 0.50,
                    ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10)
                },
                new Band2x10Definition
                {
                    Name = "Band 3: Damage Markers 3 & 5 (Rows 5-6)",
                    Bounds = new GridRelativeBounds { Left = 0.050, Top = 0.380, Width = 0.921, Height = 0.142 },
                    RowSplitRatio = 0.50,
                    ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10)
                },
                new Band2x10Definition
                {
                    Name = "Band 4: Damage 7-11 & Blue Units (Rows 7-8)",
                    Bounds = new GridRelativeBounds { Left = 0.050, Top = 0.536, Width = 0.921, Height = 0.142 },
                    RowSplitRatio = 0.50,
                    ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10)
                },
                new Band2x10Definition
                {
                    Name = "Band 5: Blue Planetary Forces (Rows 9-10)",
                    Bounds = new GridRelativeBounds { Left = 0.050, Top = 0.702, Width = 0.921, Height = 0.142 },
                    RowSplitRatio = 0.50,
                    ColumnDividers = Band2x10Definition.GetDefaultEvenDividers(10)
                }
            };

            var backBands = frontBands.Select(b =>
            {
                var copy = b.Clone();
                copy.Name = copy.Name.Replace("Front", "Back");
                return copy;
            }).ToList();

            return new SheetProfile
            {
                Name = "SPI WorldKiller 1980 (10x10)",
                SlicingMode = GridSlicingMode.Banded2x10,
                Rows = 10,
                Columns = 10,
                FrontBounds = new GridRelativeBounds { Left = 0.05, Top = 0.065, Width = 0.921, Height = 0.78 },
                BackBounds = new GridRelativeBounds { Left = 0.05, Top = 0.065, Width = 0.921, Height = 0.78 },
                FrontBands = frontBands,
                BackBands = backBands,
                InwardInsetPx = 4.0,
                BackOrientation = BackOrientationMode.HorizontalFlip,
                NamingPrefix = "WorldKiller",
                NamingFormat = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}"
            };
        }

        public static void SaveProfileToFile(SheetProfile profile, string filePath)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string json = JsonSerializer.Serialize(profile, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        public static SheetProfile? LoadProfileFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SheetProfile>(json, JsonOptions);
        }
    }
}
