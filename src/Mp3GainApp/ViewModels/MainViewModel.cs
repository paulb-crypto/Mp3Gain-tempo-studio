// =================================================================================================
// FILE: MainViewModel.cs
// LOCATION: Mp3GainApp/ViewModels/MainViewModel.cs
//
// WHAT THIS FILE DOES:
//   This is the master "brain" of the application following the MVVM pattern.
//   It holds the state of the app (loaded song, slider settings, playback position)
//   and links the buttons in the window to the background audio services.
//
// HOW IT RELATES TO THE APP:
//   - When you click "Open MP3", this file coordinates scanning and loading the song.
//   - When you move the Volume or BPM sliders, this file pushes the new values live to the audio engine.
//   - When you drag or click the timeline scrubber, this file seeks the song to that exact point.
//   - When you click "Save" or "Save Copy As...", this file executes safe rendering & metadata preservation.
// =================================================================================================

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using Mp3GainApp.Models;
using Mp3GainApp.Services;
using Wpf.Ui.Controls;

namespace Mp3GainApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // -----------------------------------------------------------------------------------------
        // SERVICES (Background helper engines)
        // -----------------------------------------------------------------------------------------
        private readonly MetadataService _metadataService;       // Reads and saves song info & artwork
        private readonly AudioAnalyzerService _analyzerService;   // Diagnostic scanner for loudness & tempo
        private readonly AudioPlaybackService _playbackService;   // Live preview stereo player
        private readonly AudioProcessingService _processingService;// Audio rendering & MP3 encoding
        private readonly BackupService _backupService;           // Safety net for 1-click lossless reversion

        // -----------------------------------------------------------------------------------------
        // INTERNAL STATE (Variables stored in memory)
        // -----------------------------------------------------------------------------------------
        private AudioFileInfo? _currentFile;
        private AudioAnalysisResult? _analysisResult;
        private bool _isBusy;
        private string _busyMessage = string.Empty;
        private double _busyProgress;
        private string _statusMessage = "Ready. Open an MP3 file to get started.";

        // Audio adjustment parameters
        private double _gainDb = 0.0;
        private double _originalBpm = 120.0;
        private double _targetBpm = 120.0;
        private bool _preventClipping = true;
        private bool _keyLock = false;

        // Live preview & metering
        private bool _isPlaying;
        private TimeSpan _currentTime = TimeSpan.Zero;
        private TimeSpan _totalTime = TimeSpan.Zero;
        private double _playbackProgressFraction;
        private float _livePeakMeter = 0.0f;
        private double _livePeakDbfs = -96.0;
        private bool _isLiveClipping;
        private bool _hasBackup;

        /// <summary>
        /// True while the user is actively dragging or clicking the timeline scrubber.
        /// When true, the background timer pauses updating the scrubber so it doesn't fight your mouse!
        /// </summary>
        public bool IsUserSeeking { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        // -----------------------------------------------------------------------------------------
        // PROPERTIES (Bound directly to elements in MainWindow.xaml)
        // -----------------------------------------------------------------------------------------

        public AudioFileInfo? CurrentFile
        {
            get => _currentFile;
            private set
            {
                _currentFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFileLoaded));
            }
        }

        public AudioAnalysisResult? AnalysisResult
        {
            get => _analysisResult;
            private set
            {
                _analysisResult = value;
                OnPropertyChanged();
            }
        }

        public bool IsFileLoaded => _currentFile != null;

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string BusyMessage
        {
            get => _busyMessage;
            set { _busyMessage = value; OnPropertyChanged(); }
        }

        public double BusyProgress
        {
            get => _busyProgress;
            set { _busyProgress = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Volume gain in Decibels (-15 dB to +15 dB)</summary>
        public double GainDb
        {
            get => _gainDb;
            set
            {
                double clamped = Math.Clamp(Math.Round(value, 1), -15.0, 15.0);
                if (Math.Abs(_gainDb - clamped) > 0.01)
                {
                    _gainDb = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GainText));
                    OnPropertyChanged(nameof(GainInputText));
                    // Update preview audio immediately in real time
                    _playbackService.SetGainDb(_gainDb);
                }
            }
        }

        public string GainText => _gainDb > 0 ? $"+{_gainDb:F1} dB" : $"{_gainDb:F1} dB";

        /// <summary>Two-way bindable direct text input for Gain</summary>
        public string GainInputText
        {
            get => _gainDb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
                    double.TryParse(value, out parsed))
                {
                    GainDb = parsed;
                }
            }
        }

        public double OriginalBpm
        {
            get => _originalBpm;
            set { _originalBpm = value; OnPropertyChanged(); }
        }

        /// <summary>Target Beats Per Minute (BPM) selected by the user</summary>
        public double TargetBpm
        {
            get => _targetBpm;
            set
            {
                double clamped = Math.Clamp(Math.Round(value, 1), 40.0, 240.0);
                if (Math.Abs(_targetBpm - clamped) > 0.05)
                {
                    _targetBpm = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TempoFactor));
                    OnPropertyChanged(nameof(TempoPercentText));
                    OnPropertyChanged(nameof(BpmInputText));
                    // Update preview tempo immediately in real time!
                    _playbackService.SetTempoFactor(TempoFactor);
                }
            }
        }

        /// <summary>Two-way bindable direct text input for BPM</summary>
        public string BpmInputText
        {
            get => _targetBpm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
                    double.TryParse(value, out parsed))
                {
                    TargetBpm = parsed;
                }
            }
        }

        public double TempoFactor => _originalBpm > 0 ? _targetBpm / _originalBpm : 1.0;

        public string TempoPercentText
        {
            get
            {
                double pct = (TempoFactor - 1.0) * 100.0;
                return pct >= 0 ? $"+{pct:F1}%" : $"{pct:F1}%";
            }
        }

        public bool PreventClipping
        {
            get => _preventClipping;
            set
            {
                _preventClipping = value;
                OnPropertyChanged();
                _playbackService.SetPreventClipping(value);
            }
        }

        /// <summary>
        /// When KeyLock is false (Turntable / Vinyl Mode): pitch naturally tracks tempo with 100% pristine punch (club vinyl fader).
        /// When KeyLock is true (Master Tempo): locks vocal and musical pitch at original key while changing BPM (digital CDJ style).
        /// </summary>
        public bool KeyLock
        {
            get => _keyLock;
            set
            {
                if (_keyLock != value)
                {
                    _keyLock = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TempoModeName));
                    OnPropertyChanged(nameof(TempoModeDescription));
                    _playbackService.SetKeyLock(value);
                }
            }
        }

        public string TempoModeName => KeyLock ? "Key Lock (Master Tempo)" : "Turntable / Vinyl Mode";

        public string TempoModeDescription => KeyLock
            ? "Keeps vocals and musical key at the original pitch while changing BPM (digital CDJ style)."
            : "Pitch naturally tracks tempo (classic club turntable pitch slider). 100% pristine audio punch with zero slicing.";

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayButtonText));
                OnPropertyChanged(nameof(PlayButtonIcon));
            }
        }

        public string PlayButtonText => IsPlaying ? "Pause Preview" : "Preview Audio";
        public SymbolRegular PlayButtonIcon => IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;

        public TimeSpan CurrentTime
        {
            get => _currentTime;
            set
            {
                _currentTime = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan TotalTime
        {
            get => _totalTime;
            set
            {
                _totalTime = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Timeline position from 0.0 to 1.0.
        /// </summary>
        public double PlaybackProgressFraction
        {
            get => _playbackProgressFraction;
            set
            {
                _playbackProgressFraction = value;
                OnPropertyChanged();
            }
        }

        public float LivePeakMeter
        {
            get => _livePeakMeter;
            set { _livePeakMeter = value; OnPropertyChanged(); }
        }

        public double LivePeakDbfs
        {
            get => _livePeakDbfs;
            set { _livePeakDbfs = value; OnPropertyChanged(); }
        }

        public bool IsLiveClipping
        {
            get => _isLiveClipping;
            set { _isLiveClipping = value; OnPropertyChanged(); }
        }

        public bool HasBackup
        {
            get => _hasBackup;
            set { _hasBackup = value; OnPropertyChanged(); }
        }

        // -----------------------------------------------------------------------------------------
        // COMMANDS
        // -----------------------------------------------------------------------------------------
        public ICommand OpenFileCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SkipForwardCommand { get; }
        public ICommand SkipBackwardCommand { get; }
        public ICommand ApplyRecommendedGainCommand { get; }
        public ICommand ResetGainCommand { get; }
        public ICommand ResetBpmCommand { get; }
        public ICommand IncreaseBpmCommand { get; }
        public ICommand DecreaseBpmCommand { get; }
        public ICommand SetPresetSpotifyCommand { get; }
        public ICommand SetPresetClub128Command { get; }
        public ICommand SetPresetWorkout140Command { get; }
        public ICommand ResetAllCommand { get; }
        public ICommand SaveChangesCommand { get; }
        public ICommand SaveCopyAsCommand { get; }
        public ICommand RevertToOriginalCommand { get; }

        public MainViewModel()
        {
            _metadataService = new MetadataService();
            _backupService = new BackupService();
            _analyzerService = new AudioAnalyzerService();
            _playbackService = new AudioPlaybackService();
            _processingService = new AudioProcessingService(_metadataService, _backupService);

            // Connect real-time playback timer updates to the window
            _playbackService.ProgressChanged += (current, total) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentTime = current;
                    TotalTime = total;

                    // Only update scrubber slider if user isn't actively dragging/clicking it
                    if (!IsUserSeeking && total.TotalSeconds > 0)
                    {
                        PlaybackProgressFraction = current.TotalSeconds / total.TotalSeconds;
                    }
                });
            };

            // Connect real-time sound meters to the window
            _playbackService.LevelMeterChanged += (linear, dbfs, clipping) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    LivePeakMeter = Math.Clamp(linear, 0.0f, 1.2f);
                    LivePeakDbfs = dbfs;
                    IsLiveClipping = clipping;
                });
            };

            // Detect when music starts or stops
            _playbackService.PlaybackStateChanged += state =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsPlaying = state == PlaybackState.Playing;
                });
            };

            // Catch and display any audio playback error
            _playbackService.PlaybackError += errorMsg =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusMessage = errorMsg;
                    System.Windows.MessageBox.Show(errorMsg, "Audio Playback Issue", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                });
            };

            // Wire up user buttons to their actions
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            PlayPauseCommand = new RelayCommand(ExecutePlayPause, () => IsFileLoaded && !IsBusy);
            StopCommand = new RelayCommand(ExecuteStop, () => IsFileLoaded);
            SkipForwardCommand = new RelayCommand(() => _playbackService.Skip(TimeSpan.FromSeconds(5)), () => IsFileLoaded);
            SkipBackwardCommand = new RelayCommand(() => _playbackService.Skip(TimeSpan.FromSeconds(-5)), () => IsFileLoaded);

            ApplyRecommendedGainCommand = new RelayCommand(ExecuteApplyRecommendedGain, () => AnalysisResult != null);
            ResetGainCommand = new RelayCommand(() => GainDb = 0.0);
            ResetBpmCommand = new RelayCommand(() => TargetBpm = OriginalBpm);
            IncreaseBpmCommand = new RelayCommand(() => TargetBpm += 1.0);
            DecreaseBpmCommand = new RelayCommand(() => TargetBpm -= 1.0);

            // Presets
            SetPresetSpotifyCommand = new RelayCommand(ExecuteApplyRecommendedGain, () => AnalysisResult != null);
            SetPresetClub128Command = new RelayCommand(() => TargetBpm = 128.0, () => IsFileLoaded);
            SetPresetWorkout140Command = new RelayCommand(() => TargetBpm = 140.0, () => IsFileLoaded);
            ResetAllCommand = new RelayCommand(() => { GainDb = 0.0; TargetBpm = OriginalBpm; }, () => IsFileLoaded);

            SaveChangesCommand = new RelayCommand(async () => await ExecuteSaveChanges(), () => IsFileLoaded && !IsBusy);
            SaveCopyAsCommand = new RelayCommand(async () => await ExecuteSaveCopyAs(), () => IsFileLoaded && !IsBusy);
            RevertToOriginalCommand = new RelayCommand(ExecuteRevert, () => IsFileLoaded && HasBackup && !IsBusy);
        }

        /// <summary>
        /// Instantly seeks audio playback to a specific fraction (0.0 = start, 1.0 = end).
        /// </summary>
        public void SeekToFraction(double fraction)
        {
            if (TotalTime.TotalSeconds > 0)
            {
                double targetSeconds = Math.Clamp(fraction * TotalTime.TotalSeconds, 0, TotalTime.TotalSeconds);
                var target = TimeSpan.FromSeconds(targetSeconds);
                CurrentTime = target;
                PlaybackProgressFraction = fraction;
                _playbackService.Seek(target);
            }
        }

        /// <summary>
        /// Skips forward or backward by the specified number of seconds.
        /// </summary>
        public void SkipSeconds(int seconds)
        {
            if (IsFileLoaded)
            {
                _playbackService.Skip(TimeSpan.FromSeconds(seconds));
            }
        }

        /// <summary>
        /// Reads an MP3 file, extracts album art and ID3 tags, and scans loudness and tempo.
        /// </summary>
        public async void LoadFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                IsBusy = true;
                BusyMessage = "Reading file metadata & tags...";
                BusyProgress = 10;
                StatusMessage = $"Loading {Path.GetFileName(filePath)}...";

                _playbackService.Stop();

                // 1. Read song information (Title, Artist, Album, Cover Art)
                var info = await Task.Run(() => _metadataService.ReadAudioFileInfo(filePath));
                CurrentFile = info;
                HasBackup = _backupService.HasBackup(filePath);

                // 2. Scan audio for loudness (Peak, RMS, LUFS) and calculate BPM
                BusyMessage = "Measuring LUFS loudness & detecting BPM...";
                var progress = new Progress<double>(p => BusyProgress = p);
                var analysis = await Task.Run(() => _analyzerService.AnalyzeAudio(filePath, progress));
                AnalysisResult = analysis;

                // 3. Set sliders to the song's detected tempo and reset gain to 0
                double detectedOrTaggedBpm = info.OriginalBpm > 0 ? info.OriginalBpm : analysis.DetectedBpm;
                if (detectedOrTaggedBpm <= 0) detectedOrTaggedBpm = 120.0;
                OriginalBpm = detectedOrTaggedBpm;
                TargetBpm = detectedOrTaggedBpm;
                GainDb = 0.0;

                // 4. Prepare preview engine so user can click Play immediately
                _playbackService.LoadAudio(filePath, GainDb, TempoFactor, PreventClipping, KeyLock);
                TotalTime = info.Duration;
                CurrentTime = TimeSpan.Zero;
                PlaybackProgressFraction = 0.0;

                StatusMessage = $"Loaded: {info.FileName} | LUFS: {analysis.Lufs:F1} | Peak: {analysis.PeakDbfs:F1} dB | BPM: {TargetBpm:F1}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading audio: {ex.Message}";
                System.Windows.MessageBox.Show($"Could not load file:\n{ex.Message}", "Load Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ExecuteOpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MP3 Audio Files (*.mp3)|*.mp3|All Files (*.*)|*.*",
                Title = "Select MP3 to Analyze and Edit"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadFile(dialog.FileName);
            }
        }

        public void ExecutePlayPause()
        {
            if (IsPlaying)
            {
                _playbackService.Pause();
            }
            else
            {
                _playbackService.Play();
            }
        }

        public void ExecuteStop()
        {
            _playbackService.Stop();
        }

        private void ExecuteApplyRecommendedGain()
        {
            if (AnalysisResult != null)
            {
                GainDb = AnalysisResult.RecommendedGainDb;
                StatusMessage = $"Applied recommended gain of {GainDb:F1} dB (targets -14 LUFS standard)";
            }
        }

        public async Task ExecuteSaveChanges()
        {
            if (CurrentFile == null) return;

            try
            {
                string dir = Path.GetDirectoryName(CurrentFile.FilePath) ?? string.Empty;
                string baseName = Path.GetFileNameWithoutExtension(CurrentFile.FilePath);

                // Strip any previous (Edited...) tags so we don't end up with "Song (Edited) (Edited).mp3"
                baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s*\((Edited|Adjusted)[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\.original$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                string bpmSuffix = Math.Abs(TargetBpm - _originalBpm) > 0.5 ? $" - {TargetBpm:F0} BPM" : "";
                string targetFileName = $"{baseName} (Edited{bpmSuffix}).mp3";
                string targetFilePath = Path.Combine(dir, targetFileName);

                // Fully release MP3 file so the save process can read and write it
                _playbackService.UnloadAudio();

                IsBusy = true;
                BusyMessage = "Applying gain, WSOLA tempo adjustments & preserving ID3 tags...";
                BusyProgress = 0;
                StatusMessage = $"Saving edited MP3 as {targetFileName}...";

                var progress = new Progress<double>(p => BusyProgress = p);

                await _processingService.ProcessAndSaveMp3Async(
                    CurrentFile.FilePath,
                    targetFilePath,
                    GainDb,
                    TempoFactor,
                    TargetBpm,
                    PreventClipping,
                    KeyLock,
                    320,
                    progress);

                // Reload audio player with original file so user can continue previewing
                _playbackService.LoadAudio(CurrentFile.FilePath, GainDb, TempoFactor, PreventClipping, KeyLock);

                StatusMessage = $"Saved: {targetFileName}";
                System.Windows.MessageBox.Show(
                    $"Edited MP3 successfully saved as:\n{targetFileName}\n\nLocation:\n{dir}\n\nYour original file '{CurrentFile.FileName}' remains completely untouched.",
                    "Edited File Saved",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Error saving MP3:\n{ex.Message}", "Save Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ExecuteSaveCopyAs()
        {
            if (CurrentFile == null) return;

            string dir = Path.GetDirectoryName(CurrentFile.FilePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(CurrentFile.FilePath);
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s*\((Edited|Adjusted)[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\.original$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            string bpmSuffix = Math.Abs(TargetBpm - _originalBpm) > 0.5 ? $" - {TargetBpm:F0} BPM" : "";
            string defaultName = $"{baseName} (Edited{bpmSuffix}).mp3";

            var dialog = new SaveFileDialog
            {
                Filter = "MP3 Audio Files (*.mp3)|*.mp3|All Files (*.*)|*.*",
                Title = "Export Copy As New MP3",
                FileName = defaultName,
                InitialDirectory = dir
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    BusyMessage = "Exporting MP3 copy with gain, tempo & ID3 tags...";
                    BusyProgress = 0;
                    StatusMessage = $"Exporting copy to {Path.GetFileName(dialog.FileName)}...";

                    var progress = new Progress<double>(p => BusyProgress = p);

                    await _processingService.ProcessAndSaveMp3Async(
                        CurrentFile.FilePath,
                        dialog.FileName,
                        GainDb,
                        TempoFactor,
                        TargetBpm,
                        PreventClipping,
                        KeyLock,
                        320,
                        progress);

                    StatusMessage = $"Exported: {Path.GetFileName(dialog.FileName)}";
                    System.Windows.MessageBox.Show($"File successfully exported to:\n{dialog.FileName}\n\nOriginal file was left untouched.", "Export Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                    System.Windows.MessageBox.Show($"Error exporting MP3:\n{ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        public void ExecuteRevert()
        {
            if (CurrentFile == null || !HasBackup) return;

            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to revert this file to its original untouched state?",
                "Confirm Revert",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Release the file completely before restoring the backup over it
                _playbackService.UnloadAudio();
                bool restored = _backupService.RevertToOriginal(CurrentFile.FilePath);
                if (restored)
                {
                    LoadFile(CurrentFile.FilePath);
                    StatusMessage = "Reverted file back to original state.";
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
