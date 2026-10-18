// =================================================================================================
// FILE: AudioAnalysisResult.cs
// LOCATION: Mp3GainApp/Models/AudioAnalysisResult.cs
//
// WHAT THIS FILE DOES:
//   This is the data model storing the diagnostic test results of your MP3:
//     - Peak dBFS (the highest wave peak)
//     - RMS dBFS (average perceived loudness)
//     - Detected BPM (rhythmic tempo)
//     - Recommended Gain in dB
//     - Clipping status (whether distorted audio peaks were detected)
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel stores the results in its "AnalysisResult" property.
//   - MainWindow.xaml binds to this model to display the 4 diagnostic tiles on screen.
// =================================================================================================

namespace Mp3GainApp.Models
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

        // User-friendly text properties displayed on the cards
        public string PeakSummary => $"{PeakDbfs:F1} dBFS";
        public string RmsSummary => $"{RmsDbfs:F1} dBFS";
        public string LufsSummary => Lufs > -90.0 ? $"{Lufs:F1} LUFS" : $"{RmsDbfs:F1} RMS";
        public string BpmSummary => DetectedBpm > 0 ? $"{DetectedBpm:F1} BPM" : "BPM Unknown";
        public string StatusSummary => IsClipping ? "Clipping Detected!" : "Audio Dynamic Range Good";
    }
}
