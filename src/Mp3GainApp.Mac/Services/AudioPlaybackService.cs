// =================================================================================================
// FILE: AudioPlaybackService.cs
// LOCATION: Mp3GainApp.Mac/Services/AudioPlaybackService.cs
//
// WHAT THIS FILE DOES:
//   Cross-platform audio preview player using Silk.NET.OpenAL:
//     - Streams audio to macOS CoreAudio / OpenAL subsystem in real-time.
//     - Real-time pitch-preserving WSOLA tempo shifting.
//     - Real-time gain adjustment with anti-clipping limiter.
//     - VU meter peak levels and timeline progress updates.
// =================================================================================================

using System;
using System.IO;
using System.Threading;
using System.Timers;
using NAudio.Wave;
using Silk.NET.OpenAL;
using Timer = System.Timers.Timer;

namespace Mp3GainApp.Mac.Services
{
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    public class AudioPlaybackService : IDisposable
    {
        private NLayerSampleProvider? _reader;
        private WsolaTempoSampleProvider? _tempoProvider;
        private GainAndLimiterSampleProvider? _gainLimiterProvider;
        private MeteringSampleProvider? _meteringProvider;

        private AL? _al;
        private ALContext? _alc;
        private unsafe Device* _device;
        private unsafe Context* _context;
        private uint _sourceId;
        private readonly uint[] _bufferIds = new uint[4];
        private bool _openAlReady;

        private Thread? _playbackThread;
        private volatile bool _isPlaying;
        private volatile bool _isPaused;
        private volatile bool _shouldStop;
        private readonly object _stateLock = new object();

        private readonly Timer _progressTimer;

        public event Action<TimeSpan, TimeSpan>? ProgressChanged;
        public event Action<float, double, bool>? LevelMeterChanged;
        public event Action<PlaybackState>? PlaybackStateChanged;
        public event Action<string>? PlaybackError;

        public PlaybackState CurrentState
        {
            get
            {
                if (_isPlaying && !_isPaused) return PlaybackState.Playing;
                if (_isPaused) return PlaybackState.Paused;
                return PlaybackState.Stopped;
            }
        }

        public TimeSpan CurrentTime => _reader?.CurrentPosition ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _reader?.TotalDuration ?? TimeSpan.Zero;

        public AudioPlaybackService()
        {
            _progressTimer = new Timer(50);
            _progressTimer.Elapsed += (s, e) =>
            {
                if (_reader != null && _isPlaying && !_isPaused)
                {
                    ProgressChanged?.Invoke(_reader.CurrentPosition, _reader.TotalDuration);
                }
            };

            InitOpenAL();
        }

        private unsafe void InitOpenAL()
        {
            try
            {
                _alc = ALContext.GetApi();
                _al = AL.GetApi();

                _device = _alc.OpenDevice(string.Empty);
                if (_device != null)
                {
                    _context = _alc.CreateContext(_device, null);
                    _alc.MakeContextCurrent(_context);

                    _sourceId = _al.GenSource();
                    fixed (uint* ptr = _bufferIds)
                    {
                        _al.GenBuffers(4, ptr);
                    }
                    _openAlReady = true;
                }
            }
            catch (Exception ex)
            {
                _openAlReady = false;
                System.Diagnostics.Debug.WriteLine($"OpenAL initialization fallback: {ex.Message}");
            }
        }

        public void LoadAudio(string filePath, double initialGainDb, double initialTempoFactor, bool preventClipping)
        {
            Stop();
            DisposeAudioPipeline();

            try
            {
                _reader = new NLayerSampleProvider(filePath);
                _tempoProvider = new WsolaTempoSampleProvider(_reader, (float)initialTempoFactor);
                _gainLimiterProvider = new GainAndLimiterSampleProvider(_tempoProvider, initialGainDb, preventClipping);
                _meteringProvider = new MeteringSampleProvider(_gainLimiterProvider);
                _meteringProvider.LevelsReported += (peakLinear, peakDbfs, isClipping) =>
                {
                    LevelMeterChanged?.Invoke(peakLinear, peakDbfs, isClipping);
                };
            }
            catch (Exception ex)
            {
                PlaybackError?.Invoke($"Failed to open audio: {ex.Message}");
            }
        }

        public void Play()
        {
            if (_reader == null) return;

            lock (_stateLock)
            {
                if (_isPaused)
                {
                    _isPaused = false;
                    _isPlaying = true;
                    if (_openAlReady && _al != null)
                    {
                        _al.SourcePlay(_sourceId);
                    }
                    _progressTimer.Start();
                    PlaybackStateChanged?.Invoke(PlaybackState.Playing);
                    return;
                }

                if (_isPlaying) return;

                _shouldStop = false;
                _isPlaying = true;
                _isPaused = false;

                _playbackThread = new Thread(PlaybackLoop)
                {
                    IsBackground = true,
                    Name = "AudioPlaybackThread"
                };
                _playbackThread.Start();
                _progressTimer.Start();
                PlaybackStateChanged?.Invoke(PlaybackState.Playing);
            }
        }

        public void Pause()
        {
            lock (_stateLock)
            {
                if (_isPlaying && !_isPaused)
                {
                    _isPaused = true;
                    if (_openAlReady && _al != null)
                    {
                        _al.SourcePause(_sourceId);
                    }
                    _progressTimer.Stop();
                    PlaybackStateChanged?.Invoke(PlaybackState.Paused);
                }
            }
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                _shouldStop = true;
                _isPlaying = false;
                _isPaused = false;

                _progressTimer.Stop();

                if (_openAlReady && _al != null)
                {
                    try
                    {
                        _al.SourceStop(_sourceId);
                        int queued;
                        _al.GetSourceProperty(_sourceId, GetSourceInteger.BuffersQueued, out queued);
                        while (queued > 0)
                        {
                            uint unqueued;
                            unsafe
                            {
                                _al.SourceUnqueueBuffers(_sourceId, 1, &unqueued);
                            }
                            queued--;
                        }
                    }
                    catch { }
                }

                if (_reader != null)
                {
                    _reader.CurrentPosition = TimeSpan.Zero;
                }
                _tempoProvider?.Reset();

                ProgressChanged?.Invoke(TimeSpan.Zero, TotalTime);
                PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
            }
        }

