// =================================================================================================
// FILE: MainViewModel.cs
// LOCATION: Mp3GainApp.Mac/ViewModels/MainViewModel.cs
//
// WHAT THIS FILE DOES:
//   MVVM orchestrator for the macOS MP3 Gain & BPM Studio application:
//     - Coordinates audio analysis, real-time playback preview, gain and tempo sliders,
//       save / save copy as, and 1-click revert backup restoration.
// =================================================================================================

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Mp3GainApp.Mac.Models;
using Mp3GainApp.Mac.Services;

namespace Mp3GainApp.Mac.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly MetadataService _metadataService;
        private readonly BackupService _backupService;
        private readonly AudioAnalyzerService _analyzerService;
        private readonly AudioPlaybackService _playbackService;
        private readonly AudioProcessingService _processingService;

        // Active song & diagnostic models
        private AudioFileInfo? _currentFile;
        private AudioAnalysisResult? _analysisResult;

        // UI state variables
        private bool _isBusy;
        private string _statusMessage = "Ready. Drag and drop an MP3 file or click Browse to start.";
        private double _progressValue;
        private bool _isProgressIndeterminate;
        private bool _hasFileLoaded;
        private bool _hasBackup;

        // Gain adjustments
        private double _gainDb = 0.0;
        private string _gainInputText = "0.0";
        private bool _preventClipping = true;

        // BPM adjustments
        private double _tempoSliderValue = 0.0;
        private double _calculatedBpm = 120.0;
        private string _bpmInputText = "120.0";

        // Real-time playback
        private PlaybackState _playbackState = PlaybackState.Stopped;
        private TimeSpan _currentTime = TimeSpan.Zero;
        private TimeSpan _totalTime = TimeSpan.Zero;
        private double _timelineSliderValue;
        private bool _isUserDraggingTimeline;
        private float _vuMeterLevel;
        private string _vuMeterDbText = "-∞ dBFS";
        private bool _isClippingLive;

        // Event handler for UI notifications
        public event Action<string, string>? ShowNotification;
        public event Action<string, Action<string>>? RequestSaveFileDialog;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _metadataService = new MetadataService();
            _backupService = new BackupService();
            _analyzerService = new AudioAnalyzerService();
            _playbackService = new AudioPlaybackService();
            _processingService = new AudioProcessingService(_metadataService, _backupService);

            // Connect playback events
            _playbackService.ProgressChanged += (current, total) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_isUserDraggingTimeline && total.TotalSeconds > 0)
                    {
                        _currentTime = current;
                        _totalTime = total;
                        _timelineSliderValue = Math.Clamp(current.TotalSeconds / total.TotalSeconds * 100.0, 0.0, 100.0);
                        OnPropertyChanged(nameof(CurrentTime));
                        OnPropertyChanged(nameof(TotalTime));
                        OnPropertyChanged(nameof(CurrentTimeFormatted));
                        OnPropertyChanged(nameof(TotalTimeFormatted));
                        OnPropertyChanged(nameof(TimelineSliderValue));
                    }
                });
            };

            _playbackService.LevelMeterChanged += (peak, db, clipped) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    VuMeterLevel = peak;
                    VuMeterDbText = db > -90 ? $"{db:F1} dBFS" : "-∞ dBFS";
                    IsClippingLive = clipped;
                });
            };

            _playbackService.PlaybackStateChanged += (state) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PlaybackState = state;
                });
            };

            _playbackService.PlaybackError += (err) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = err;
                });
            };

            // Initialize commands
            PlayCommand = new RelayCommand(ExecutePlay, () => HasFileLoaded && !IsBusy);
            PauseCommand = new RelayCommand(ExecutePause, () => HasFileLoaded && PlaybackState == PlaybackState.Playing);
            StopCommand = new RelayCommand(ExecuteStop, () => HasFileLoaded && PlaybackState != PlaybackState.Stopped);
            SkipBackwardCommand = new RelayCommand(() => _playbackService.Skip(TimeSpan.FromSeconds(-5)), () => HasFileLoaded);
            SkipForwardCommand = new RelayCommand(() => _playbackService.Skip(TimeSpan.FromSeconds(5)), () => HasFileLoaded);

            SetGainPresetCommand = new RelayCommand<string>(ExecuteSetGainPreset);
            ApplyAutoGainCommand = new RelayCommand(ExecuteApplyAutoGain, () => AnalysisResult != null);
            ResetGainCommand = new RelayCommand(() => GainDb = 0.0);

            SetTempoPresetCommand = new RelayCommand<string>(ExecuteSetTempoPreset);
            ResetBpmCommand = new RelayCommand(() => TempoSliderValue = 0.0);

            SaveCommand = new RelayCommand(async () => await ExecuteSaveAsync(false), () => HasFileLoaded && !IsBusy);
            SaveCopyCommand = new RelayCommand(ExecuteSaveCopy, () => HasFileLoaded && !IsBusy);
            RevertCommand = new RelayCommand(async () => await ExecuteRevertAsync(), () => HasBackup && !IsBusy);
        }

        // =========================================================================================
        // PUBLIC BINDING PROPERTIES
        // =========================================================================================

        public AudioFileInfo? CurrentFile
        {
            get => _currentFile;
            private set { _currentFile = value; OnPropertyChanged(); }
        }

        public AudioAnalysisResult? AnalysisResult
        {
            get => _analysisResult;
            private set { _analysisResult = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                RaiseCommandStatuses();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set { _isProgressIndeterminate = value; OnPropertyChanged(); }
        }

        public bool HasFileLoaded
        {
            get => _hasFileLoaded;
            private set
            {
                _hasFileLoaded = value;
                OnPropertyChanged();
                RaiseCommandStatuses();
            }
        }

        public bool HasBackup
        {
            get => _hasBackup;
            private set
            {
                _hasBackup = value;
                OnPropertyChanged();
                ((RelayCommand)RevertCommand).RaiseCanExecuteChanged();
            }
        }

        public double GainDb
        {
            get => _gainDb;
            set
            {
                double clamped = Math.Clamp(Math.Round(value, 1), -15.0, 15.0);
                if (Math.Abs(_gainDb - clamped) > 0.01)
                {
                    _gainDb = clamped;
                    _gainInputText = clamped.ToString("0.0");
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GainDbFormatted));
                    OnPropertyChanged(nameof(GainInputText));
                    _playbackService.SetGainDb(_gainDb);
                }
            }
        }

        public string GainDbFormatted => _gainDb >= 0 ? $"+{_gainDb:F1} dB" : $"{_gainDb:F1} dB";

        public string GainInputText
        {
            get => _gainInputText;
            set
            {
                _gainInputText = value;
                OnPropertyChanged();
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
                    double.TryParse(value, out parsed))
                {
                    double clamped = Math.Clamp(parsed, -15.0, 15.0);
                    if (Math.Abs(_gainDb - clamped) > 0.01)
                    {
                        _gainDb = clamped;
                        OnPropertyChanged(nameof(GainDb));
                        OnPropertyChanged(nameof(GainDbFormatted));
                        _playbackService.SetGainDb(_gainDb);
                    }
                }
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

        public double TempoSliderValue
        {
            get => _tempoSliderValue;
            set
            {
                double clamped = Math.Clamp(Math.Round(value, 1), -30.0, 50.0);
                if (Math.Abs(_tempoSliderValue - clamped) > 0.01)
                {
                    _tempoSliderValue = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TempoSliderFormatted));
                    UpdateBpmFromSlider();
                }
            }
        }

        public string TempoSliderFormatted => _tempoSliderValue >= 0 ? $"+{_tempoSliderValue:F1}%" : $"{_tempoSliderValue:F1}%";

        public double CalculatedBpm
        {
            get => _calculatedBpm;
            private set
            {
                _calculatedBpm = Math.Round(value, 1);
                _bpmInputText = _calculatedBpm.ToString("0.0");
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalculatedBpmFormatted));
                OnPropertyChanged(nameof(BpmInputText));
            }
        }

        public string CalculatedBpmFormatted => $"{_calculatedBpm:F1} BPM";

        public string BpmInputText
        {
            get => _bpmInputText;
            set
            {
                _bpmInputText = value;
                OnPropertyChanged();
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
                    double.TryParse(value, out parsed))
                {
                    if (parsed >= 30.0 && parsed <= 300.0 && BaseBpm > 0)
                    {
                        double percent = ((parsed / BaseBpm) - 1.0) * 100.0;
                        percent = Math.Clamp(percent, -30.0, 50.0);
                        _tempoSliderValue = Math.Round(percent, 1);
                        _calculatedBpm = parsed;
                        OnPropertyChanged(nameof(TempoSliderValue));
                        OnPropertyChanged(nameof(TempoSliderFormatted));
                        OnPropertyChanged(nameof(CalculatedBpm));
                        OnPropertyChanged(nameof(CalculatedBpmFormatted));
                        _playbackService.SetTempoFactor(1.0 + _tempoSliderValue / 100.0);
                    }
                }
            }
        }

        public double BaseBpm => AnalysisResult != null && AnalysisResult.DetectedBpm > 0 ? AnalysisResult.DetectedBpm : 120.0;

        public PlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                _playbackState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsStopped));
                RaiseCommandStatuses();
            }
        }

        public bool IsPlaying => _playbackState == PlaybackState.Playing;
        public bool IsPaused => _playbackState == PlaybackState.Paused;
        public bool IsStopped => _playbackState == PlaybackState.Stopped;

        public TimeSpan CurrentTime => _currentTime;
        public TimeSpan TotalTime => _totalTime;
        public string CurrentTimeFormatted => $"{(int)_currentTime.TotalMinutes:D2}:{_currentTime.Seconds:D2}";
        public string TotalTimeFormatted => $"{(int)_totalTime.TotalMinutes:D2}:{_totalTime.Seconds:D2}";

        public double TimelineSliderValue
        {
            get => _timelineSliderValue;
            set
            {
                _timelineSliderValue = value;
                OnPropertyChanged();
            }
        }

        public float VuMeterLevel
        {
            get => _vuMeterLevel;
            private set { _vuMeterLevel = value; OnPropertyChanged(); }
        }

        public string VuMeterDbText
        {
            get => _vuMeterDbText;
            private set { _vuMeterDbText = value; OnPropertyChanged(); }
        }

        public bool IsClippingLive
        {
            get => _isClippingLive;
            private set { _isClippingLive = value; OnPropertyChanged(); }
        }

        // =========================================================================================
        // COMMANDS
        // =========================================================================================

        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SkipBackwardCommand { get; }
        public ICommand SkipForwardCommand { get; }
        public ICommand SetGainPresetCommand { get; }
        public ICommand ApplyAutoGainCommand { get; }
        public ICommand ResetGainCommand { get; }
        public ICommand SetTempoPresetCommand { get; }
        public ICommand ResetBpmCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveCopyCommand { get; }
        public ICommand RevertCommand { get; }

        // =========================================================================================
        // LOGIC METHODS
        // =========================================================================================

        public async Task LoadFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                IsBusy = true;
                IsProgressIndeterminate = false;
                ProgressValue = 0.0;
                StatusMessage = $"Analyzing {Path.GetFileName(filePath)}...";

                _playbackService.Stop();

                CurrentFile = _metadataService.ReadAudioFileInfo(filePath);
                HasBackup = _backupService.HasBackup(filePath);

                var progress = new Progress<double>(val => ProgressValue = val);
                AnalysisResult = await Task.Run(() => _analyzerService.AnalyzeAudio(filePath, progress));

                GainDb = AnalysisResult.RecommendedGainDb;
                TempoSliderValue = 0.0;
                CalculatedBpm = AnalysisResult.DetectedBpm > 0 ? AnalysisResult.DetectedBpm : 120.0;

                _playbackService.LoadAudio(filePath, GainDb, 1.0, PreventClipping);

                HasFileLoaded = true;
                StatusMessage = $"Ready. Analyzed: {AnalysisResult.PeakSummary} Peak, {AnalysisResult.LufsSummary}, {AnalysisResult.BpmSummary}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error analyzing file: {ex.Message}";
                ShowNotification?.Invoke("Load Error", $"Failed to process audio: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateBpmFromSlider()
        {
            double baseBpm = BaseBpm;
            double factor = 1.0 + (_tempoSliderValue / 100.0);
            CalculatedBpm = baseBpm * factor;
            _playbackService.SetTempoFactor(factor);
        }

        private void ExecuteSetGainPreset(string? param)
        {
            if (double.TryParse(param, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double db))
            {
                GainDb = db;
            }
        }

        private void ExecuteApplyAutoGain()
        {
            if (AnalysisResult != null)
            {
                GainDb = AnalysisResult.RecommendedGainDb;
            }
        }

        private void ExecuteSetTempoPreset(string? param)
        {
            if (param == "half")
            {
                CalculatedBpm = BaseBpm * 0.5;
                TempoSliderValue = -50.0;
            }
            else if (param == "double")
            {
                CalculatedBpm = BaseBpm * 2.0;
                TempoSliderValue = 50.0;
            }
            else if (double.TryParse(param, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
            {
                TempoSliderValue = pct;
            }
        }

        private void ExecutePlay() => _playbackService.Play();
        private void ExecutePause() => _playbackService.Pause();
        private void ExecuteStop() => _playbackService.Stop();

        public void SeekTimeline(double percent)
        {
            if (TotalTime.TotalSeconds > 0)
            {
                var target = TimeSpan.FromSeconds((percent / 100.0) * TotalTime.TotalSeconds);
                _playbackService.Seek(target);
            }
        }

        public void SetDraggingTimeline(bool isDragging)
        {
            _isUserDraggingTimeline = isDragging;
        }

        private async Task ExecuteSaveAsync(bool isCopy, string? targetPath = null)
        {
            if (CurrentFile == null || string.IsNullOrEmpty(CurrentFile.FilePath))
                return;

            string sourcePath = CurrentFile.FilePath;
            string destPath = targetPath ?? sourcePath;

            try
            {
                IsBusy = true;
                IsProgressIndeterminate = false;
                ProgressValue = 0.0;
                StatusMessage = isCopy ? $"Rendering copy to {Path.GetFileName(destPath)}..." : $"Rendering and saving changes to {CurrentFile.FileName}...";

                _playbackService.UnloadAudio();

                double tempoFactor = 1.0 + (_tempoSliderValue / 100.0);
                var progress = new Progress<double>(val => ProgressValue = val);

                await _processingService.ProcessAndSaveMp3Async(
                    sourcePath,
                    destPath,
                    GainDb,
                    tempoFactor,
                    CalculatedBpm,
                    PreventClipping,
                    CurrentFile.BitrateKbps > 0 ? CurrentFile.BitrateKbps : 320,
                    progress);

                HasBackup = _backupService.HasBackup(sourcePath);
                StatusMessage = isCopy ? "Copy saved successfully!" : "Changes applied and saved to MP3!";
                ShowNotification?.Invoke("Save Complete", $"Successfully saved MP3 with Gain {GainDbFormatted} and Tempo {CalculatedBpm:F1} BPM.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
                ShowNotification?.Invoke("Save Error", $"Could not save MP3: {ex.Message}");
            }
            finally
            {
                _playbackService.LoadAudio(sourcePath, GainDb, 1.0 + (_tempoSliderValue / 100.0), PreventClipping);
                IsBusy = false;
            }
        }

        private void ExecuteSaveCopy()
        {
            if (CurrentFile == null) return;
            string defaultName = $"{Path.GetFileNameWithoutExtension(CurrentFile.FilePath)} - Adjusted.mp3";
            RequestSaveFileDialog?.Invoke(defaultName, async (selectedPath) =>
            {
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    await ExecuteSaveAsync(true, selectedPath);
                }
            });
        }

        private async Task ExecuteRevertAsync()
        {
            if (CurrentFile == null || !HasBackup)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Reverting to pristine original backup...";

                _playbackService.UnloadAudio();

                bool reverted = _backupService.RevertToOriginal(CurrentFile.FilePath);
                if (reverted)
                {
                    StatusMessage = "Reverted to original! Reloading audio...";
                    await LoadFileAsync(CurrentFile.FilePath);
                    ShowNotification?.Invoke("Reverted", "Original untouched MP3 audio and tags restored!");
                }
                else
                {
                    StatusMessage = "Backup file not found.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Revert failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RaiseCommandStatuses()
        {
            ((RelayCommand)PlayCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PauseCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveCopyCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RevertCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ApplyAutoGainCommand).RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

