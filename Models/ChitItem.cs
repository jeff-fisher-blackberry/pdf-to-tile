using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;

namespace pdf_to_Tiles.Models
{
    /// <summary>
    /// Represents an immutable point-in-time snapshot of a chit for per-tile Undo/Redo.
    /// </summary>
    public sealed class ChitSnapshot : IDisposable
    {
        public Mat Mat { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public Color TransparentColor { get; }

        public ChitSnapshot(Mat sourceMat, int width, int height, Color transparentColor)
        {
            Mat = sourceMat.Clone();
            Width = width;
            Height = height;
            TransparentColor = transparentColor;
        }

        public void Dispose()
        {
            if (Mat != null && !Mat.IsDisposed)
            {
                Mat.Dispose();
                Mat = null!;
            }
        }
    }

    /// <summary>
    /// Represents an in-memory wargame counter piece (chit) within the Chit Editor (Tab 3).
    /// Holds 4-channel BGRA matrices with deterministic disposal and per-chit Undo/Redo.
    /// </summary>
    public partial class ChitItem : ObservableObject, IDisposable
    {
        private readonly object _lock = new();

        public int Index { get; set; } // 1..20
        public int Row { get; set; }   // 1..2
        public int Col { get; set; }   // 1..10
        public string Side { get; set; } = "Front";
        public string PieceName { get; set; } = string.Empty;

        public Mat? OriginalMat { get; private set; }
        public Mat? WorkingMat { get; private set; }

        [ObservableProperty]
        private BitmapSource? _previewSource;

        [ObservableProperty]
        private BitmapSource? _thumbnailSource;

        [ObservableProperty]
        private int _pixelWidth;

        [ObservableProperty]
        private int _pixelHeight;

        [ObservableProperty]
        private bool _isDirty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _dimensionsText = string.Empty;

        [ObservableProperty]
        private string _coordinateText = string.Empty;

        [ObservableProperty]
        private bool _canUndo;

        [ObservableProperty]
        private bool _canRedo;

        private readonly List<ChitSnapshot> _history = new();
        private int _historyIndex = -1;
        private const int MaxHistoryDepth = 20;

        public ChitItem(int index, int row, int col, string side, string pieceName, Mat bgraMat)
        {
            Index = index;
            Row = row;
            Col = col;
            Side = side;
            PieceName = pieceName;
            CoordinateText = $"R{row:D2}_C{col:D2}";

            OriginalMat = bgraMat.Clone();
            WorkingMat = bgraMat;

            PixelWidth = WorkingMat.Width;
            PixelHeight = WorkingMat.Height;
            DimensionsText = $"{PixelWidth} × {PixelHeight} px";

            // Baseline snapshot
            _history.Add(new ChitSnapshot(WorkingMat, PixelWidth, PixelHeight, Colors.Transparent));
            _historyIndex = 0;
            UpdateUndoRedoState();

            UpdateDisplayBitmaps();
        }

        /// <summary>
        /// Updates the working matrix and records a snapshot into the Undo history.
        /// </summary>
        public void UpdateWorkingMat(Mat newBgraMat, bool recordHistory, Color transparentColor)
        {
            lock (_lock)
            {
                if (newBgraMat == null || newBgraMat.IsDisposed || newBgraMat.Width <= 0 || newBgraMat.Height <= 0)
                {
                    return;
                }

                if (recordHistory)
                {
                    // Prune any redo branches ahead of current index
                    for (int i = _history.Count - 1; i > _historyIndex; i--)
                    {
                        _history[i].Dispose();
                        _history.RemoveAt(i);
                    }

                    _history.Add(new ChitSnapshot(newBgraMat, newBgraMat.Width, newBgraMat.Height, transparentColor));
                    _historyIndex = _history.Count - 1;

                    // Cap history depth
                    while (_history.Count > MaxHistoryDepth)
                    {
                        _history[0].Dispose();
                        _history.RemoveAt(0);
                        _historyIndex--;
                    }

                    IsDirty = true;
                }

                if (WorkingMat != null && !ReferenceEquals(WorkingMat, newBgraMat) && !WorkingMat.IsDisposed)
                {
                    WorkingMat.Dispose();
                }

                WorkingMat = newBgraMat;
                PixelWidth = WorkingMat.Width;
                PixelHeight = WorkingMat.Height;
                DimensionsText = $"{PixelWidth} × {PixelHeight} px";

                UpdateUndoRedoState();
                UpdateDisplayBitmaps();
            }
        }

