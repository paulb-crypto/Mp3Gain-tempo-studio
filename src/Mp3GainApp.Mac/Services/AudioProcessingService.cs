// =================================================================================================
// FILE: AudioProcessingService.cs
// LOCATION: Mp3GainApp.Mac/Services/AudioProcessingService.cs
//
// WHAT THIS FILE DOES:
//   Cross-platform audio processing and MP3 rendering engine:
//     1. Studio-grade WSOLA pitch-preserving time-stretching with phase peak alignment.
//     2. Safe element-by-element ring buffering.
//     3. Soft-knee anti-clipping limiter.
//     4. High-bitrate MP3 encoding via bundled or system ffmpeg/lame with fallback.
//     5. Full ID3 tag and album artwork preservation.
// =================================================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NLayer;
using Mp3GainApp.Mac.Models;

namespace Mp3GainApp.Mac.Services
{
    public class AudioProcessingService
    {
        private readonly MetadataService _metadataService;
        private readonly BackupService _backupService;

        public AudioProcessingService(MetadataService metadataService, BackupService backupService)
        {
            _metadataService = metadataService;
            _backupService = backupService;
        }

        public async Task ProcessAndSaveMp3Async(
            string sourceFilePath,
            string targetFilePath,
            double gainDb,
            double tempoFactor,
            double targetBpm,
            bool preventClipping,
            int targetBitrate = 320,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                bool isSameFile = string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(targetFilePath), StringComparison.OrdinalIgnoreCase);

                if (isSameFile)
                {
                    _backupService.CreateBackupIfNeeded(sourceFilePath);
                }

                string backupPath = _backupService.GetBackupPath(sourceFilePath);
                string renderPath = isSameFile ? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3") : targetFilePath;
                string tempWavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

                var targetDir = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (targetBitrate <= 0)
                {
                    targetBitrate = 320;
                    try
                    {
                        string bitrateSource = File.Exists(backupPath) ? backupPath : sourceFilePath;
                        var meta = _metadataService.ReadAudioFileInfo(bitrateSource);
                        if (meta.BitrateKbps > 0)
                        {
                            targetBitrate = Math.Max(meta.BitrateKbps, 256);
                        }
                    }
                    catch { targetBitrate = 320; }
                }