        public void Seek(TimeSpan targetTime)
        {
            lock (_stateLock)
            {
                if (_reader != null)
                {
                    var clamped = targetTime < TimeSpan.Zero ? TimeSpan.Zero :
                                  targetTime > _reader.TotalDuration ? _reader.TotalDuration : targetTime;

                    _reader.CurrentPosition = clamped;
                    _tempoProvider?.Reset();

                    if (_openAlReady && _al != null)
                    {
                        try
                        {
                            _al.SourceStop(_sourceId);
                            int queued;
                            _al.GetSourceProperty(_sourceId, GetSourceInteger.BuffersQueued, out queued);
                            while (queued > 0)
                            {
                                uint unqueued;
                                unsafe
                                {
                                    _al.SourceUnqueueBuffers(_sourceId, 1, &unqueued);
                                }
                                queued--;
                            }
                            if (_isPlaying && !_isPaused)
                            {
                                _al.SourcePlay(_sourceId);
                            }
                        }
                        catch { }
                    }

                    ProgressChanged?.Invoke(clamped, TotalTime);
                }
            }
        }

        public void Skip(TimeSpan delta)
        {
            if (_reader != null)
            {
                Seek(_reader.CurrentPosition + delta);
            }
        }

        public void SetGainDb(double gainDb)
        {
            _gainLimiterProvider?.SetGainDb(gainDb);
        }

        public void SetTempoFactor(double tempoFactor)
        {
            _tempoProvider?.SetTempoFactor((float)tempoFactor);
        }

        public void SetPreventClipping(bool prevent)
        {
            _gainLimiterProvider?.SetPreventClipping(prevent);
        }

        public void UnloadAudio()
        {
            Stop();
            DisposeAudioPipeline();
            PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
        }