        /// <summary>
        /// Reverts the chit to the previous snapshot state.
        /// </summary>
        public void Undo()
        {
            lock (_lock)
            {
                if (_historyIndex > 0)
                {
                    _historyIndex--;
                    ApplySnapshot(_history[_historyIndex]);
                    UpdateUndoRedoState();
                }
            }
        }

        /// <summary>
        /// Reapplies an undone action.
        /// </summary>
        public void Redo()
        {
            lock (_lock)
            {
                if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
                {
                    _historyIndex++;
                    ApplySnapshot(_history[_historyIndex]);
                    UpdateUndoRedoState();
                }
            }
        }

        /// <summary>
        /// Restores the working chit back to its initial baseline slice.
        /// </summary>
        public void ResetToOriginal()
        {
            lock (_lock)
            {
                if (OriginalMat != null && !OriginalMat.IsDisposed)
                {
                    UpdateWorkingMat(OriginalMat.Clone(), recordHistory: true, Colors.Transparent);
                    IsDirty = false;
                }
            }
        }

        private void ApplySnapshot(ChitSnapshot snapshot)
        {
            if (snapshot.Mat == null || snapshot.Mat.IsDisposed) return;

            if (WorkingMat != null && !WorkingMat.IsDisposed)
            {
                WorkingMat.Dispose();
            }

            WorkingMat = snapshot.Mat.Clone();
            PixelWidth = snapshot.Width;
            PixelHeight = snapshot.Height;
            DimensionsText = $"{PixelWidth} × {PixelHeight} px";

            IsDirty = (_historyIndex > 0);
            UpdateDisplayBitmaps();
        }

        private void UpdateUndoRedoState()
        {
            CanUndo = _historyIndex > 0;
            CanRedo = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        }

        public void UpdateDisplayBitmaps()
        {
            if (WorkingMat == null || WorkingMat.IsDisposed || WorkingMat.Width <= 0 || WorkingMat.Height <= 0)
            {
                PreviewSource = null;
                ThumbnailSource = null;
                return;
            }

            PreviewSource = MatToFrozenBitmapSource(WorkingMat);

            // Generate high-speed thumbnail (max 64x64 aspect-scaled)
            int thumbW = 64;
            int thumbH = Math.Max(16, (int)Math.Round((double)WorkingMat.Height * thumbW / WorkingMat.Width));
            using var thumbMat = new Mat();
            Cv2.Resize(WorkingMat, thumbMat, new OpenCvSharp.Size(thumbW, thumbH), 0, 0, InterpolationFlags.Area);
            ThumbnailSource = MatToFrozenBitmapSource(thumbMat);
        }

        /// <summary>
        /// Sets a non-destructive live preview bitmap on the chit without modifying WorkingMat or recording history.
        /// If previewMat is null, resets PreviewSource back to the committed WorkingMat bitmap.
        /// </summary>
        public void SetPreviewMat(Mat? previewMat)
        {
            lock (_lock)
            {
                if (previewMat != null && !previewMat.IsDisposed && previewMat.Width > 0 && previewMat.Height > 0)
                {
                    PreviewSource = MatToFrozenBitmapSource(previewMat);
                }
                else if (WorkingMat != null && !WorkingMat.IsDisposed && WorkingMat.Width > 0 && WorkingMat.Height > 0)
                {
                    PreviewSource = MatToFrozenBitmapSource(WorkingMat);
                }
                else
                {
                    PreviewSource = null;
                }
            }
        }

        private static BitmapSource MatToFrozenBitmapSource(Mat bgraMat)
        {
            int width = bgraMat.Width;
            int height = bgraMat.Height;
            int stride = (int)bgraMat.Step();

            var bs = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                bgraMat.Data,
                stride * height,
                stride);

            bs.Freeze();
            return bs;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var snap in _history)
                {
                    snap.Dispose();
                }
                _history.Clear();
                _historyIndex = -1;

                if (WorkingMat != null && !WorkingMat.IsDisposed)
                {
                    WorkingMat.Dispose();
                    WorkingMat = null;
                }

                if (OriginalMat != null && !OriginalMat.IsDisposed)
                {
                    OriginalMat.Dispose();
                    OriginalMat = null;
                }
            }
        }
    }
}
