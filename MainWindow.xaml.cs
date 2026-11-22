using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using pdf_to_Tiles.Models;
using pdf_to_Tiles.ViewModels;

namespace pdf_to_Tiles
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainViewModel vm)
            {
                vm.GetPdfViewportSize = () => (PdfScrollViewer.ActualWidth, PdfScrollViewer.ActualHeight);
                vm.GetSlicerViewportSize = () => (SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);

                vm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.GridLeftPercent):
                case nameof(MainViewModel.GridTopPercent):
                case nameof(MainViewModel.GridWidthPercent):
                case nameof(MainViewModel.GridHeightPercent):
                case nameof(MainViewModel.ProfileRows):
                case nameof(MainViewModel.ProfileColumns):
                case nameof(MainViewModel.ShowGridOverlay):
                case nameof(MainViewModel.ShowAlignmentGrid):
                case nameof(MainViewModel.SlicerImage):
                case nameof(MainViewModel.SelectedSide):
                case nameof(MainViewModel.ActiveBandIndex):
                case nameof(MainViewModel.ActiveBandLeftPercent):
                case nameof(MainViewModel.ActiveBandTopPercent):
                case nameof(MainViewModel.ActiveBandWidthPercent):
                case nameof(MainViewModel.ActiveBandHeightPercent):
                case nameof(MainViewModel.ActiveBandRowSplitPercent):
                case nameof(MainViewModel.SelectedDividerIndex):
                case nameof(MainViewModel.IsFineTuningDividers):
                case nameof(MainViewModel.PreviewRowIndex):
                case nameof(MainViewModel.PreviewColIndex):
                case nameof(MainViewModel.PreviewChitNumber):
                case nameof(MainViewModel.SelectedProfile):
                    UpdateGridOverlay();
                    break;
            }
        }

        private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsFitToPage)
            {
                vm.RecalculatePdfFitToPage(PdfScrollViewer.ActualWidth, PdfScrollViewer.ActualHeight);
            }
        }

        private void SlicerScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsSlicerFitToPage)
            {
                vm.RecalculateSlicerFitToPage(SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);
            }
        }

        private void SlicerImageDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridOverlay();
            if (DataContext is MainViewModel vm && vm.IsSlicerFitToPage)
            {
                vm.RecalculateSlicerFitToPage(SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);
            }
        }

        private void PdfPageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private void UpdateGridOverlay()
        {
            if (DataContext is not MainViewModel vm || SlicerGridCanvas == null || SlicerImageDisplay == null)
            {
                return;
            }

            SlicerGridCanvas.Children.Clear();

            double imgW = SlicerImageDisplay.ActualWidth;
            double imgH = SlicerImageDisplay.ActualHeight;

            if (imgW <= 0 || imgH <= 0 || vm.SlicerImage == null)
            {
                return;
            }

            // 1. Draw alignment crosshairs if enabled
            if (vm.ShowAlignmentGrid)
            {
                var crosshairBrush = new SolidColorBrush(Color.FromArgb(180, 239, 68, 68)); // Semi-transparent Red
                crosshairBrush.Freeze();

                var hLine = new Line
                {
                    X1 = 0,
                    Y1 = imgH / 2.0,
                    X2 = imgW,
                    Y2 = imgH / 2.0,
                    Stroke = crosshairBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 6, 4 }
                };
                SlicerGridCanvas.Children.Add(hLine);

                var vLine = new Line
                {
                    X1 = imgW / 2.0,
                    Y1 = 0,
                    X2 = imgW / 2.0,
                    Y2 = imgH,
                    Stroke = crosshairBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 6, 4 }
                };
                SlicerGridCanvas.Children.Add(vLine);
            }

            // 2. Draw slicing grid overlay if enabled
            if (vm.ShowGridOverlay)
            {
                if (vm.IsBandedMode)
                {
                    var bands = vm.SelectedSide.Equals("Back", StringComparison.OrdinalIgnoreCase)
                        ? vm.SelectedProfile?.BackBands
                        : vm.SelectedProfile?.FrontBands;

                    if (bands != null && bands.Count > 0)
                    {
                        // First pass: render all inactive bands in subtle cyan
                        var inactiveBrush = new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)); // #38BDF8 Sky 400
                        inactiveBrush.Freeze();

                        for (int b = 0; b < bands.Count; b++)
                        {
                            if (b == vm.ActiveBandIndex) continue;

                            var band = bands[b];
                            double bx = band.Bounds.Left * imgW;
                            double by = band.Bounds.Top * imgH;
                            double bw = band.Bounds.Width * imgW;
                            double bh = band.Bounds.Height * imgH;

                            var inactiveRect = new Rectangle
                            {
                                Width = Math.Max(1, bw),
                                Height = Math.Max(1, bh),
                                Stroke = inactiveBrush,
                                StrokeThickness = 1.5
                            };
                            Canvas.SetLeft(inactiveRect, bx);
                            Canvas.SetTop(inactiveRect, by);
                            SlicerGridCanvas.Children.Add(inactiveRect);

                            // Inactive row divider
                            double splitY = by + (band.RowSplitRatio * bh);
                            var splitLine = new Line
                            {
                                X1 = bx,
                                Y1 = splitY,
                                X2 = bx + bw,
                                Y2 = splitY,
                                Stroke = inactiveBrush,
                                StrokeThickness = 1
                            };
                            SlicerGridCanvas.Children.Add(splitLine);
                        }

                        // Second pass: render the active band on top with bold lines and divider markers
                        int activeIdx = Math.Clamp(vm.ActiveBandIndex, 0, bands.Count - 1);
                        var activeBand = bands[activeIdx];

                        double abx = activeBand.Bounds.Left * imgW;
                        double aby = activeBand.Bounds.Top * imgH;
                        double abw = activeBand.Bounds.Width * imgW;
                        double abh = activeBand.Bounds.Height * imgH;

                        var activeBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // #0284C7 Sky 600
                        activeBrush.Freeze();

                        var activeRect = new Rectangle
                        {
                            Width = Math.Max(1, abw),
                            Height = Math.Max(1, abh),
                            Stroke = activeBrush,
                            StrokeThickness = 3,
                            StrokeDashArray = new DoubleCollection { 6, 3 }
                        };
                        Canvas.SetLeft(activeRect, abx);
                        Canvas.SetTop(activeRect, aby);
                        SlicerGridCanvas.Children.Add(activeRect);

                        // Active row divider
                        double activeSplitY = aby + (activeBand.RowSplitRatio * abh);
                        var activeRowLine = new Line
                        {
                            X1 = abx,
                            Y1 = activeSplitY,
                            X2 = abx + abw,
                            Y2 = activeSplitY,
                            Stroke = activeBrush,
                            StrokeThickness = 2,
                            StrokeDashArray = new DoubleCollection { 4, 3 }
                        };
                        SlicerGridCanvas.Children.Add(activeRowLine);

                        // 9 Column dividers
                        var dividers = activeBand.ColumnDividers ?? Band2x10Definition.GetDefaultEvenDividers(10);
                        var amberBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber 500
                        amberBrush.Freeze();

                        for (int i = 0; i < dividers.Count; i++)
                        {
                            double divX = abx + (dividers[i] * abw);
                            bool isSelectedDivider = vm.IsFineTuningDividers && (i == vm.SelectedDividerIndex);

                            var colLine = new Line
                            {
                                X1 = divX,
                                Y1 = aby,
                                X2 = divX,
                                Y2 = aby + abh,
                                Stroke = isSelectedDivider ? amberBrush : activeBrush,
                                StrokeThickness = isSelectedDivider ? 3 : 1.5,
                                StrokeDashArray = isSelectedDivider ? null : new DoubleCollection { 4, 3 }
                            };
                            SlicerGridCanvas.Children.Add(colLine);
                        }

                        // Preview cell indicator highlight inside active band
                        int pRow = Math.Clamp(vm.PreviewRowIndex, 0, 1);
                        int pCol = Math.Clamp(vm.PreviewColIndex, 0, 9);

                        var divPositions = new List<double> { 0.0 };
                        divPositions.AddRange(dividers);
                        divPositions.Add(1.0);

                        double cellX1 = abx + (divPositions[pCol] * abw);
                        double cellX2 = abx + (divPositions[pCol + 1] * abw);
                        double cellY1 = (pRow == 0 ? aby : activeSplitY);
                        double cellY2 = (pRow == 0 ? activeSplitY : aby + abh);

                        var previewCellBrush = new SolidColorBrush(Color.FromArgb(45, 239, 68, 68)); // Translucent Red
                        previewCellBrush.Freeze();
                        var previewCellBorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Solid Red
                        previewCellBorderBrush.Freeze();

                        var cellHighlight = new Rectangle
                        {
                            Width = Math.Max(1, cellX2 - cellX1),
                            Height = Math.Max(1, cellY2 - cellY1),
                            Fill = previewCellBrush,
                            Stroke = previewCellBorderBrush,
                            StrokeThickness = 2
                        };
                        Canvas.SetLeft(cellHighlight, cellX1);
                        Canvas.SetTop(cellHighlight, cellY1);
                        SlicerGridCanvas.Children.Add(cellHighlight);
                    }
                }
                else if (vm.IsUniformGridMode && vm.ProfileRows > 0 && vm.ProfileColumns > 0)
                {
                    double x = (vm.GridLeftPercent / 100.0) * imgW;
                    double y = (vm.GridTopPercent / 100.0) * imgH;
                    double w = (vm.GridWidthPercent / 100.0) * imgW;
                    double h = (vm.GridHeightPercent / 100.0) * imgH;

                    // Outer bounding box
                    var borderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // #0284C7 Sky 600
                    borderBrush.Freeze();

                    var outerRect = new Rectangle
                    {
                        Width = Math.Max(1, w),
                        Height = Math.Max(1, h),
                        Stroke = borderBrush,
                        StrokeThickness = 3,
                        StrokeDashArray = new DoubleCollection { 6, 3 }
                    };
                    Canvas.SetLeft(outerRect, x);
                    Canvas.SetTop(outerRect, y);
                    SlicerGridCanvas.Children.Add(outerRect);

                    // Internal row and column grid lines
                    var gridLineBrush = new SolidColorBrush(Color.FromArgb(180, 2, 132, 199));
                    gridLineBrush.Freeze();

                    double cellW = w / vm.ProfileColumns;
                    double cellH = h / vm.ProfileRows;

                    for (int c = 1; c < vm.ProfileColumns; c++)
                    {
                        double lineX = x + (c * cellW);
                        var vLine = new Line
                        {
                            X1 = lineX,
                            Y1 = y,
                            X2 = lineX,
                            Y2 = y + h,
                            Stroke = gridLineBrush,
                            StrokeThickness = 1,
                            StrokeDashArray = new DoubleCollection { 4, 3 }
                        };
                        SlicerGridCanvas.Children.Add(vLine);
                    }

                    for (int r = 1; r < vm.ProfileRows; r++)
                    {
                        double lineY = y + (r * cellH);
                        var hLine = new Line
                        {
                            X1 = x,
                            Y1 = lineY,
                            X2 = x + w,
                            Y2 = lineY,
                            Stroke = gridLineBrush,
                            StrokeThickness = 1,
                            StrokeDashArray = new DoubleCollection { 4, 3 }
                        };
                        SlicerGridCanvas.Children.Add(hLine);
                    }
                }
            }
        }
    }
}