                try
                {
                    // STEP 1: Render processed PCM samples into high-quality 16-bit WAV
                    using (var reader = new NLayerSampleProvider(sourceFilePath))
                    {
                        var sampleRate = reader.WaveFormat.SampleRate;
                        var channels = reader.WaveFormat.Channels;

                        ISampleProvider pipeline = reader;
                        if (Math.Abs(tempoFactor - 1.0) > 0.005)
                        {
                            pipeline = new WsolaTempoSampleProvider(pipeline, (float)tempoFactor);
                        }

                        var gainLimiterProvider = new GainAndLimiterSampleProvider(pipeline, gainDb, preventClipping);

                        using (var wavFs = File.Create(tempWavPath))
                        using (var bw = new BinaryWriter(wavFs))
                        {
                            // Write placeholder WAV header
                            WriteWavHeader(bw, sampleRate, channels, 0);

                            var floatBuffer = new float[sampleRate * channels];
                            var byteBuffer = new byte[floatBuffer.Length * 2];
                            long totalSamplesWritten = 0;
                            long estimatedTotalSamples = (long)(reader.TotalDuration.TotalSeconds * sampleRate * channels / tempoFactor);

                            int samplesRead;
                            while ((samplesRead = gainLimiterProvider.Read(floatBuffer, 0, floatBuffer.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                int byteCount = samplesRead * 2;
                                if (byteBuffer.Length < byteCount)
                                {
                                    byteBuffer = new byte[byteCount];
                                }

                                for (int i = 0; i < samplesRead; i++)
                                {
                                    short pcmSample = (short)Math.Clamp(floatBuffer[i] * 32767.0f, -32768f, 32767f);
                                    byteBuffer[i * 2] = (byte)(pcmSample & 0xFF);
                                    byteBuffer[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
                                }

                                bw.Write(byteBuffer, 0, byteCount);
                                totalSamplesWritten += samplesRead;

                                if (progress != null && estimatedTotalSamples > 0)
                                {
                                    double percent = Math.Min(80.0, (double)totalSamplesWritten / estimatedTotalSamples * 80.0);
                                    progress.Report(percent);
                                }
                            }

                            // Finalize WAV header with actual data length
                            uint dataLength = (uint)(totalSamplesWritten * 2);
                            bw.Seek(0, SeekOrigin.Begin);
                            WriteWavHeader(bw, sampleRate, channels, dataLength);
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(85.0);

                    // STEP 2: Encode WAV to high-bitrate MP3
                    EncodeWavToMp3(tempWavPath, renderPath, targetBitrate);
                    progress?.Report(95.0);

                    // STEP 3: Replace target file if rendering to temp
                    if (isSameFile)
                    {
                        File.Copy(renderPath, targetFilePath, true);
                    }

                    // STEP 4: Copy and persist all ID3 tags, artwork, and BPM
                    string tagSource = File.Exists(backupPath) ? backupPath : sourceFilePath;
                    _metadataService.CopyAndPersistTags(tagSource, targetFilePath, targetBpm, gainDb);

                    progress?.Report(100.0);
                }
                finally
                {
                    if (File.Exists(tempWavPath))
                    {
                        try { File.Delete(tempWavPath); } catch { }
                    }
                    if (isSameFile && File.Exists(renderPath))
                    {
                        try { File.Delete(renderPath); } catch { }
                    }
                }
            }, cancellationToken);
        }

        private static void WriteWavHeader(BinaryWriter bw, int sampleRate, int channels, uint dataBytes)
        {
            uint totalChunkSize = 36 + dataBytes;
            ushort bitsPerSample = 16;
            ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
            uint byteRate = (uint)(sampleRate * blockAlign);

            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(totalChunkSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write((uint)16);           // Subchunk1Size
            bw.Write((ushort)1);           // AudioFormat (PCM = 1)
            bw.Write((ushort)channels);
            bw.Write((uint)sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write(bitsPerSample);

            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataBytes);
        }

        private static void EncodeWavToMp3(string wavPath, string mp3Path, int bitrateKbps)
        {
            string? encoderPath = FindEncoderBinary();
            if (encoderPath != null)
            {
                string exeName = Path.GetFileName(encoderPath).ToLowerInvariant();
                string arguments = exeName.Contains("lame")
                    ? $"-b {bitrateKbps} \"{wavPath}\" \"{mp3Path}\""
                    : $"-y -i \"{wavPath}\" -codec:a libmp3lame -b:a {bitrateKbps}k \"{mp3Path}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = encoderPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(startInfo);
                if (proc != null)
                {
                    proc.WaitForExit();
                    if (proc.ExitCode == 0 && File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0)
                    {
                        return;
                    }
                }
            }

            // Fallback: If no encoder binary available, copy WAV to MP3 or instruct user
            throw new InvalidOperationException("MP3 encoder (ffmpeg or lame) was not found. Please install ffmpeg using 'brew install ffmpeg' or place ffmpeg in the application folder.");
        }

        private static string? FindEncoderBinary()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string resourcesDir = Path.Combine(appDir, "..", "Resources");

            string[] candidates =
            {
                Path.Combine(appDir, "ffmpeg"),
                Path.Combine(appDir, "lame"),
                Path.Combine(resourcesDir, "ffmpeg"),
                Path.Combine(resourcesDir, "lame"),
                "/opt/homebrew/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/opt/homebrew/bin/lame",
                "/usr/local/bin/lame",
                "/usr/bin/ffmpeg",
                "/usr/bin/lame"
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                        return path;
                }
                catch { }
            }

            // Check PATH via which
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "ffmpeg",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }

            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "lame",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }

