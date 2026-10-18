// =================================================================================================
// FILE: AudioPlaybackService.cs
// LOCATION: Mp3GainApp/Services/AudioPlaybackService.cs
//
// WHAT THIS FILE DOES:
//   This is the real-time audio playback preview player.
//   It streams the song to your speakers while continuously applying your volume gain,
//   real-time tempo/BPM changes, and the soft-knee anti-clipping limiter.
//   It also measures audio peaks to bounce the visual volume meter and updates the scrubber.
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel connects to this service when you click "Preview Audio", "Pause Preview",
//     "Stop", drag the timeline slider, or move the Volume Gain and BPM sliders.
// =================================================================================================

using System.Timers;
using NAudio.Wave;
using Timer = System.Timers.Timer;

namespace Mp3GainApp.Services
{
    public class AudioPlaybackService : IDisposable
    {
        private WaveOutEvent? _waveOut;                                  // Sends audio directly to Windows sound device
        private AudioFileReader? _audioFileReader;                      // Decodes MP3 audio stream
        private ITempoAdjustableSampleProvider? _tempoProvider;         // Real-time tempo processor (Varispeed or WSOLA)
        private GainAndLimiterSampleProvider? _gainLimiterProvider;     // Real-time volume boost & soft limiter
        private MeteringSampleProvider? _meteringProvider;              // Real-time VU peak level analyzer
        private readonly Timer _progressTimer;                          // 20 FPS timer for the timeline slider

        private string? _currentFilePath;
        private double _currentGainDb;
        private double _currentTempoFactor;
        private bool _preventClipping;
        private bool _keyLock;

        // Events that notify the user interface
        public event Action<TimeSpan, TimeSpan>? ProgressChanged;       // (CurrentTime, TotalDuration)
        public event Action<float, double, bool>? LevelMeterChanged;    // (PeakLevel, Peak_dB, IsClipping)
        public event Action<PlaybackState>? PlaybackStateChanged;        // (Playing, Paused, Stopped)
        public event Action<string>? PlaybackError;                      // Error notification

        public PlaybackState CurrentState => _waveOut?.PlaybackState ?? PlaybackState.Stopped;
        public TimeSpan CurrentTime => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioFileReader?.TotalTime ?? TimeSpan.Zero;

        public AudioPlaybackService()
        {
            _progressTimer = new Timer(50); // 20 updates per second for smooth timeline movement
            _progressTimer.Elapsed += OnTimerElapsed;
        }

