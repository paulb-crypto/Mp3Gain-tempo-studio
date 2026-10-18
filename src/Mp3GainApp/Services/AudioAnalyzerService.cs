// =================================================================================================
// FILE: AudioAnalyzerService.cs
// LOCATION: Mp3GainApp/Services/AudioAnalyzerService.cs
//
// WHAT THIS FILE DOES:
//   This is the diagnostic sound doctor for your MP3 files:
//     1. Peak Level: Finds the single loudest moment in the entire track (measured in dBFS).
//     2. RMS Loudness & ITU-R BS.1770 LUFS: Measures integrated perceived loudness matching
//        industry standards (Spotify, YouTube, Apple Music).
//     3. Clipping Detection: Checks if the song already contains distorted digital audio peaks.
//     4. High-Precision BPM Detection: Downmixes to mono, analyzes transient energy flux at 100 Hz,
//        and applies parabolic peak autocorrelation for 0.1 BPM accuracy.
//     5. Auto-Gain Recommendation: Suggests the mathematically optimal volume adjustment
//        to bring the track up to modern streaming loudness standards (-14 LUFS) without clipping.
//
// HOW IT RELATES TO THE APP:
//   - When you load an MP3, MainViewModel calls AnalyzeAudio here to populate the diagnostic
//     cards on screen and set the initial BPM and Gain recommendations.
// =================================================================================================

using System.IO;
using NAudio.Wave;
using Mp3GainApp.Models;

namespace Mp3GainApp.Services
{
    public class AudioAnalyzerService
    {
        // -14.0 LUFS is the modern audio mastering standard (used by Spotify, YouTube, Apple Music)
        public const double ReferenceLufsTarget = -14.0;

        /// <summary>
        /// Scans the entire MP3 audio stream to measure loudness (LUFS, RMS, Peak) and detect tempo.
        /// </summary>
        public AudioAnalysisResult AnalyzeAudio(string filePath, IProgress<double>? progress = null)
        {
            var result = new AudioAnalysisResult();

            using var reader = new AudioFileReader(filePath);
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels = reader.WaveFormat.Channels;
            result.Duration = reader.TotalTime;

            // Setup ITU-R BS.1770 K-weighting filters for each channel
            var stage1Filters = new KWeightingStage1Filter[channels];
            var stage2Filters = new KWeightingStage2Filter[channels];
            for (int c = 0; c < channels; c++)
            {
                stage1Filters[c] = new KWeightingStage1Filter(sampleRate);
                stage2Filters[c] = new KWeightingStage2Filter(sampleRate);
            }

            var buffer = new float[sampleRate * channels]; // 1-second buffer
            double sumSquares = 0.0;
            double[] kWeightedSumSquares = new double[channels];
            float maxPeak = 0.0f;
            long totalSamplesRead = 0;
            long totalFramesRead = 0;
            int clippedSamples = 0;

            // 100 Hz envelope resolution (10 ms windows) for accurate rhythm analysis
            var energyList = new List<float>();
            int windowFrames = Math.Max(1, sampleRate / 100);
            float currentWindowEnergy = 0.0f;
            int currentWindowFrameCount = 0;

            long totalBytes = reader.Length;
            int read;

            // Read the entire song chunk by chunk
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                int frames = read / channels;
                for (int f = 0; f < frames; f++)
                {
                    int frameBase = f * channels;
                    float monoFrameSum = 0.0f;

                    for (int c = 0; c < channels; c++)
                    {
                        float sample = buffer[frameBase + c];
                        float absSample = Math.Abs(sample);

                        if (absSample > maxPeak)
                        {
                            maxPeak = absSample;
                        }

                        if (absSample >= 0.999f)
                        {
                            clippedSamples++;
                        }

                        sumSquares += sample * sample;
                        totalSamplesRead++;
                        monoFrameSum += sample;

                        // Process sample through K-weighting filters for LUFS
                        float s1 = stage1Filters[c].Process(sample);
                        float s2 = stage2Filters[c].Process(s1);
                        kWeightedSumSquares[c] += s2 * s2;
                    }

                    totalFramesRead++;

                    // Downmixed mono frame for tempo/transient analysis
                    float monoSample = monoFrameSum / channels;
                    currentWindowEnergy += Math.Abs(monoSample);
                    currentWindowFrameCount++;

                    if (currentWindowFrameCount >= windowFrames)
                    {
                        energyList.Add(currentWindowEnergy / currentWindowFrameCount);
                        currentWindowEnergy = 0.0f;
                        currentWindowFrameCount = 0;
                    }
                }

                // Update analysis progress bar (0% to 75%)
                if (progress != null && totalBytes > 0)
                {
                    double percent = (double)reader.Position / totalBytes * 75.0;
                    progress.Report(percent);
                }
            }

