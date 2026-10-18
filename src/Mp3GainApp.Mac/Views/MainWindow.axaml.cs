// =================================================================================================
// FILE: MainWindow.axaml.cs
// LOCATION: Mp3GainApp.Mac/Views/MainWindow.axaml.cs
//
// WHAT THIS FILE DOES:
//   Code-behind for MainWindow:
//     - StorageProvider file open / save dialogs (macOS native file pickers).
//     - Drag and Drop MP3 file handling.
//     - Timeline scrubbing pointer events.
// =================================================================================================

using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mp3GainApp.Mac.ViewModels;

namespace Mp3GainApp.Mac.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainViewModel vm)
            {
                vm.RequestSaveFileDialog += async (defaultName, callback) =>
                {
                    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save Copy As MP3",
                        SuggestedFileName = defaultName,
                        DefaultExtension = "mp3",
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("MP3 Audio") { Patterns = new[] { "*.mp3" } }
                        }
                    });

                    if (file != null)
                    {
                        string localPath = file.Path.LocalPath;
                        callback(localPath);
                    }
                };
            }
        }

        private async void OnBrowseFileClicked(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select MP3 Audio File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("MP3 Audio") { Patterns = new[] { "*.mp3" } }
                }
            });

            if (files.Count > 0 && DataContext is MainViewModel vm)
            {
                string localPath = files[0].Path.LocalPath;
                await vm.LoadFileAsync(localPath);
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files) && DataContext is MainViewModel vm)
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    var firstFile = files.FirstOrDefault(f => f.Path.LocalPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
                    if (firstFile != null)
                    {
                        await vm.LoadFileAsync(firstFile.Path.LocalPath);
                    }
                }
            }
        }

        private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SetDraggingTimeline(true);
            }
        }

        private void OnTimelinePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (sender is Slider slider && DataContext is MainViewModel vm)
            {
                vm.SeekTimeline(slider.Value);
                vm.SetDraggingTimeline(false);
            }
        }
    }
}