        private unsafe void PlaybackLoop()
        {
            if (_meteringProvider == null || _reader == null) return;

            int sampleRate = _reader.WaveFormat.SampleRate;
            int channels = _reader.WaveFormat.Channels;
            BufferFormat format = channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;

            int chunkFrames = sampleRate / 20; // 50 ms chunks
            var floatBuffer = new float[chunkFrames * channels];
            var pcmBuffer = new short[chunkFrames * channels];

            if (_openAlReady && _al != null)
            {
                // Pre-fill initial buffers
                for (int b = 0; b < 4; b++)
                {
                    int read = _meteringProvider.Read(floatBuffer, 0, floatBuffer.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        pcmBuffer[i] = (short)Math.Clamp(floatBuffer[i] * 32767.0f, -32768f, 32767f);
                    }

                    fixed (short* ptr = pcmBuffer)
                    {
                        _al.BufferData(_bufferIds[b], format, ptr, read * 2, sampleRate);
                    }
                    fixed (uint* bufPtr = &_bufferIds[b])
                    {
                        _al.SourceQueueBuffers(_sourceId, 1, bufPtr);
                    }
                }

                _al.SourcePlay(_sourceId);

                while (!_shouldStop)
                {
                    if (_isPaused)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    int processed;
                    _al.GetSourceProperty(_sourceId, GetSourceInteger.BuffersProcessed, out processed);

                    while (processed > 0 && !_shouldStop)
                    {
                        uint unqueuedBuffer;
                        _al.SourceUnqueueBuffers(_sourceId, 1, &unqueuedBuffer);
                        processed--;

                        int read = _meteringProvider.Read(floatBuffer, 0, floatBuffer.Length);
                        if (read > 0)
                        {
                            for (int i = 0; i < read; i++)
                            {
                                pcmBuffer[i] = (short)Math.Clamp(floatBuffer[i] * 32767.0f, -32768f, 32767f);
                            }

                            fixed (short* ptr = pcmBuffer)
                            {
                                _al.BufferData(unqueuedBuffer, format, ptr, read * 2, sampleRate);
                            }
                            _al.SourceQueueBuffers(_sourceId, 1, &unqueuedBuffer);

                            int state;
                            _al.GetSourceProperty(_sourceId, GetSourceInteger.SourceState, out state);
                            if (state != (int)SourceState.Playing && !_isPaused)
                            {
                                _al.SourcePlay(_sourceId);
                            }
                        }
                        else
                        {
                            // Audio finished
                            _shouldStop = true;
                            break;
                        }
                    }

                    Thread.Sleep(15);
                }
            }
            else
            {
                // Software clock timing simulation if audio device not connected
                while (!_shouldStop)
                {
                    if (_isPaused)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    int read = _meteringProvider.Read(floatBuffer, 0, floatBuffer.Length);
                    if (read <= 0) break;

                    Thread.Sleep(45);
                }
            }

            lock (_stateLock)
            {
                _isPlaying = false;
                _isPaused = false;
            }

            _progressTimer.Stop();
            PlaybackStateChanged?.Invoke(PlaybackState.Stopped);
        }

        private void DisposeAudioPipeline()
        {
            if (_reader != null)
            {
                try { _reader.Dispose(); } catch { }
                _reader = null;
            }
            _tempoProvider = null;
            _gainLimiterProvider = null;
            _meteringProvider = null;
        }

        public unsafe void Dispose()
        {
            Stop();
            DisposeAudioPipeline();
            _progressTimer.Dispose();

            if (_openAlReady && _al != null)
            {
                try
                {
                    _al.DeleteSource(_sourceId);
                    fixed (uint* ptr = _bufferIds)
                    {
                        _al.DeleteBuffers(4, ptr);
                    }
                    if (_context != null && _alc != null)
                    {
                        _alc.DestroyContext(_context);
                    }
                    if (_device != null && _alc != null)
                    {
                        _alc.CloseDevice(_device);
                    }
                }
                catch { }
            }
        }
    }

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
