// =================================================================================================
// FILE: MainWindow.xaml.cs
// LOCATION: Mp3GainApp/MainWindow.xaml.cs
//
// WHAT THIS FILE DOES:
//   This is the "code-behind" for the main window. It handles direct Windows desktop interactions:
//     1. Drag-and-drop: Dropping an MP3 file from File Explorer or Desktop into the app window.
//     2. Interactive Timeline Scrubbing: Clicking or dragging the playback timeline slider to skip
//        forwards or backwards in the song smoothly.
//     3. Keyboard Shortcuts: Spacebar (Play/Pause), Esc (Stop), Left/Right Arrows (Skip -5s/+5s),
//        Ctrl+O (Open), Ctrl+S (Save), Ctrl+Shift+S (Export Copy), Ctrl+Z (Revert).
//
// HOW IT RELATES TO THE APP:
//   - Bridges native Windows mouse, keyboard & drag events directly to MainViewModel.cs.
// =================================================================================================

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mp3GainApp.ViewModels;
using Wpf.Ui.Controls;

namespace Mp3GainApp
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Fires when the user clicks anywhere on the timeline slider.
        /// Immediately calculates the exact clicked position along the bar and jumps there!
        /// </summary>
        private void TimelineSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && DataContext is MainViewModel vm)
            {
                vm.IsUserSeeking = true;
                var point = e.GetPosition(slider);
                if (slider.ActualWidth > 0)
                {
                    double fraction = Math.Clamp(point.X / slider.ActualWidth, 0.0, 1.0);
                    slider.Value = fraction;
                    vm.SeekToFraction(fraction);
                }
            }
        }

        /// <summary>
        /// Fires when the user drags the timeline slider with mouse held down.
        /// </summary>
        private void TimelineSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Slider slider && DataContext is MainViewModel vm)
            {
                vm.IsUserSeeking = true;
                var point = e.GetPosition(slider);
                if (slider.ActualWidth > 0)
                {
                    double fraction = Math.Clamp(point.X / slider.ActualWidth, 0.0, 1.0);
                    slider.Value = fraction;
                }
            }
        }

        /// <summary>
        /// Fires when the user releases their mouse click on the timeline slider.
        /// Unfreezes the timer and ensures the audio is playing from the exact chosen timestamp.
        /// </summary>
        private void TimelineSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsUserSeeking = false;
                vm.SeekToFraction(TimelineSlider.Value);
            }
        }

        /// <summary>
        /// Global keyboard shortcut handler
        /// </summary>
        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Do not intercept keystrokes if user is typing numbers into an input box
            if (e.OriginalSource is System.Windows.Controls.TextBox)
            {
                if (e.Key == Key.Enter)
                {
                    // Move focus away to commit text input binding
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
                return;
            }

            if (DataContext is not MainViewModel vm) return;

            // Spacebar: Play / Pause Preview
            if (e.Key == Key.Space)
            {
                if (vm.PlayPauseCommand.CanExecute(null))
                {
                    vm.ExecutePlayPause();
                    e.Handled = true;
                }
            }
            // Escape: Stop Playback
            else if (e.Key == Key.Escape)
            {
                if (vm.StopCommand.CanExecute(null))
                {
                    vm.ExecuteStop();
                    e.Handled = true;
                }
            }
            // Left Arrow: Skip backward 5s
            else if (e.Key == Key.Left)
            {
                vm.SkipSeconds(-5);
                e.Handled = true;
            }
            // Right Arrow: Skip forward 5s
            else if (e.Key == Key.Right)
            {
                vm.SkipSeconds(5);
                e.Handled = true;
            }
            // Ctrl + O: Open MP3
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                vm.ExecuteOpenFile();
                e.Handled = true;
            }
            // Ctrl + Shift + S: Save Copy As...
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (vm.SaveCopyAsCommand.CanExecute(null))
                {
                    await vm.ExecuteSaveCopyAs();
                    e.Handled = true;
                }
            }
            // Ctrl + S: Save Changes to MP3
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (vm.SaveChangesCommand.CanExecute(null))
                {
                    await vm.ExecuteSaveChanges();
                    e.Handled = true;
                }
            }
            // Ctrl + Z: Revert
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (vm.RevertToOriginalCommand.CanExecute(null))
                {
                    vm.ExecuteRevert();
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Visual mouse cursor feedback when dragging a file over the window
        /// </summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Handles dropping an MP3 file into the window
        /// </summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var file = files[0];
                    if (Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DataContext is MainViewModel vm)
                        {
                            vm.LoadFile(file);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "Please select an MP3 audio file.", 
                            "Unsupported Format", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
        }
    }
}
