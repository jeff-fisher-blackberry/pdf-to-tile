using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            if (DataContext is MainViewModel vm)
            {
                vm.GetPdfViewportSize = () => (PdfScrollViewer.ActualWidth, PdfScrollViewer.ActualHeight);
                vm.GetSlicerViewportSize = () => (SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);

                vm.PropertyChanged += ViewModel_PropertyChanged;

                SlicerScrollViewer.IsVisibleChanged += (s, e) =>
                {
                    if (SlicerScrollViewer.IsVisible && DataContext is MainViewModel mvm)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (mvm.IsSlicerFitToWidth && SlicerScrollViewer.ActualWidth > 0)
                            {
                                mvm.RecalculateSlicerFitToWidth(SlicerScrollViewer.ActualWidth);
                            }
                            else if (mvm.IsSlicerFitToPage && SlicerScrollViewer.ActualWidth > 0 && SlicerScrollViewer.ActualHeight > 0)
                            {
                                mvm.RecalculateSlicerFitToPage(SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);
                            }
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                };
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

                case nameof(MainViewModel.IsSettingUpperLeftCorner):
                case nameof(MainViewModel.IsSettingLowerRightCorner):
                case nameof(MainViewModel.SelectedTabIndex):
                    if (DataContext is MainViewModel vmTab && vmTab.SelectedTabIndex != 1)
                    {
                        if (vmTab.IsSettingUpperLeftCorner) vmTab.IsSettingUpperLeftCorner = false;
                        if (vmTab.IsSettingLowerRightCorner) vmTab.IsSettingLowerRightCorner = false;
                    }
                    UpdateSlicerCursor();
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
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsSlicerFitToWidth && SlicerScrollViewer.ActualWidth > 0)
                {
                    vm.RecalculateSlicerFitToWidth(SlicerScrollViewer.ActualWidth);
                }
                else if (vm.IsSlicerFitToPage && SlicerScrollViewer.ActualWidth > 0 && SlicerScrollViewer.ActualHeight > 0)
                {
                    vm.RecalculateSlicerFitToPage(SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);
                }
            }
        }

        private void SlicerImageDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridOverlay();
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsSlicerFitToWidth && SlicerScrollViewer.ActualWidth > 0)
                {
                    vm.RecalculateSlicerFitToWidth(SlicerScrollViewer.ActualWidth);
                }
                else if (vm.IsSlicerFitToPage && SlicerScrollViewer.ActualWidth > 0 && SlicerScrollViewer.ActualHeight > 0)
                {
                    vm.RecalculateSlicerFitToPage(SlicerScrollViewer.ActualWidth, SlicerScrollViewer.ActualHeight);
                }
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
                        // Render the single active 2x10 band with bold lines and divider markers
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

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            // Tab 2 (Deskew & Grid Slicer): Escape cancels corner picking
            if (e.Key == Key.Escape && (vm.IsSettingUpperLeftCorner || vm.IsSettingLowerRightCorner))
            {
                vm.IsSettingUpperLeftCorner = false;
                vm.IsSettingLowerRightCorner = false;
                e.Handled = true;
                return;
            }

            // Tab 3 (Chit Editor): Ctrl+X, Ctrl+C, Ctrl+V for working chit
            if (vm.SelectedTabIndex == 2)
            {
                // Do not intercept if user is typing or editing inside a TextBox
                if (e.OriginalSource is TextBox)
                {
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (e.Key == Key.X)
                    {
                        if (vm.CutChitCommand.CanExecute(null))
                        {
                            vm.CutChitCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.C)
                    {
                        if (vm.CopyChitCommand.CanExecute(null))
                        {
                            vm.CopyChitCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.V)
                    {
                        if (vm.PasteChitCommand.CanExecute(null))
                        {
                            vm.PasteChitCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private void LeftSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true; // Enforce axis isolation: Left only responds to Left/Right
            }
        }

        private void WidthSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true; // Enforce axis isolation: Width only responds to Left/Right
            }
        }

        private void TopSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = true; // Enforce axis isolation: Top only responds to Up/Down
                return;
            }

            if (e.Key == Key.Up)
            {
                // Pressing Up moves boundary UP -> Top decreases
                if (sender is Slider slider)
                {
                    slider.Value = Math.Max(slider.Minimum, Math.Round(slider.Value - slider.SmallChange, 1));
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                // Pressing Down moves boundary DOWN -> Top increases
                if (sender is Slider slider)
                {
                    slider.Value = Math.Min(slider.Maximum, Math.Round(slider.Value + slider.SmallChange, 1));
                    e.Handled = true;
                }
            }
        }

        private void HeightSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = true; // Enforce axis isolation: Height only responds to Up/Down
                return;
            }

            if (e.Key == Key.Up)
            {
                // Pressing Up moves bottom boundary UP -> Height decreases
                if (sender is Slider slider)
                {
                    slider.Value = Math.Max(slider.Minimum, Math.Round(slider.Value - slider.SmallChange, 1));
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                // Pressing Down moves bottom boundary DOWN -> Height increases
                if (sender is Slider slider)
                {
                    slider.Value = Math.Min(slider.Maximum, Math.Round(slider.Value + slider.SmallChange, 1));
                    e.Handled = true;
                }
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            if (DataContext is MainViewModel vm && (vm.IsSettingUpperLeftCorner || vm.IsSettingLowerRightCorner))
            {
                vm.IsSettingUpperLeftCorner = false;
                vm.IsSettingLowerRightCorner = false;
            }
        }

        private void UpdateSlicerCursor()
        {
            if (DataContext is MainViewModel vm && (vm.IsSettingUpperLeftCorner || vm.IsSettingLowerRightCorner) && vm.SelectedTabIndex == 1)
            {
                if (SlicerScrollViewer != null) SlicerScrollViewer.Cursor = Cursors.Cross;
                if (SlicerGridCanvas != null) SlicerGridCanvas.Cursor = Cursors.Cross;
                if (SlicerImageDisplay != null) SlicerImageDisplay.Cursor = Cursors.Cross;
            }
            else
            {
                ResetSlicerCursor();
            }
        }

        private void ResetSlicerCursor()
        {
            if (SlicerScrollViewer != null) SlicerScrollViewer.Cursor = Cursors.Arrow;
            if (SlicerGridCanvas != null) SlicerGridCanvas.Cursor = Cursors.Arrow;
            if (SlicerImageDisplay != null) SlicerImageDisplay.Cursor = Cursors.Arrow;
        }

        private void SlicerScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel vm || (!vm.IsSettingUpperLeftCorner && !vm.IsSettingLowerRightCorner))
            {
                return;
            }

            if (SlicerImageDisplay == null || SlicerImageDisplay.ActualWidth <= 0 || SlicerImageDisplay.ActualHeight <= 0 || vm.SlicerPixelWidth <= 0 || vm.SlicerPixelHeight <= 0)
            {
                vm.IsSettingUpperLeftCorner = false;
                vm.IsSettingLowerRightCorner = false;
                return;
            }

            Point clickPos = e.GetPosition(SlicerImageDisplay);
            double imgW = SlicerImageDisplay.ActualWidth;
            double imgH = SlicerImageDisplay.ActualHeight;

            // Only capture clicks within the bounds of the sheet
            if (clickPos.X >= 0 && clickPos.X <= imgW && clickPos.Y >= 0 && clickPos.Y <= imgH)
            {
                int clickPxX = (int)Math.Clamp(Math.Round(clickPos.X / imgW * vm.SlicerPixelWidth), 0, vm.SlicerPixelWidth);
                int clickPxY = (int)Math.Clamp(Math.Round(clickPos.Y / imgH * vm.SlicerPixelHeight), 0, vm.SlicerPixelHeight);

                if (vm.IsSettingUpperLeftCorner)
                {
                    int currentRight = vm.ActiveBandLeftPx + vm.ActiveBandWidthPx;
                    int currentBottom = vm.ActiveBandTopPx + vm.ActiveBandHeightPx;

                    int newLeft = Math.Clamp(clickPxX, 0, Math.Max(0, vm.SlicerPixelWidth - 20));
                    int newTop = Math.Clamp(clickPxY, 0, Math.Max(0, vm.SlicerPixelHeight - 4));

                    vm.ActiveBandLeftPx = newLeft;
                    vm.ActiveBandTopPx = newTop;

                    if (currentRight > newLeft + 20)
                    {
                        vm.ActiveBandWidthPx = currentRight - newLeft;
                    }
                    else
                    {
                        vm.ActiveBandWidthPx = Math.Max(20, Math.Min(vm.ActiveBandWidthPx, vm.SlicerPixelWidth - newLeft));
                    }

                    if (currentBottom > newTop + 4)
                    {
                        vm.ActiveBandHeightPx = currentBottom - newTop;
                    }
                    else
                    {
                        vm.ActiveBandHeightPx = Math.Max(4, Math.Min(vm.ActiveBandHeightPx, vm.SlicerPixelHeight - newTop));
                    }

                    vm.StatusMessage = $"Set upper-left corner to ({vm.ActiveBandLeftPx}, {vm.ActiveBandTopPx}) px. Size: {vm.ActiveBandDimensionsText}.";
                    vm.IsSettingUpperLeftCorner = false;
                    e.Handled = true;
                }
                else if (vm.IsSettingLowerRightCorner)
                {
                    int clickRight = Math.Clamp(clickPxX, 0, vm.SlicerPixelWidth);
                    int clickBottom = Math.Clamp(clickPxY, 0, vm.SlicerPixelHeight);

                    // X axis: if click is to the right of LeftPx, extend width; if to the left, swap!
                    if (clickRight >= vm.ActiveBandLeftPx + 20)
                    {
                        vm.ActiveBandWidthPx = clickRight - vm.ActiveBandLeftPx;
                    }
                    else if (clickRight < vm.ActiveBandLeftPx)
                    {
                        int oldLeft = vm.ActiveBandLeftPx;
                        vm.ActiveBandLeftPx = clickRight;
                        vm.ActiveBandWidthPx = Math.Max(20, oldLeft - clickRight);
                    }
                    else
                    {
                        vm.ActiveBandWidthPx = 20;
                    }

                    // Y axis: if click is below TopPx, extend height; if above, swap!
                    if (clickBottom >= vm.ActiveBandTopPx + 4)
                    {
                        vm.ActiveBandHeightPx = clickBottom - vm.ActiveBandTopPx;
                    }
                    else if (clickBottom < vm.ActiveBandTopPx)
                    {
                        int oldTop = vm.ActiveBandTopPx;
                        vm.ActiveBandTopPx = clickBottom;
                        vm.ActiveBandHeightPx = Math.Max(4, oldTop - clickBottom);
                    }
                    else
                    {
                        vm.ActiveBandHeightPx = 4;
                    }

                    vm.StatusMessage = $"Set lower-right corner to ({vm.ActiveBandRightPx}, {vm.ActiveBandBottomPx}) px. Size: {vm.ActiveBandDimensionsText}.";
                    vm.IsSettingLowerRightCorner = false;
                    e.Handled = true;
                }
            }
        }

        private void ChitPreview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedChit?.WorkingMat != null && !vm.SelectedChit.WorkingMat.IsDisposed)
            {
                if (sender is Image img && img.Source is BitmapSource)
                {
                    Point clickPos = e.GetPosition(img);
                    double actualW = img.ActualWidth;
                    double actualH = img.ActualHeight;

                    if (actualW > 0 && actualH > 0)
                    {
                        int matW = vm.SelectedChit.WorkingMat.Width;
                        int matH = vm.SelectedChit.WorkingMat.Height;

                        int px = (int)Math.Clamp(Math.Floor(clickPos.X * matW / actualW), 0, matW - 1);
                        int py = (int)Math.Clamp(Math.Floor(clickPos.Y * matH / actualH), 0, matH - 1);

                        var indexer = vm.SelectedChit.WorkingMat.GetGenericIndexer<OpenCvSharp.Vec4b>();
                        OpenCvSharp.Vec4b colorVec = indexer[py, px];

                        if (colorVec.Item3 > 0) // if not fully transparent
                        {
                            vm.ChitEdgeCleanColor = Color.FromRgb(colorVec.Item2, colorVec.Item1, colorVec.Item0);
                            vm.LockEdgeCleanColor = true;
                            vm.StatusMessage = $"Sampled background color #{vm.ChitEdgeCleanColor.R:X2}{vm.ChitEdgeCleanColor.G:X2}{vm.ChitEdgeCleanColor.B:X2} at ({px}, {py}).";
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private void ChitEditorZoomTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.ApplyChitEditorZoomInput(textBox.Text);
                    textBox.Text = vm.ChitEditorZoomInputText;
                    textBox.SelectAll();
                }
                e.Handled = true;
            }
        }

        private void ChitEditorZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is MainViewModel vm)
            {
                vm.ApplyChitEditorZoomInput(textBox.Text);
                textBox.Text = vm.ChitEditorZoomInputText;
            }
        }

        private void BandLeftTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandLeftInput(tb.Text);
                tb.Text = vm.ActiveBandLeftText;
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void BandLeftTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandLeftInput(tb.Text);
                tb.Text = vm.ActiveBandLeftText;
            }
        }

        private void BandTopTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandTopInput(tb.Text);
                tb.Text = vm.ActiveBandTopText;
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void BandTopTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandTopInput(tb.Text);
                tb.Text = vm.ActiveBandTopText;
            }
        }

        private void BandWidthTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandWidthInput(tb.Text);
                tb.Text = vm.ActiveBandWidthText;
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void BandWidthTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandWidthInput(tb.Text);
                tb.Text = vm.ActiveBandWidthText;
            }
        }

        private void BandHeightTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandHeightInput(tb.Text);
                tb.Text = vm.ActiveBandHeightText;
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void BandHeightTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandHeightInput(tb.Text);
                tb.Text = vm.ActiveBandHeightText;
            }
        }

        private void BandRowSplitTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandRowSplitInput(tb.Text);
                tb.Text = vm.ActiveBandRowSplitText;
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void BandRowSplitTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                vm.ApplyActiveBandRowSplitInput(tb.Text);
                tb.Text = vm.ActiveBandRowSplitText;
            }
        }
    }
}
