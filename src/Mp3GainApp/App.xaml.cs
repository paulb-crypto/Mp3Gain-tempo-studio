// =================================================================================================
// FILE: App.xaml.cs
// LOCATION: Mp3GainApp/App.xaml.cs
//
// WHAT THIS FILE DOES:
//   This is the application startup entry point.
//   It initializes the application lifecycle and configures the Windows 11 Dark theme
//   with Mica / Fluent design before the main window is displayed.
//
// HOW IT RELATES TO THE APP:
//   - When you double-click Mp3GainApp.exe, Windows runs this code first to launch the app.
// =================================================================================================

using System.Windows;
using Wpf.Ui.Appearance;

namespace Mp3GainApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply modern Windows 11 Fluent Dark theme
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
    }
}
