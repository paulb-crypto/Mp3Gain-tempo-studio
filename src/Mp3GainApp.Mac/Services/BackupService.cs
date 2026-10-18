// =================================================================================================
// FILE: BackupService.cs
// LOCATION: Mp3GainApp.Mac/Services/BackupService.cs
//
// WHAT THIS FILE DOES:
//   Guarantees safety by creating an untouched backup copy named "<filename>.original.mp3"
//   before any modification occurs. Powers 1-click Revert.
// =================================================================================================

using System;
using System.IO;

namespace Mp3GainApp.Mac.Services
{
    public class BackupService
    {
        private const string BackupExtension = ".original.mp3";

        public string GetBackupPath(string sourceFilePath)
        {
            var dir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
            var fileName = Path.GetFileName(sourceFilePath);
            return Path.Combine(dir, $"{fileName}{BackupExtension}");
        }

        public bool HasBackup(string sourceFilePath)
        {
            var backupPath = GetBackupPath(sourceFilePath);
            return File.Exists(backupPath);
        }

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

