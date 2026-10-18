// =================================================================================================
// FILE: AudioFileInfo.cs
// LOCATION: Mp3GainApp.Mac/Models/AudioFileInfo.cs
//
// WHAT THIS FILE DOES:
//   Data model representing an opened MP3 file.
//   Stores file name, file size, duration, sample rate, bitrate, and ID3 tags.
//   Uses Avalonia.Media.Imaging.Bitmap for cross-platform album art display.
// =================================================================================================

using System;
using Avalonia.Media.Imaging;

namespace Mp3GainApp.Mac.Models
{
    public class AudioFileInfo
    {
        // File system information
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
        
        // ID3 Metadata
        public string Title { get; set; } = "Unknown Title";
        public string Artist { get; set; } = "Unknown Artist";
        public string Album { get; set; } = "Unknown Album";
        public uint Year { get; set; }
        public string Genre { get; set; } = "Unknown";
        public uint TrackNumber { get; set; }
        public double OriginalBpm { get; set; }
        
        // Album Artwork image
        public Bitmap? AlbumArt { get; set; }
        public byte[]? AlbumArtBytes { get; set; }
        public string? AlbumArtMimeType { get; set; }
        
        // Technical audio parameters
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitrateKbps { get; set; }
    }
}

