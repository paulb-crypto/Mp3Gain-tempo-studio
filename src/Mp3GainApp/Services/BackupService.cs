// =================================================================================================
// FILE: BackupService.cs
// LOCATION: Mp3GainApp/Services/BackupService.cs
//
// WHAT THIS FILE DOES:
//   This is your safety net. Whenever an MP3 file is about to be modified, this service creates
//   an untouched byte-for-byte copy named "<filename>.original.bak" in the same folder.
//   It powers the 1-click "Revert to Original File" button so you can always undo your changes.
//
// HOW IT RELATES TO THE APP:
//   - MainViewModel checks HasBackup to enable the Revert button and shield badge.
//   - When you click "Revert", this service copies the untouched backup back over your file.
// =================================================================================================

using System.IO;

namespace Mp3GainApp.Services
{
    public class BackupService
    {
        // FIX: The backup extension must keep ".mp3" at the end so that TagLib#
        // can recognize the backup file as a valid MP3 when reading/copying ID3 tags.
        // Previously ".original.bak" caused TagLib.File.Create() to throw because
        // ".bak" is not a recognized audio file extension.
        private const string BackupExtension = ".original.mp3";

        /// <summary>Gets the backup file path (e.g., "Song.mp3.original.mp3")</summary>
        public string GetBackupPath(string sourceFilePath)
        {
            var dir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
            var fileName = Path.GetFileName(sourceFilePath);
            return Path.Combine(dir, $"{fileName}{BackupExtension}");
        }

        /// <summary>Checks whether a backup copy already exists on disk</summary>
        public bool HasBackup(string sourceFilePath)
        {
            var backupPath = GetBackupPath(sourceFilePath);
            return File.Exists(backupPath);
        }

        /// <summary>
        /// Creates a pristine backup copy if one does not already exist.
        /// </summary>
        public void CreateBackupIfNeeded(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                return;

            var backupPath = GetBackupPath(sourceFilePath);
            if (!File.Exists(backupPath))
            {
                File.Copy(sourceFilePath, backupPath, true);
            }
        }

        /// <summary>
        /// 1-Click Revert: Overwrites the modified file with the pristine backup file,
        /// restoring it bit-for-bit to its original state.
        /// </summary>
        public bool RevertToOriginal(string sourceFilePath)
        {
            var backupPath = GetBackupPath(sourceFilePath);
            if (!File.Exists(backupPath))
                return false;

            File.Copy(backupPath, sourceFilePath, true);
            return true;
        }
    }
}
