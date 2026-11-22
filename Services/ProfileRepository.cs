using System;
using System.Collections.Generic;
using System.IO;
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
                new SheetProfile
                {
                    Name = "SPI WorldKiller 1980 (10x10)",
                    Rows = 10,
                    Columns = 10,
                    FrontBounds = new GridRelativeBounds { Left = 0.05, Top = 0.08, Width = 0.90, Height = 0.84 },
                    BackBounds = new GridRelativeBounds { Left = 0.05, Top = 0.08, Width = 0.90, Height = 0.84 },
                    InwardInsetPx = 4.0,
                    BackOrientation = BackOrientationMode.HorizontalFlip,
                    NamingPrefix = "WorldKiller",
                    NamingFormat = "{Prefix}_R{Row:D2}_C{Col:D2}_{Side}"
                },
                new SheetProfile
                {
                    Name = "Generic 10x10 (Square Chits)",
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
