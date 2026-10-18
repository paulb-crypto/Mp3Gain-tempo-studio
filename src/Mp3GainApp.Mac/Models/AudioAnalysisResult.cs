// =================================================================================================
// FILE: AudioAnalysisResult.cs
// LOCATION: Mp3GainApp.Mac/Models/AudioAnalysisResult.cs
//
// WHAT THIS FILE DOES:
//   Data model storing diagnostic test results of an MP3:
//     - Peak dBFS
//     - RMS dBFS
//     - ITU-R BS.1770 LUFS integrated loudness
//     - Detected BPM
//     - Recommended Gain in dB
//     - Clipping status
// =================================================================================================

using System;

namespace Mp3GainApp.Mac.Models
{
    public class AudioAnalysisResult
    {
        public float PeakLinear { get; set; }
        public double PeakDbfs { get; set; }
        public double RmsDbfs { get; set; }
        public double Lufs { get; set; }
        public double DetectedBpm { get; set; }
        public double RecommendedGainDb { get; set; }
        public bool IsClipping { get; set; }
        public int ClippedSampleCount { get; set; }
        public TimeSpan Duration { get; set; }
        public int TotalSamples { get; set; }

        public string PeakSummary => $"{PeakDbfs:F1} dBFS";
        public string RmsSummary => $"{RmsDbfs:F1} dBFS";
        public string LufsSummary => Lufs > -90.0 ? $"{Lufs:F1} LUFS" : $"{RmsDbfs:F1} RMS";
        public string BpmSummary => DetectedBpm > 0 ? $"{DetectedBpm:F1} BPM" : "BPM Unknown";
        public string StatusSummary => IsClipping ? "Clipping Detected!" : "Audio Dynamic Range Good";
    }
}