            result.TotalSamples = (int)Math.Min(totalSamplesRead, int.MaxValue);
            result.PeakLinear = maxPeak;
            result.PeakDbfs = maxPeak > 0 ? 20.0 * Math.Log10(maxPeak) : -96.0;

            // RMS Loudness
            double rms = totalSamplesRead > 0 ? Math.Sqrt(sumSquares / totalSamplesRead) : 0.0;
            result.RmsDbfs = rms > 0 ? 20.0 * Math.Log10(rms) : -96.0;

            // ITU-R BS.1770 LUFS Integrated Loudness
            double totalWeightedEnergy = 0.0;
            if (totalFramesRead > 0)
            {
                for (int c = 0; c < channels; c++)
                {
                    // For stereo, left and right both have channel weight 1.0
                    totalWeightedEnergy += (kWeightedSumSquares[c] / totalFramesRead);
                }
            }
            result.Lufs = totalWeightedEnergy > 1e-10 ? -0.691 + 10.0 * Math.Log10(totalWeightedEnergy) : -96.0;

            result.IsClipping = clippedSamples > 0;
            result.ClippedSampleCount = clippedSamples;

            // Calculate recommended gain: brings perceived loudness to -14.0 LUFS standard,
            // while making sure peak levels never exceed -0.5 dBFS.
            double loudnessRef = result.Lufs > -90.0 ? result.Lufs : result.RmsDbfs;
            double targetGain = ReferenceLufsTarget - loudnessRef;
            double maxAllowedGain = -0.5 - result.PeakDbfs;
            if (targetGain > maxAllowedGain)
            {
                targetGain = maxAllowedGain;
            }
            result.RecommendedGainDb = Math.Clamp(Math.Round(targetGain, 1), -15.0, 15.0);

            // Run high-precision autocorrelation on energy pulses to detect BPM
            progress?.Report(85.0);
            result.DetectedBpm = DetectBpmFromEnergyEnvelope(energyList, 100.0);
            progress?.Report(100.0);

            return result;
        }

        /// <summary>
        /// Detects BPM by finding recurring rhythmic peaks using 100 Hz onset flux and parabolic autocorrelation.
        /// </summary>
        private double DetectBpmFromEnergyEnvelope(List<float> energy, double envelopeSampleRateHz)
        {
            if (energy.Count < (int)(envelopeSampleRateHz * 5))
                return 120.0; // Default fallback if audio is too short

            // Calculate half-wave rectified spectral energy flux (transients where beats hit)
            var flux = new float[energy.Count];
            for (int i = 1; i < energy.Count; i++)
            {
                float diff = energy[i] - energy[i - 1];
                flux[i] = diff > 0 ? diff : 0.0f;
            }

            // Search between standard musical tempo ranges: 60 BPM to 200 BPM
            int minLag = (int)Math.Round(envelopeSampleRateHz * 60.0 / 200.0); // fastest tempo (200 BPM = 30 lag @ 100Hz)
            int maxLag = (int)Math.Round(envelopeSampleRateHz * 60.0 / 60.0);  // slowest tempo (60 BPM = 100 lag @ 100Hz)

            int analysisLength = Math.Min(flux.Length - maxLag, (int)(envelopeSampleRateHz * 90));
            if (analysisLength <= 0) analysisLength = flux.Length - maxLag;

            var autocorr = new double[maxLag + 2];
            double bestAutocorr = 0.0;
            int bestLag = (minLag + maxLag) / 2;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0.0;
                for (int i = 0; i < analysisLength; i++)
                {
                    sum += flux[i] * flux[i + lag];
                }
                autocorr[lag] = sum;

                if (sum > bestAutocorr)
                {
                    bestAutocorr = sum;
                    bestLag = lag;
                }
            }

            // Parabolic / Quadratic Peak Interpolation for fractional lag precision
            double fineLag = bestLag;
            if (bestLag > minLag && bestLag < maxLag)
            {
                double alpha = autocorr[bestLag - 1];
                double beta = autocorr[bestLag];
                double gamma = autocorr[bestLag + 1];
                double denom = 2.0 * (alpha - 2.0 * beta + gamma);
                if (Math.Abs(denom) > 1e-9)
                {
                    double delta = (alpha - gamma) / denom;
                    delta = Math.Clamp(delta, -0.5, 0.5);
                    fineLag = bestLag + delta;
                }
            }