            return null;
        }
    }

    public class NLayerSampleProvider : ISampleProvider, IDisposable
    {
        private readonly MpegFile _mpegFile;
        public WaveFormat WaveFormat { get; }
        public TimeSpan TotalDuration => _mpegFile.Duration;
        public TimeSpan CurrentPosition
        {
            get => _mpegFile.Time;
            set => _mpegFile.Time = value;
        }

        public NLayerSampleProvider(string filePath)
        {
            _mpegFile = new MpegFile(filePath);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpegFile.SampleRate, _mpegFile.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return _mpegFile.ReadSamples(buffer, offset, count);
        }

        public void Dispose()
        {
            _mpegFile.Dispose();
        }
    }

    public class GainAndLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile float _gainMultiplier;
        private volatile bool _preventClipping;

        public WaveFormat WaveFormat => _source.WaveFormat;
        public bool HasClipped { get; private set; }
        public int ClippedSampleCount { get; private set; }

        public GainAndLimiterSampleProvider(ISampleProvider source, double gainDb, bool preventClipping = true)
        {
            _source = source;
            SetGainDb(gainDb);
            _preventClipping = preventClipping;
        }

        public void SetGainDb(double gainDb)
        {
            _gainMultiplier = (float)Math.Pow(10.0, gainDb / 20.0);
        }

        public void SetPreventClipping(bool prevent)
        {
            _preventClipping = prevent;
        }

        public void ResetClippingStats()
        {
            HasClipped = false;
            ClippedSampleCount = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float gain = _gainMultiplier;
            bool preventClipping = _preventClipping;
            const float threshold = 0.95f;

            for (int i = 0; i < read; i++)
            {
                int idx = offset + i;
                float sample = buffer[idx] * gain;
                float absSample = Math.Abs(sample);

                if (absSample >= 1.0f)
                {
                    HasClipped = true;
                    ClippedSampleCount++;
                }

                if (preventClipping)
                {
                    if (absSample > threshold)
                    {
                        float excess = absSample - threshold;
                        float compressed = threshold + (float)Math.Tanh(excess * 2.0f) * (1.0f - threshold);
                        sample = sample < 0 ? -compressed : compressed;
                    }
                    sample = Math.Clamp(sample, -0.999f, 0.999f);
                }

                buffer[idx] = sample;
            }

            return read;
        }
    }

    public class WsolaTempoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly int _sampleRate;
        private volatile float _tempoFactor = 1.0f;

        private readonly int _hopFrames;
        private readonly int _hopSamples;
        private readonly int _searchFrames;
        private readonly float[] _fadeWindow;
        private readonly CircularSampleBuffer _inputBuffer;
        private readonly CircularSampleBuffer _outputBuffer;
        private readonly float[] _sourceReadBuffer;
        private readonly float[] _tempHopBuffer;
        private readonly object _syncLock = new();

        private int _prevGrainFrameOffset = 0;
        private bool _isInitialized = false;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public WsolaTempoSampleProvider(ISampleProvider source, float tempoFactor)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;
            SetTempoFactor(tempoFactor);

            _hopFrames = Math.Max(256, _sampleRate / 50);
            _hopSamples = _hopFrames * _channels;
            _searchFrames = Math.Max(128, (_sampleRate * 12) / 1000);

            _fadeWindow = new float[_hopFrames];
            for (int f = 0; f < _hopFrames; f++)
            {
                _fadeWindow[f] = 0.5f * (1.0f - (float)Math.Cos(Math.PI * f / _hopFrames));
            }

            int bufferCapacity = Math.Max(_hopSamples * 64, _sampleRate * _channels * 3);
            _inputBuffer = new CircularSampleBuffer(bufferCapacity);
            _outputBuffer = new CircularSampleBuffer(bufferCapacity);
            _sourceReadBuffer = new float[_hopSamples * 8];
            _tempHopBuffer = new float[_hopSamples];
        }

        public void SetTempoFactor(float tempoFactor)
        {
            _tempoFactor = Math.Clamp(tempoFactor, 0.5f, 2.0f);
        }

        public void Reset()
        {
            lock (_syncLock)
            {
                _inputBuffer.Clear();
                _outputBuffer.Clear();
                _prevGrainFrameOffset = 0;
                _isInitialized = false;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            float tempo = _tempoFactor;
            if (Math.Abs(tempo - 1.0f) < 0.005f && _outputBuffer.AvailableSamples == 0 && _inputBuffer.AvailableSamples == 0)
            {
                return _source.Read(buffer, offset, count);
            }

            lock (_syncLock)
            {
                int samplesDelivered = 0;

                while (samplesDelivered < count)
                {
                    if (_outputBuffer.AvailableSamples > 0)
                    {
                        int readOut = _outputBuffer.Read(buffer, offset + samplesDelivered, count - samplesDelivered);
                        samplesDelivered += readOut;
                        if (samplesDelivered >= count) break;
                    }

                    int analysisHop = (int)Math.Round(_hopFrames * tempo);
                    analysisHop = Math.Max(1, analysisHop);

                    int maxLookaheadFrames = _prevGrainFrameOffset + Math.Max(_hopFrames, analysisHop + _searchFrames) + _hopFrames + 64;
                    int requiredInputSamples = maxLookaheadFrames * _channels;

                    while (_inputBuffer.AvailableSamples < requiredInputSamples)
                    {
                        int readFromSrc = _source.Read(_sourceReadBuffer, 0, _sourceReadBuffer.Length);
                        if (readFromSrc <= 0)
                        {
                            if (_inputBuffer.AvailableSamples > 0)
                            {
                                int flush = Math.Min(_inputBuffer.AvailableSamples, count - samplesDelivered);
                                int readFlushed = _inputBuffer.Read(buffer, offset + samplesDelivered, flush);
                                samplesDelivered += readFlushed;
                            }
                            return samplesDelivered;
                        }
                        _inputBuffer.Write(_sourceReadBuffer, 0, readFromSrc);
                    }

                    if (!_isInitialized)
                    {
                        for (int i = 0; i < _hopSamples; i++)
                        {
                            _tempHopBuffer[i] = _inputBuffer.Peek(i);
                        }
                        _outputBuffer.Write(_tempHopBuffer, 0, _hopSamples);
                        _prevGrainFrameOffset = 0;
                        _isInitialized = true;
                        continue;
                    }

                    int contFrame = _prevGrainFrameOffset + _hopFrames;
                    int targetFrame = _prevGrainFrameOffset + analysisHop;

                    int minDelta = -Math.Min(_searchFrames, targetFrame);
                    int maxDelta = _searchFrames;
                    int bestCandFrame = targetFrame;
                    double bestScore = -double.MaxValue;

                    int contSampleOffset = contFrame * _channels;

                    for (int delta = minDelta; delta <= maxDelta; delta += 2)
                    {
                        int candFrame = targetFrame + delta;
                        int candSampleOffset = candFrame * _channels;

                        double crossCorr = 0.0;
                        double candNorm = 0.0;
                        double contNorm = 0.0;

                        for (int k = 0; k < _hopSamples; k += _channels)
                        {
                            for (int c = 0; c < _channels; c++)
                            {
                                float sCont = _inputBuffer.Peek(contSampleOffset + k + c);
                                float sCand = _inputBuffer.Peek(candSampleOffset + k + c);
                                crossCorr += sCont * sCand;
                                contNorm += sCont * sCont;
                                candNorm += sCand * sCand;
                            }
                        }

                        double norm = Math.Sqrt(contNorm * candNorm);
                        double score = norm > 1e-6 ? (crossCorr / norm) : crossCorr;
                        score *= (1.0 - 0.25 * Math.Abs(delta) / (_searchFrames + 1));

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCandFrame = candFrame;
                        }
                    }

                    int fineCenter = bestCandFrame;
                    for (int fineOffset = -1; fineOffset <= 1; fineOffset += 2)
                    {
                        int candFrame = fineCenter + fineOffset;
                        if (candFrame < 0) continue;

                        int candSampleOffset = candFrame * _channels;
                        double crossCorr = 0.0;
                        double candNorm = 0.0;
                        double contNorm = 0.0;

                        for (int k = 0; k < _hopSamples; k += _channels)
                        {
                            for (int c = 0; c < _channels; c++)
                            {
                                float sCont = _inputBuffer.Peek(contSampleOffset + k + c);
                                float sCand = _inputBuffer.Peek(candSampleOffset + k + c);
                                crossCorr += sCont * sCand;
                                contNorm += sCont * sCont;
                                candNorm += sCand * sCand;
                            }
                        }

                        double norm = Math.Sqrt(contNorm * candNorm);
                        double score = norm > 1e-6 ? (crossCorr / norm) : crossCorr;
                        int delta = candFrame - targetFrame;
                        score *= (1.0 - 0.25 * Math.Abs(delta) / (_searchFrames + 1));

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCandFrame = candFrame;
                        }
                    }

                    int bestCandSampleOffset = bestCandFrame * _channels;
                    for (int f = 0; f < _hopFrames; f++)
                    {
                        float w = _fadeWindow[f];
                        float invW = 1.0f - w;
                        for (int c = 0; c < _channels; c++)
                        {
                            float sCont = _inputBuffer.Peek(contSampleOffset + f * _channels + c);
                            float sCand = _inputBuffer.Peek(bestCandSampleOffset + f * _channels + c);
                            _tempHopBuffer[f * _channels + c] = (invW * sCont) + (w * sCand);
                        }
                    }

                    _outputBuffer.Write(_tempHopBuffer, 0, _hopSamples);
                    _prevGrainFrameOffset = bestCandFrame;

                    int earliestNeededFrame = Math.Max(0, Math.Min(contFrame, bestCandFrame) - 32);
                    if (earliestNeededFrame > 128)
                    {
                        int discardSamples = earliestNeededFrame * _channels;
                        _inputBuffer.Discard(discardSamples);
                        _prevGrainFrameOffset -= earliestNeededFrame;
                    }
                }

                return samplesDelivered;
            }
        }
    }

    public class CircularSampleBuffer
    {
        private readonly float[] _buffer;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private int _count;
        private readonly object _lock = new object();

        public int AvailableSamples => _count;

        public CircularSampleBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new float[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _tail = 0;
                _count = 0;
                Array.Clear(_buffer, 0, _capacity);
            }
        }

        public void Write(float[] source, int offset, int count)
        {
            lock (_lock)
            {
                int writeCount = Math.Min(count, _capacity - _count);
                for (int i = 0; i < writeCount; i++)
                {
                    _buffer[_tail] = source[offset + i];
                    _tail = (_tail + 1) % _capacity;
                }
                _count += writeCount;
            }
        }

        public int Read(float[] destination, int offset, int count)
        {
            lock (_lock)
            {
                int readCount = Math.Min(count, _count);
                for (int i = 0; i < readCount; i++)
                {
                    destination[offset + i] = _buffer[_head];
                    _head = (_head + 1) % _capacity;
                }
                _count -= readCount;
                return readCount;
            }
        }

        public float Peek(int indexFromHead)
        {
            lock (_lock)
            {
                if (indexFromHead >= _count) return 0.0f;
                int idx = (_head + indexFromHead) % _capacity;
                return _buffer[idx];
            }
        }

        public void PeekRange(float[] destination, int indexFromHead, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    if (indexFromHead + i < _count)
                    {
                        int idx = (_head + indexFromHead + i) % _capacity;
                        destination[i] = _buffer[idx];
                    }
                    else
                    {
                        destination[i] = 0.0f;
                    }
                }
            }
        }

        public void Discard(int count)
        {
            lock (_lock)
            {
                int discardCount = Math.Min(count, _count);
                _head = (_head + discardCount) % _capacity;
                _count -= discardCount;
            }
        }
    }
}

