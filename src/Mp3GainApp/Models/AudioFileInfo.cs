// =================================================================================================
// FILE: AudioFileInfo.cs
// LOCATION: Mp3GainApp/Models/AudioFileInfo.cs
//
// WHAT THIS FILE DOES:
//   This is the data model representing an opened MP3 file.
//   It stores the file name, file size, duration, sample rate, bitrate, and all ID3 tags
//   (Title, Artist, Album, Year, Genre, Track Number, and the embedded Album Artwork picture).
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel stores the active AudioFileInfo in its "CurrentFile" property.
//   - MainWindow.xaml binds to CurrentFile to display your album cover image and song title.
// =================================================================================================

using System.Windows.Media.Imaging;

namespace Mp3GainApp.Models
{
    public class AudioFileInfo
    {
        // File system information
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
        
        // ID3 Metadata (Song details)
        public string Title { get; set; } = "Unknown Title";
        public string Artist { get; set; } = "Unknown Artist";
        public string Album { get; set; } = "Unknown Album";
        public uint Year { get; set; }
        public string Genre { get; set; } = "Unknown";
        public uint TrackNumber { get; set; }
        public double OriginalBpm { get; set; }
        
        // Album Artwork image for the user interface
        public BitmapImage? AlbumArt { get; set; }
        public byte[]? AlbumArtBytes { get; set; }
        public string? AlbumArtMimeType { get; set; }
        
        // Technical audio parameters
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitrateKbps { get; set; }
    }
}