        /// <summary>
        /// Loads an MP3 file into the real-time preview pipeline.
        /// </summary>
        public void LoadAudio(string filePath, double initialGainDb, double initialTempoFactor, bool preventClipping, bool keyLock = false)
        {
            _currentFilePath = filePath;
            _currentGainDb = initialGainDb;
            _currentTempoFactor = initialTempoFactor;
            _preventClipping = preventClipping;
            _keyLock = keyLock;

            Stop();
            DisposePlaybackEngine();

            try
            {
                // 1. Read MP3 file
                _audioFileReader = new AudioFileReader(filePath);

                // 2. Wrap in real-time tempo processor (Turntable Varispeed or WSOLA Key Lock)
                _tempoProvider = keyLock
                    ? new WsolaTempoSampleProvider(_audioFileReader, (float)initialTempoFactor)
                    : new VarispeedTempoSampleProvider(_audioFileReader, (float)initialTempoFactor);

                // 3. Wrap in volume gain multiplier & anti-clipping limiter
                _gainLimiterProvider = new GainAndLimiterSampleProvider(_tempoProvider, initialGainDb, preventClipping);

                // 4. Wrap in level meter to feed the visual volume bar and clipping warning
                _meteringProvider = new MeteringSampleProvider(_gainLimiterProvider);
                _meteringProvider.LevelsReported += (peakLinear, peakDbfs, isClipping) =>
                {
                    LevelMeterChanged?.Invoke(peakLinear, peakDbfs, isClipping);
                };

                // 5. Connect to Windows sound output
                _waveOut = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 };
                _waveOut.Init(_meteringProvider);
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    _progressTimer.Stop();
                    PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
                    if (e.Exception != null)
                    {
                        PlaybackError?.Invoke($"Audio playback error: {e.Exception.Message}");
                    }
                };
            }
            catch (Exception ex)
            {
                PlaybackError?.Invoke($"Failed to initialize audio: {ex.Message}");
            }
        }

        /// <summary>Starts playing the preview audio</summary>
        public void Play()
        {
            try
            {
                if (_audioFileReader != null && _waveOut != null)
                {
                    // If file reached the end, rewind to start
                    if (_audioFileReader.Position >= _audioFileReader.Length)
                    {
                        _audioFileReader.Position = 0;
                        _tempoProvider?.Reset();
                    }

                    _waveOut.Play();
                    _progressTimer.Start();
                    PlaybackStateChanged?.Invoke(PlaybackState.Playing);
                }
            }
            catch (Exception ex)
            {
                PlaybackError?.Invoke($"Could not play audio: {ex.Message}");
            }
        }

        /// <summary>Pauses preview playback</summary>
        public void Pause()
        {
            try
            {
                if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
                {
                    _waveOut.Pause();
                    _progressTimer.Stop();
                    PlaybackStateChanged?.Invoke(PlaybackState.Paused);
                }
            }
            catch { }
        }

        /// <summary>
        /// Completely releases the loaded MP3 file by disposing every audio component.
        /// This MUST be called before saving/writing to the same file, otherwise Windows
        /// will report "file is being used by another process" and the save will fail.
        /// After saving, call LoadAudio() again to re-open the file for preview.
        /// </summary>
        public void UnloadAudio()
        {
            DisposePlaybackEngine();
            PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
        }

        /// <summary>Stops playback and rewinds to the start</summary>
        public void Stop()
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _progressTimer.Stop();
                    if (_audioFileReader != null)
                    {
                        _audioFileReader.Position = 0;
                    }
                    _tempoProvider?.Reset();
                    ProgressChanged?.Invoke(TimeSpan.Zero, TotalTime);
                    PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
                }
            }
            catch { }
        }

        /// <summary>
        /// Jumps to a specific time in the song when the user clicks or drags the timeline slider.
        /// </summary>
        public void Seek(TimeSpan targetTime)
        {
            if (_audioFileReader != null)
            {
                var clamped = targetTime < TimeSpan.Zero ? TimeSpan.Zero :
                              targetTime > _audioFileReader.TotalTime ? _audioFileReader.TotalTime : targetTime;
                
                _audioFileReader.CurrentTime = clamped;
                _tempoProvider?.Reset();
                ProgressChanged?.Invoke(clamped, TotalTime);
            }
        }

        /// <summary>
        /// Jumps forward or backward by the specified duration (e.g. +5s or -5s).
        /// </summary>
        public void Skip(TimeSpan delta)
        {
            if (_audioFileReader != null)
            {
                Seek(_audioFileReader.CurrentTime + delta);
            }
        }

        /// <summary>Updates volume immediately while audio is playing</summary>
        public void SetGainDb(double gainDb)
        {
            _gainLimiterProvider?.SetGainDb(gainDb);
        }

        /// <summary>
        /// Updates playback tempo immediately while audio is playing!
        /// Moving the BPM slider instantly speeds up or slows down the sound.
        /// </summary>
        public void SetTempoFactor(double tempoFactor)
        {
            _tempoProvider?.SetTempoFactor((float)tempoFactor);
        }

        /// <summary>Toggles the anti-clipping limiter on/off</summary>
        public void SetPreventClipping(bool prevent)
        {
            _preventClipping = prevent;
            _gainLimiterProvider?.SetPreventClipping(prevent);
        }

        /// <summary>
        /// Toggles between Turntable / Vinyl Mode (keyLock = false) and Key Lock Mode (keyLock = true).
        /// Switches the underlying playback engine immediately while audio is playing.
        /// </summary>
        public void SetKeyLock(bool keyLock)
        {
            if (_keyLock == keyLock) return;
            _keyLock = keyLock;

            if (!string.IsNullOrEmpty(_currentFilePath) && _audioFileReader != null && _waveOut != null)
            {
                bool wasPlaying = _waveOut.PlaybackState == PlaybackState.Playing;
                var currentTime = _audioFileReader.CurrentTime;
                LoadAudio(_currentFilePath, _currentGainDb, _currentTempoFactor, _preventClipping, _keyLock);
                Seek(currentTime);
                if (wasPlaying)
                {
                    Play();
                }
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_audioFileReader != null && _waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
            {
                ProgressChanged?.Invoke(_audioFileReader.CurrentTime, _audioFileReader.TotalTime);
            }
        }

        private void DisposePlaybackEngine()
        {
            _progressTimer.Stop();

            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                try { _audioFileReader.Dispose(); } catch { }
                _audioFileReader = null;
            }

            _tempoProvider = null;
            _gainLimiterProvider = null;
            _meteringProvider = null;
        }

        public void Dispose()
        {
            DisposePlaybackEngine();
            _progressTimer.Dispose();
        }
    }

    /// <summary>
    /// METERING PROVIDER:
    /// Measures peak levels and clipping in real time for UI meters.
    /// </summary>
    public class MeteringSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public WaveFormat WaveFormat => _source.WaveFormat;

        public event Action<float, double, bool>? LevelsReported;

        public MeteringSampleProvider(ISampleProvider source)
        {
            _source = source;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read <= 0) return read;

            float peak = 0.0f;
            bool clipped = false;

            for (int i = 0; i < read; i++)
            {
                float abs = Math.Abs(buffer[offset + i]);
                if (abs > peak) peak = abs;
                if (abs >= 0.999f) clipped = true;
            }

            double peakDbfs = peak > 0 ? 20.0 * Math.Log10(peak) : -96.0;
            LevelsReported?.Invoke(peak, peakDbfs, clipped);

            return read;
        }
    }
}
