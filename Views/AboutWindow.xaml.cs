using System.Reflection;
using System.Windows;

namespace pdf_to_Tiles.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            string rawVersion = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "2026-11-26.15";

            // Strip build metadata or commit hash suffix (+...) if present
            string cleanVersion = rawVersion.Split('+')[0].Trim();

            VersionTextBlock.Text = $"Version {cleanVersion}";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
