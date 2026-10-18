// =================================================================================================
// FILE: MetadataService.cs
// LOCATION: Mp3GainApp.Mac/Services/MetadataService.cs
//
// WHAT THIS FILE DOES:
//   Cross-platform ID3 tag & album artwork reader and persister using TagLib#.
//   Preserves 100% of metadata (artwork, lyrics, year, genre) and updates TBPM.
// =================================================================================================

using System;
using System.IO;
using Avalonia.Media.Imaging;
using Mp3GainApp.Mac.Models;

namespace Mp3GainApp.Mac.Services
{
    public class MetadataService
    {
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
                
                info.Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tagFile.Tag.Title;
                info.Artist = string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer) ? "Unknown Artist" : tagFile.Tag.FirstPerformer;
                info.Album = string.IsNullOrWhiteSpace(tagFile.Tag.Album) ? "Unknown Album" : tagFile.Tag.Album;
                info.Year = tagFile.Tag.Year;
                info.Genre = string.IsNullOrWhiteSpace(tagFile.Tag.FirstGenre) ? "Unknown" : tagFile.Tag.FirstGenre;
                info.TrackNumber = tagFile.Tag.Track;
                info.OriginalBpm = tagFile.Tag.BeatsPerMinute;

                if (tagFile.Properties != null)
                {
                    info.Duration = tagFile.Properties.Duration;
                    info.SampleRate = tagFile.Properties.AudioSampleRate;
                    info.Channels = tagFile.Properties.AudioChannels;
                    info.BitrateKbps = tagFile.Properties.AudioBitrate;
                }

                if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    info.AlbumArtBytes = pic.Data.Data;
                    info.AlbumArtMimeType = pic.MimeType;

                    using var ms = new MemoryStream(info.AlbumArtBytes);
                    info.AlbumArt = new Bitmap(ms);
                }
            }
            catch
            {
                info.Title = Path.GetFileNameWithoutExtension(filePath);
            }

            return info;
        }

        public void CopyAndPersistTags(string sourceFilePath, string targetFilePath, double newBpm, double gainDb)
        {
            using var sourceTagFile = TagLib.File.Create(sourceFilePath);
            using var targetTagFile = TagLib.File.Create(targetFilePath);

            sourceTagFile.Tag.CopyTo(targetTagFile.Tag, true);

            if (newBpm > 0)
            {
                targetTagFile.Tag.BeatsPerMinute = (uint)Math.Round(newBpm);
            }

            if (sourceTagFile.Tag.Pictures != null && sourceTagFile.Tag.Pictures.Length > 0)
            {
                targetTagFile.Tag.Pictures = sourceTagFile.Tag.Pictures;
            }

            targetTagFile.Save();
        }
    }
}

