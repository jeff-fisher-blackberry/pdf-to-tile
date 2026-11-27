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
            PreviewKeyUp += MainWindow_PreviewKeyUp;

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
                    ResetCornerBoundsPass();
                    UpdateGridOverlay();
                    break;
                case nameof(MainViewModel.BandBoundingBoxHeader):
                    ResetCornerBoundsPass();
                    UpdateGridOverlay();
                    break;
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
                case nameof(MainViewModel.EffectiveBandRows):
                case nameof(MainViewModel.EffectiveBandColumns):
                case nameof(MainViewModel.IsDoubleWidthColumns):
                case nameof(MainViewModel.SlicerOverlayColor):
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

                case nameof(MainViewModel.CloneAnchorX):
                case nameof(MainViewModel.CloneAnchorY):
                case nameof(MainViewModel.HasCloneAnchor):
                case nameof(MainViewModel.HasDestinationAnchor):
                case nameof(MainViewModel.CloneStampRadius):
                case nameof(MainViewModel.CloneStampShape):
                case nameof(MainViewModel.CloneMode):
                case nameof(MainViewModel.SelectedChit):
                    UpdateWorkingChitOverlay();
                    break;

                case nameof(MainViewModel.IsCloneStampActive):
                    if (DataContext is MainViewModel vmClone && !vmClone.IsCloneStampActive)
                    {
                        _isCloning = false;
                        _hasModifiedInStroke = false;
                        _currentStrokePoints.Clear();
                    }
                    UpdateWorkingChitOverlay();
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
                if (DataContext is MainViewModel vm)
                {
                    vm.HasExplicitlyPickedPage = true;
                }
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private void BandDimensionTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.SetBandDimensionsCommand.Execute(null);
                }
                e.Handled = true;
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
                    var bands = MainViewModel.IsRearSide(vm.SelectedSide)
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

                        var activeBrush = new SolidColorBrush(vm.SlicerOverlayColor);
                        activeBrush.Freeze();

                        var activeRect = new Rectangle
                        {
                            Width = Math.Max(1, abw),
                            Height = Math.Max(1, abh),
                            Stroke = activeBrush,
                            StrokeThickness = 6,
                            StrokeDashArray = new DoubleCollection { 6, 3 }
                        };
                        Canvas.SetLeft(activeRect, abx);
                        Canvas.SetTop(activeRect, aby);
                        SlicerGridCanvas.Children.Add(activeRect);

                        // Active row dividers
                        int numRows = vm.EffectiveBandRows;
                        int numCols = vm.EffectiveBandColumns;

                        if (numRows == 2)
                        {
                            double activeSplitY = aby + (activeBand.RowSplitRatio * abh);
                            var activeRowLine = new Line
                            {
                                X1 = abx,
                                Y1 = activeSplitY,
                                X2 = abx + abw,
                                Y2 = activeSplitY,
                                Stroke = activeBrush,
                                StrokeThickness = 4,
                                StrokeDashArray = new DoubleCollection { 4, 3 }
                            };
                            SlicerGridCanvas.Children.Add(activeRowLine);
                        }
                        else if (numRows > 1)
                        {
                            for (int r = 1; r < numRows; r++)
                            {
                                double splitY = aby + (abh * (r / (double)numRows));
                                var activeRowLine = new Line
                                {
                                    X1 = abx,
                                    Y1 = splitY,
                                    X2 = abx + abw,
                                    Y2 = splitY,
                                    Stroke = activeBrush,
                                    StrokeThickness = 4,
                                    StrokeDashArray = new DoubleCollection { 4, 3 }
                                };
                                SlicerGridCanvas.Children.Add(activeRowLine);
                            }
                        }

                        // Column dividers
                        List<double> dividers;
                        if (numCols == 10 && activeBand.ColumnDividers != null && activeBand.ColumnDividers.Count == 9)
                        {
                            dividers = activeBand.ColumnDividers;
                        }
                        else
                        {
                            dividers = Band2x10Definition.GetDefaultEvenDividers(numCols);
                        }

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
                                StrokeThickness = isSelectedDivider ? 6 : 3,
                                StrokeDashArray = isSelectedDivider ? null : new DoubleCollection { 4, 3 }
                            };
                            SlicerGridCanvas.Children.Add(colLine);
                        }

                        // Preview cell indicator highlight inside active band
                        int pRow = Math.Clamp(vm.PreviewRowIndex, 0, Math.Max(0, numRows - 1));
                        int pCol = Math.Clamp(vm.PreviewColIndex, 0, Math.Max(0, numCols - 1));

                        var divPositions = new List<double> { 0.0 };
                        divPositions.AddRange(dividers);
                        divPositions.Add(1.0);

                        double cellX1 = abx + (divPositions[Math.Min(pCol, divPositions.Count - 2)] * abw);
                        double cellX2 = abx + (divPositions[Math.Min(pCol + 1, divPositions.Count - 1)] * abw);

                        double cellY1, cellY2;
                        if (numRows == 2)
                        {
                            double activeSplitY = aby + (activeBand.RowSplitRatio * abh);
                            cellY1 = (pRow == 0 ? aby : activeSplitY);
                            cellY2 = (pRow == 0 ? activeSplitY : aby + abh);
                        }
                        else
                        {
                            cellY1 = aby + (abh * (pRow / (double)numRows));
                            cellY2 = aby + (abh * ((pRow + 1) / (double)numRows));
                        }

                        var previewCellBrush = new SolidColorBrush(Color.FromArgb(55, vm.SlicerOverlayColor.R, vm.SlicerOverlayColor.G, vm.SlicerOverlayColor.B));
                        previewCellBrush.Freeze();
                        var previewCellBorderBrush = new SolidColorBrush(vm.SlicerOverlayColor);
                        previewCellBorderBrush.Freeze();

                        var cellHighlight = new Rectangle
                        {
                            Width = Math.Max(1, cellX2 - cellX1),
                            Height = Math.Max(1, cellY2 - cellY1),
                            Fill = previewCellBrush,
                            Stroke = previewCellBorderBrush,
                            StrokeThickness = 4
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
                    var borderBrush = new SolidColorBrush(vm.SlicerOverlayColor);
                    borderBrush.Freeze();

                    var outerRect = new Rectangle
                    {
                        Width = Math.Max(1, w),
                        Height = Math.Max(1, h),
                        Stroke = borderBrush,
                        StrokeThickness = 6,
                        StrokeDashArray = new DoubleCollection { 6, 3 }
                    };
                    Canvas.SetLeft(outerRect, x);
                    Canvas.SetTop(outerRect, y);
                    SlicerGridCanvas.Children.Add(outerRect);

                    // Internal row and column grid lines
                    var gridLineBrush = new SolidColorBrush(Color.FromArgb(180, vm.SlicerOverlayColor.R, vm.SlicerOverlayColor.G, vm.SlicerOverlayColor.B));
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
                            StrokeThickness = 2,
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
                            StrokeThickness = 2,
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

            // Clone Stamp anchor setting preview: Shift key hides stamp footprint to show pure crosshair
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (vm.IsCloneStampActive)
                {
                    vm.IsCloneHovering = false;
                    UpdateWorkingChitOverlay();
                }
            }

            // Global Help shortcut: F1
            if (e.Key == Key.F1)
            {
                if (vm.ShowShortcutsCommand.CanExecute(null))
                {
                    vm.ShowShortcutsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // Escape key handling
            if (e.Key == Key.Escape)
            {
                // If Clone Stamp is active, untoggle it cleanly
                if (vm.IsCloneStampActive)
                {
                    vm.DeactivateCloneStamp();
                    e.Handled = true;
                    return;
                }

                // Tab 2 (Deskew & Grid Slicer): Escape cancels corner picking
                if (vm.IsSettingUpperLeftCorner || vm.IsSettingLowerRightCorner)
                {
                    vm.IsSettingUpperLeftCorner = false;
                    vm.IsSettingLowerRightCorner = false;
                    ResetCornerBoundsPass();
                    e.Handled = true;
                    return;
                }
            }

            // Mode switching shortcuts: Ctrl+1, Ctrl+2, Ctrl+3
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.D1 || e.Key == Key.NumPad1)
                {
                    vm.SelectedTabIndex = 0;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.D2 || e.Key == Key.NumPad2)
                {
                    vm.SelectedTabIndex = 1;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.D3 || e.Key == Key.NumPad3)
                {
                    vm.SelectedTabIndex = 2;
                    e.Handled = true;
                    return;
                }
            }

            // If user is focused on or typing inside a TextBox, protect TextBox clipboard & editing operations!
            bool isTextBoxFocused = e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox;

            // Global Document / File accelerators
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (e.Key == Key.O)
                {
                    if (isShift)
                    {
                        if (vm.OpenImageForSlicingCommand.CanExecute(null))
                        {
                            vm.OpenImageForSlicingCommand.Execute(null);
                            e.Handled = true;
                            return;
                        }
                    }
                    else
                    {
                        if (vm.OpenPdfCommand.CanExecute(null))
                        {
                            vm.OpenPdfCommand.Execute(null);
                            e.Handled = true;
                            return;
                        }
                    }
                }
                else if (e.Key == Key.W)
                {
                    if (vm.SelectedTabIndex == 0 && vm.ClosePdfCommand.CanExecute(null))
                    {
                        vm.ClosePdfCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                    else if (vm.SelectedTabIndex == 1 && vm.CloseTilePageImageCommand.CanExecute(null))
                    {
                        vm.CloseTilePageImageCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                    else if (vm.SelectedTabIndex == 2 && vm.CloseTilesCommand.CanExecute(null))
                    {
                        vm.CloseTilesCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.E)
                {
                    if (vm.ExportPageToPngCommand.CanExecute(null))
                    {
                        vm.ExportPageToPngCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.S && isShift)
                {
                    if (vm.ExportAllEditedChitsCommand.CanExecute(null))
                    {
                        vm.ExportAllEditedChitsCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Tab 3 (Chit Editor): Clipboard & Undo/Redo operations for working chit
            // CRITICAL: NEVER intercept if user is typing or editing inside a TextBox
            if (vm.SelectedTabIndex == 2 && !isTextBoxFocused)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (e.Key == Key.Z)
                    {
                        if (vm.UndoChitCommand.CanExecute(null))
                        {
                            vm.UndoChitCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.Y)
                    {
                        if (vm.RedoChitCommand.CanExecute(null))
                        {
                            vm.RedoChitCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.X)
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
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                        {
                            if (vm.PasteImageToNewTileCommand.CanExecute(null))
                            {
                                vm.PasteImageToNewTileCommand.Execute(null);
                                e.Handled = true;
                            }
                        }
                        else
                        {
                            if (vm.PasteImageToTileCommand.CanExecute(null))
                            {
                                vm.PasteImageToTileCommand.Execute(null);
                                e.Handled = true;
                            }
                        }
                    }
                }

                if (e.Key == Key.Delete)
                {
                    if (vm.DeleteSelectedTilesCommand.CanExecute(null))
                    {
                        vm.DeleteSelectedTilesCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                    else if (vm.DeleteCurrentTileCommand.CanExecute(null))
                    {
                        vm.DeleteCurrentTileCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (DataContext is MainViewModel vm && vm.IsCloneStampActive)
                {
                    bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    vm.IsCloneHovering = !isShift && vm.HasCloneAnchor;
                    UpdateWorkingChitOverlay();
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

        private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateClipboardState();
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (DataContext is MainViewModel vm && vm.SelectedTabIndex == 2)
            {
                vm.UpdateClipboardState();
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            if (DataContext is MainViewModel vm && (vm.IsSettingUpperLeftCorner || vm.IsSettingLowerRightCorner))
            {
                vm.IsSettingUpperLeftCorner = false;
                vm.IsSettingLowerRightCorner = false;
                ResetCornerBoundsPass();
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
                    _hasSetUpperLeftInPass = true;
                    if (_hasSetUpperLeftInPass && _hasSetLowerRightInPass)
                    {
                        vm.RecalculateRowSplitToFiftyPercent();
                        ResetCornerBoundsPass();
                    }
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
                    _hasSetLowerRightInPass = true;
                    if (_hasSetUpperLeftInPass && _hasSetLowerRightInPass)
                    {
                        vm.RecalculateRowSplitToFiftyPercent();
                        ResetCornerBoundsPass();
                    }
                    e.Handled = true;
                }
            }
        }

        private bool _hasSetUpperLeftInPass = false;
        private bool _hasSetLowerRightInPass = false;

        private void ResetCornerBoundsPass()
        {
            _hasSetUpperLeftInPass = false;
            _hasSetLowerRightInPass = false;
        }

        private bool _isCloning = false;
        private bool _hasModifiedInStroke = false;
        private int _cloneDeltaX = 0;
        private int _cloneDeltaY = 0;
        private readonly List<Point> _currentStrokePoints = new();

        private void ChitPreview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedChit?.WorkingMat == null || vm.SelectedChit.WorkingMat.IsDisposed)
            {
                return;
            }

            if (sender is not Image img || img.Source is not BitmapSource)
            {
                return;
            }

            Point clickPos = e.GetPosition(img);
            double actualW = img.ActualWidth;
            double actualH = img.ActualHeight;

            if (actualW <= 0 || actualH <= 0) return;

            int matW = vm.SelectedChit.WorkingMat.Width;
            int matH = vm.SelectedChit.WorkingMat.Height;
            if (matW < 8 || matH < 8) return;

            int px = (int)Math.Clamp(Math.Floor(clickPos.X * matW / actualW), 0, matW - 1);
            int py = (int)Math.Clamp(Math.Floor(clickPos.Y * matH / actualH), 0, matH - 1);

            // Paint.NET Clone Stamp Tool handling
            if (vm.IsCloneStampActive)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (isShift || !vm.HasCloneAnchor || vm.IsPickingCloneAnchor)
                {
                    // Set Source Anchor (Shift+Click or first click when no anchor set yet)
                    vm.CloneAnchorX = px;
                    vm.CloneAnchorY = py;
                    vm.HasCloneAnchor = true;
                    vm.HasDestinationAnchor = false; // Reset lockstep alignment so next target click defines alignment
                    vm.CloneOffsetDeltaX = 0;
                    vm.CloneOffsetDeltaY = 0;
                    vm.IsPickingCloneAnchor = false;
                    if (vm.IsCloneModeAnchored)
                    {
                        vm.CaptureCloneAnchorSourceMat();
                    }
                    string modeDesc = vm.IsCloneModeAnchored ? "Anchored (fixed source)" : "Floating (lockstep)";
                    vm.StatusMessage = $"Clone source anchor set at ({px}, {py}) [{modeDesc}]. Click or drag on target to stamp; Shift+Click to move anchor.";
                    UpdateWorkingChitOverlay();
                    e.Handled = true;
                    return;
                }

                // If anchor is not set yet, prompt user
                if (!vm.HasCloneAnchor)
                {
                    vm.StatusMessage = "Please set a clone source anchor first (Shift + Click on canvas).";
                    e.Handled = true;
                    return;
                }

                // If in Anchored mode, stamp the fixed source patch onto target directly
                if (vm.IsCloneModeAnchored)
                {
                    _isCloning = true;
                    _hasModifiedInStroke = false;
                    _currentStrokePoints.Clear();
                    img.CaptureMouse();

                    vm.CloneHoverDstX = px;
                    vm.CloneHoverDstY = py;
                    vm.IsCloneHovering = true;

                    var srcMat = vm.GetCloneAnchorSourceMat() ?? vm.SelectedChit.WorkingMat;
                    bool mod = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStampAnchored(
                        vm.SelectedChit.WorkingMat,
                        px, py,
                        vm.CloneAnchorX, vm.CloneAnchorY,
                        srcMat,
                        vm.CloneStampRadius,
                        vm.ActiveCloneStampShape);

                    if (mod)
                    {
                        _hasModifiedInStroke = true;
                        _currentStrokePoints.Add(new Point(px, py));
                        vm.SelectedChit.UpdateDisplayBitmaps();
                    }

                    UpdateWorkingChitOverlay();
                    e.Handled = true;
                    return;
                }

                // If this is the first target click since setting source anchor in Floating mode, record destination anchor and compute lockstep offset!
                if (!vm.HasDestinationAnchor)
                {
                    vm.CloneDstAnchorX = px;
                    vm.CloneDstAnchorY = py;
                    vm.CloneOffsetDeltaX = vm.CloneAnchorX - px;
                    vm.CloneOffsetDeltaY = vm.CloneAnchorY - py;
                    vm.HasDestinationAnchor = true;
                }

                _cloneDeltaX = vm.CloneOffsetDeltaX;
                _cloneDeltaY = vm.CloneOffsetDeltaY;
                _isCloning = true;
                _hasModifiedInStroke = false;
                _currentStrokePoints.Clear();
                img.CaptureMouse();

                vm.CloneHoverDstX = px;
                vm.CloneHoverDstY = py;
                vm.IsCloneHovering = true;

                bool modFloating = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStamp(
                    vm.SelectedChit.WorkingMat,
                    px, py,
                    _cloneDeltaX, _cloneDeltaY,
                    vm.CloneStampRadius,
                    vm.ActiveCloneStampShape);

                if (modFloating)
                {
                    _hasModifiedInStroke = true;
                    _currentStrokePoints.Add(new Point(px, py));
                    vm.SelectedChit.UpdateDisplayBitmaps();
                }

                UpdateWorkingChitOverlay();
                e.Handled = true;
                return;
            }

            // Normal Edge Cleanup Background Color Sampling
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

        private void ChitPreview_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedChit?.WorkingMat == null || vm.SelectedChit.WorkingMat.IsDisposed)
            {
                return;
            }

            if (sender is not Image img || img.Source is not BitmapSource)
            {
                return;
            }

            Point clickPos = e.GetPosition(img);
            double actualW = img.ActualWidth;
            double actualH = img.ActualHeight;

            if (actualW <= 0 || actualH <= 0) return;

            int matW = vm.SelectedChit.WorkingMat.Width;
            int matH = vm.SelectedChit.WorkingMat.Height;
            if (matW < 8 || matH < 8) return;

            int px = (int)Math.Clamp(Math.Floor(clickPos.X * matW / actualW), 0, matW - 1);
            int py = (int)Math.Clamp(Math.Floor(clickPos.Y * matH / actualH), 0, matH - 1);

            if (vm.IsCloneStampActive)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                vm.CloneHoverDstX = px;
                vm.CloneHoverDstY = py;

                // When setting anchor (Shift held or no anchor set yet), show clean crosshair without stamp footprint
                if (isShift || !vm.HasCloneAnchor)
                {
                    vm.IsCloneHovering = false;
                }
                else
                {
                    vm.IsCloneHovering = true;
                }

                if (_isCloning && e.LeftButton == MouseButtonState.Pressed && vm.HasCloneAnchor)
                {
                    Point pt = new Point(px, py);
                    if (_currentStrokePoints.Count == 0 || _currentStrokePoints[^1] != pt)
                    {
                        bool mod;
                        if (vm.IsCloneModeAnchored)
                        {
                            var srcMat = vm.GetCloneAnchorSourceMat() ?? vm.SelectedChit.WorkingMat;
                            mod = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStampAnchored(
                                vm.SelectedChit.WorkingMat,
                                px, py,
                                vm.CloneAnchorX, vm.CloneAnchorY,
                                srcMat,
                                vm.CloneStampRadius,
                                vm.ActiveCloneStampShape);
                        }
                        else
                        {
                            mod = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStamp(
                                vm.SelectedChit.WorkingMat,
                                px, py,
                                _cloneDeltaX, _cloneDeltaY,
                                vm.CloneStampRadius,
                                vm.ActiveCloneStampShape);
                        }

                        if (mod)
                        {
                            _hasModifiedInStroke = true;
                            _currentStrokePoints.Add(pt);
                            vm.SelectedChit.UpdateDisplayBitmaps();
                        }
                    }
                }

                UpdateWorkingChitOverlay();
            }
        }

        private void ChitPreview_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsCloneStampActive)
            {
                vm.IsCloneHovering = false;
                UpdateWorkingChitOverlay();
            }
        }

        private void ChitPreview_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isCloning)
            {
                _isCloning = false;
                if (sender is Image img)
                {
                    img.ReleaseMouseCapture();
                }

                if (DataContext is MainViewModel vm && vm.SelectedChit?.WorkingMat != null && !vm.SelectedChit.WorkingMat.IsDisposed)
                {
                    if (_hasModifiedInStroke)
                    {
                        vm.SelectedChit.CommitInPlaceEdit(vm.TransparentColor);

                        var affected = new List<pdf_to_Tiles.Models.ChitItem> { vm.SelectedChit };
                        if (vm.SelectedChit.IsSelected)
                        {
                            var otherChits = vm.LoadedChits.Where(c => c != vm.SelectedChit && c.IsSelected).ToList();
                            bool isBatch = otherChits.Count > 0 && _currentStrokePoints.Count > 0;
                            if (isBatch)
                            {
                                foreach (var chit in otherChits)
                                {
                                    if (chit.WorkingMat == null || chit.WorkingMat.IsDisposed) continue;

                                    bool anyMod = false;
                                    foreach (var pt in _currentStrokePoints)
                                    {
                                        bool mod;
                                        if (vm.IsCloneModeAnchored)
                                        {
                                            var srcMat = vm.GetCloneAnchorSourceMat() ?? chit.WorkingMat;
                                            mod = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStampAnchored(
                                                chit.WorkingMat,
                                                (int)pt.X, (int)pt.Y,
                                                vm.CloneAnchorX, vm.CloneAnchorY,
                                                srcMat,
                                                vm.CloneStampRadius,
                                                vm.ActiveCloneStampShape);
                                        }
                                        else
                                        {
                                            mod = pdf_to_Tiles.Services.ChitEditorService.ApplyCloneStamp(
                                                chit.WorkingMat,
                                                (int)pt.X, (int)pt.Y,
                                                _cloneDeltaX, _cloneDeltaY,
                                                vm.CloneStampRadius,
                                                vm.ActiveCloneStampShape);
                                        }
                                        if (mod) anyMod = true;
                                    }

                                    if (anyMod)
                                    {
                                        chit.CommitInPlaceEdit(vm.TransparentColor);
                                        affected.Add(chit);
                                    }
                                }
                            }
                        }

                        vm.RecordEditHistory(affected);
                        string shapeName = vm.CloneStampShape == "Square" ? "Square" : "Circle";
                        if (!vm.SelectedChit.IsSelected)
                        {
                            vm.StatusMessage = $"Applied {shapeName} clone stamp ({vm.CloneStampRadius * 2}px) to active tile #{vm.SelectedChit.Index} (excluded from batch replay).";
                        }
                        else if (affected.Count > 1)
                        {
                            vm.StatusMessage = affected.Count == vm.LoadedChits.Count
                                ? $"Applied {shapeName} clone stamp across all {vm.LoadedChits.Count} tiles."
                                : $"Applied {shapeName} clone stamp across {affected.Count} selected tiles.";
                        }
                        else
                        {
                            vm.StatusMessage = $"Applied {shapeName} clone stamp ({vm.CloneStampRadius * 2}px).";
                        }
                        _hasModifiedInStroke = false;
                        _currentStrokePoints.Clear();
                    }
                }
                UpdateWorkingChitOverlay();
            }
        }

        private void UpdateWorkingChitOverlay()
        {
            if (WorkingChitOverlayCanvas == null || DataContext is not MainViewModel vm) return;
            WorkingChitOverlayCanvas.Children.Clear();

            if (!vm.IsCloneStampActive || !vm.HasCloneAnchor || vm.SelectedChit?.WorkingMat == null || vm.SelectedChit.WorkingMat.IsDisposed)
            {
                return;
            }

            double r = vm.CloneStampRadius;
            double size = Math.Max(2, r * 2);
            bool isSquare = vm.ActiveCloneStampShape == pdf_to_Tiles.Services.CloneStampShape.Square;

            var amberBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber 500
            amberBrush.Freeze();
            var amberFill = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
            amberFill.Freeze();

            var blueBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // Sky 600
            blueBrush.Freeze();
            var blueFill = new SolidColorBrush(Color.FromArgb(40, 2, 132, 199));
            blueFill.Freeze();

            // 1. Draw static anchor reference indicator at (CloneAnchorX, CloneAnchorY)
            double ax = vm.CloneAnchorX;
            double ay = vm.CloneAnchorY;

            Shape anchorShape = isSquare
                ? new Rectangle { Width = size, Height = size, Stroke = amberBrush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 } }
                : new Ellipse { Width = size, Height = size, Stroke = amberBrush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 } };

            Canvas.SetLeft(anchorShape, ax - r);
            Canvas.SetTop(anchorShape, ay - r);
            WorkingChitOverlayCanvas.Children.Add(anchorShape);

            // Anchor center crosshair
            WorkingChitOverlayCanvas.Children.Add(new Line { X1 = ax - (r + 3), Y1 = ay, X2 = ax + (r + 3), Y2 = ay, Stroke = Brushes.White, StrokeThickness = 1 });
            WorkingChitOverlayCanvas.Children.Add(new Line { X1 = ax, Y1 = ay - (r + 3), X2 = ax, Y2 = ay + (r + 3), Stroke = Brushes.White, StrokeThickness = 1 });

            // 2. If hovering or dragging with an active destination lockstep alignment, render shifting source and target brush
            if (vm.IsCloneHovering && vm.CloneHoverDstX >= 0 && vm.CloneHoverDstY >= 0)
            {
                double hx = vm.CloneHoverDstX;
                double hy = vm.CloneHoverDstY;

                // Target brush indicator (Sky Blue)
                Shape targetBrushShape = isSquare
                    ? new Rectangle { Width = size, Height = size, Stroke = blueBrush, Fill = blueFill, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } }
                    : new Ellipse { Width = size, Height = size, Stroke = blueBrush, Fill = blueFill, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } };

                Canvas.SetLeft(targetBrushShape, hx - r);
                Canvas.SetTop(targetBrushShape, hy - r);
                WorkingChitOverlayCanvas.Children.Add(targetBrushShape);

                WorkingChitOverlayCanvas.Children.Add(new Line { X1 = hx - (r + 2), Y1 = hy, X2 = hx + (r + 2), Y2 = hy, Stroke = blueBrush, StrokeThickness = 1 });
                WorkingChitOverlayCanvas.Children.Add(new Line { X1 = hx, Y1 = hy - (r + 2), X2 = hx, Y2 = hy + (r + 2), Stroke = blueBrush, StrokeThickness = 1 });

                // If Floating mode and lockstep alignment is established, also render shifting source indicator
                if (!vm.IsCloneModeAnchored && vm.HasDestinationAnchor)
                {
                    double shiftSrcX = hx + vm.CloneOffsetDeltaX;
                    double shiftSrcY = hy + vm.CloneOffsetDeltaY;

                    Shape shiftSrcShape = isSquare
                        ? new Rectangle { Width = size, Height = size, Stroke = amberBrush, Fill = amberFill, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } }
                        : new Ellipse { Width = size, Height = size, Stroke = amberBrush, Fill = amberFill, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } };

                    Canvas.SetLeft(shiftSrcShape, shiftSrcX - r);
                    Canvas.SetTop(shiftSrcShape, shiftSrcY - r);
                    WorkingChitOverlayCanvas.Children.Add(shiftSrcShape);

                    WorkingChitOverlayCanvas.Children.Add(new Line { X1 = shiftSrcX - (r + 2), Y1 = shiftSrcY, X2 = shiftSrcX + (r + 2), Y2 = shiftSrcY, Stroke = amberBrush, StrokeThickness = 1 });
                    WorkingChitOverlayCanvas.Children.Add(new Line { X1 = shiftSrcX, Y1 = shiftSrcY - (r + 2), X2 = shiftSrcX, Y2 = shiftSrcY + (r + 2), Stroke = amberBrush, StrokeThickness = 1 });
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

        private void OverlayColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            OverlayColorPopup.IsOpen = !OverlayColorPopup.IsOpen;
        }

        private void OverlayColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is OverlayColorOption option && DataContext is MainViewModel vm)
            {
                vm.SelectOverlayColorCommand.Execute(option);
            }
            OverlayColorPopup.IsOpen = false;
        }

        private void TileStripItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && DataContext is MainViewModel vm)
            {
                vm.UpdateClipboardState();

                if (item.DataContext is ChitItem chit)
                {
                    // If the right-clicked item is already selected, preserve multi-selection
                    if (!chit.IsSelected)
                    {
                        // Deselect other items and select only this right-clicked item
                        foreach (var other in vm.LoadedChits)
                        {
                            other.IsSelected = ReferenceEquals(other, chit);
                        }
                    }
                    vm.SelectedChit = chit;
                    item.Focus();
                }
            }
        }

        private void TileStripItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateClipboardState();
            }
        }
    }
}