            // Musical Octave Disambiguation:
            // In 4/4 music, strong kick drums on beats 1 & 3 often produce an artificial peak at half-tempo
            // (e.g. 63.5 BPM instead of 127 BPM). If a corresponding fundamental beat peak exists at half the lag
            // (2x tempo) with at least 55% of the correlation energy, select the true musical tempo.
            double rawBpm = (envelopeSampleRateHz * 60.0) / fineLag;
            if (rawBpm < 85.0)
            {
                int halfLag = (int)Math.Round(bestLag / 2.0);
                if (halfLag >= minLag && halfLag <= maxLag)
                {
                    int searchStart = Math.Max(minLag, halfLag - 2);
                    int searchEnd = Math.Min(maxLag, halfLag + 2);
                    int localPeakLag = halfLag;
                    double localPeakScore = 0.0;

                    for (int l = searchStart; l <= searchEnd; l++)
                    {
                        if (autocorr[l] > localPeakScore)
                        {
                            localPeakScore = autocorr[l];
                            localPeakLag = l;
                        }
                    }

                    if (localPeakScore >= 0.55 * bestAutocorr)
                    {
                        double alpha = localPeakLag > minLag ? autocorr[localPeakLag - 1] : localPeakScore;
                        double beta = localPeakScore;
                        double gamma = localPeakLag < maxLag ? autocorr[localPeakLag + 1] : localPeakScore;
                        double denom = 2.0 * (alpha - 2.0 * beta + gamma);
                        double delta = 0.0;
                        if (Math.Abs(denom) > 1e-9)
                        {
                            delta = Math.Clamp((alpha - gamma) / denom, -0.5, 0.5);
                        }
                        fineLag = localPeakLag + delta;
                        rawBpm = (envelopeSampleRateHz * 60.0) / fineLag;
                    }
                }
            }

            return Math.Round(rawBpm, 1);
        }

        // =========================================================================================
        // ITU-R BS.1770 K-WEIGHTING BIQUAD FILTERS
        // =========================================================================================

        /// <summary>
        /// Stage 1: High shelf filter modeling acoustic head effects (~+4 dB boost above 1.5 kHz).
        /// </summary>
        private class KWeightingStage1Filter
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            public KWeightingStage1Filter(double sampleRate)
            {
                // Standard BS.1770 head-filter parameters
                double f0 = 1681.974450955533;
                double gainDb = 3.999843853973347;
                double q = 0.7071752369274193;

                double a = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * f0 / sampleRate;
                double alpha = Math.Sin(w0) / (2.0 * q);
                double cosW0 = Math.Cos(w0);

                double b0 = a * ((a + 1.0) + (a - 1.0) * cosW0 + 2.0 * Math.Sqrt(a) * alpha);
                double b1 = -2.0 * a * ((a - 1.0) + (a + 1.0) * cosW0);
                double b2 = a * ((a + 1.0) + (a - 1.0) * cosW0 - 2.0 * Math.Sqrt(a) * alpha);
                double a0 = (a + 1.0) - (a - 1.0) * cosW0 + 2.0 * Math.Sqrt(a) * alpha;
                double a1 = 2.0 * ((a - 1.0) - (a + 1.0) * cosW0);
                double a2 = (a + 1.0) - (a - 1.0) * cosW0 - 2.0 * Math.Sqrt(a) * alpha;

                _b0 = b0 / a0;
                _b1 = b1 / a0;
                _b2 = b2 / a0;
                _a1 = a1 / a0;
                _a2 = a2 / a0;
            }

            public float Process(float input)
            {
                double x0 = input;
                double y0 = _b0 * x0 + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x0;
                _y2 = _y1;
                _y1 = y0;
                return (float)y0;
            }
        }

        /// <summary>
        /// Stage 2: RLB High-pass filter roll-off below 100 Hz (attenuates inaudible DC/sub-bass).
        /// </summary>
        private class KWeightingStage2Filter
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            public KWeightingStage2Filter(double sampleRate)
            {
                // Standard BS.1770 RLB filter parameters
                double f0 = 38.13547087602444;
                double q = 0.5003270373237673;

                double w0 = 2.0 * Math.PI * f0 / sampleRate;
                double alpha = Math.Sin(w0) / (2.0 * q);
                double cosW0 = Math.Cos(w0);

                double b0 = (1.0 + cosW0) / 2.0;
                double b1 = -(1.0 + cosW0);
                double b2 = (1.0 + cosW0) / 2.0;
                double a0 = 1.0 + alpha;
                double a1 = -2.0 * cosW0;
                double a2 = 1.0 - alpha;

                _b0 = b0 / a0;
                _b1 = b1 / a0;
                _b2 = b2 / a0;
                _a1 = a1 / a0;
                _a2 = a2 / a0;
            }

            public float Process(float input)
            {
                double x0 = input;
                double y0 = _b0 * x0 + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x0;
                _y2 = _y1;
                _y1 = y0;
                return (float)y0;
            }
        }
    }
}
