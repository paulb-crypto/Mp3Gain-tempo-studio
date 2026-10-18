// =================================================================================================
// FILE: MetadataService.cs
// LOCATION: Mp3GainApp/Services/MetadataService.cs
//
// WHAT THIS FILE DOES:
//   This is the metadata guardian for your MP3 files using TagLib#.
//   It reads the ID3 tags (Title, Artist, Album, Year, Genre, Track Number, and Cover Artwork).
//   When saving, it guarantees that 100% of your tags and embedded artwork are preserved,
//   and it writes your new BPM value into the official ID3 "TBPM" tag.
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel calls ReadAudioFileInfo when loading a song to show your track info & cover art.
//   - AudioProcessingService calls CopyAndPersistTags when saving so your MP3 keeps all its metadata.
// =================================================================================================

using System.IO;
using System.Windows.Media.Imaging;
using Mp3GainApp.Models;

namespace Mp3GainApp.Services
{
    public class MetadataService
    {
        /// <summary>
        /// Reads song information and extracts the embedded album artwork for the screen.
        /// </summary>
        public AudioFileInfo ReadAudioFileInfo(string filePath)
        {
            var file = new FileInfo(filePath);
            var info = new AudioFileInfo
            {
                FilePath = filePath,
                FileName = file.Name,
                FileSizeBytes = file.Length
            };

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                
                // Read text tags
                info.Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tagFile.Tag.Title;
                info.Artist = string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer) ? "Unknown Artist" : tagFile.Tag.FirstPerformer;
                info.Album = string.IsNullOrWhiteSpace(tagFile.Tag.Album) ? "Unknown Album" : tagFile.Tag.Album;
                info.Year = tagFile.Tag.Year;
                info.Genre = string.IsNullOrWhiteSpace(tagFile.Tag.FirstGenre) ? "Unknown" : tagFile.Tag.FirstGenre;
                info.TrackNumber = tagFile.Tag.Track;
                info.OriginalBpm = tagFile.Tag.BeatsPerMinute;

                // Read technical audio stream properties
                if (tagFile.Properties != null)
                {
                    info.Duration = tagFile.Properties.Duration;
                    info.SampleRate = tagFile.Properties.AudioSampleRate;
                    info.Channels = tagFile.Properties.AudioChannels;
                    info.BitrateKbps = tagFile.Properties.AudioBitrate;
                }

                // Extract Album Artwork image
                if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    info.AlbumArtBytes = pic.Data.Data;
                    info.AlbumArtMimeType = pic.MimeType;

                    using var ms = new MemoryStream(info.AlbumArtBytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Makes the image safe to display across background threads
                    info.AlbumArt = bitmap;
                }
            }
            catch
            {
                // If a file has corrupted or missing tags, fallback to the file name
                info.Title = Path.GetFileNameWithoutExtension(filePath);
            }

            return info;
        }

        /// <summary>
        /// Copies all ID3 tags and album cover pictures from the original song to the newly saved file,
        /// and updates the Beats Per Minute (BPM) tag so music players (like Traktor, Rekordbox, Apple Music)
        /// recognize the new tempo.
        /// </summary>
        public void CopyAndPersistTags(string sourceFilePath, string targetFilePath, double newBpm, double gainDb)
        {
            using var sourceTagFile = TagLib.File.Create(sourceFilePath);
            using var targetTagFile = TagLib.File.Create(targetFilePath);

            // Copy all tags across without losing anything
            sourceTagFile.Tag.CopyTo(targetTagFile.Tag, true);

            // Remove any old static TLEN (Track Length) frame so Windows Explorer and media players
            // read the actual new audio duration from the physical audio stream
            if (targetTagFile.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
            {
                id3v2.RemoveFrames("TLEN");
            }

            // Persist the new BPM into the official ID3 TBPM tag
            if (newBpm > 0)
            {
                targetTagFile.Tag.BeatsPerMinute = (uint)Math.Round(newBpm);
            }

            // Ensure cover artwork is preserved intact
            if (sourceTagFile.Tag.Pictures != null && sourceTagFile.Tag.Pictures.Length > 0)
            {
                targetTagFile.Tag.Pictures = sourceTagFile.Tag.Pictures;
            }

            // Save the ID3 header directly into the MP3 file
            targetTagFile.Save();
        }
    }
}
