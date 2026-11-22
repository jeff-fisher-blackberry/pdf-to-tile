using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                vm.GetViewportSize = () => (PageScrollViewer.ActualWidth, PageScrollViewer.ActualHeight);
            }
        }

        private void PageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsFitToPage)
            {
                vm.RecalculateFitToPage(PageScrollViewer.ActualWidth, PageScrollViewer.ActualHeight);
            }
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }
    }
}
