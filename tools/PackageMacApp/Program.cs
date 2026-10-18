using System;
using System.IO;
using System.IO.Compression;
using DiscUtils.Iso9660;
using SkiaSharp;

namespace PackageMacApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Packaging MP3 Gain & BPM Studio for macOS ===");

            string rootDir = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(rootDir, "Mp3GainApp.sln")) && Directory.GetParent(rootDir) != null)
            {
                rootDir = Directory.GetParent(rootDir)!.FullName;
            }

            string publishDir = Path.Combine(rootDir, "publish");
            Directory.CreateDirectory(publishDir);

            // Step 1: Generate high-resolution macOS ICNS icon
            byte[] icnsBytes = GenerateAppIconIcns();
            string iconPath = Path.Combine(publishDir, "AppIcon.icns");
            File.WriteAllBytes(iconPath, icnsBytes);
            Console.WriteLine($"[1/4] Generated macOS ICNS icon: {iconPath} ({icnsBytes.Length} bytes)");

            // Architectures to package
            var targets = new (string Arch, string Label)[]
            {
                ("osx-arm64", "AppleSilicon"),
                ("osx-x64", "Intel")
            };

            foreach (var target in targets)
            {
                string sourcePublishDir = Path.Combine(publishDir, target.Arch);
                if (!Directory.Exists(sourcePublishDir))
                {
                    Console.WriteLine($"Warning: Source publish dir not found: {sourcePublishDir}. Skipping {target.Label}.");
                    continue;
                }

                Console.WriteLine($"\n--- Packaging for {target.Label} ({target.Arch}) ---");

                // Step 2: Assemble .app Bundle
                string stagingDir = Path.Combine(publishDir, $"bundle-{target.Arch}");
                if (Directory.Exists(stagingDir))
                {
                    Directory.Delete(stagingDir, true);
                }
                Directory.CreateDirectory(stagingDir);

                string appBundleDir = Path.Combine(stagingDir, "Mp3GainStudio.app");
                string contentsDir = Path.Combine(appBundleDir, "Contents");
                string macOsDir = Path.Combine(contentsDir, "MacOS");
                string resourcesDir = Path.Combine(contentsDir, "Resources");

                Directory.CreateDirectory(macOsDir);
                Directory.CreateDirectory(resourcesDir);

                // Copy published application binaries into Contents/MacOS
                CopyDirectory(sourcePublishDir, macOsDir);

                // Copy Icon into Contents/Resources
                File.WriteAllBytes(Path.Combine(resourcesDir, "AppIcon.icns"), icnsBytes);

                // Create Info.plist
                string plistContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>CFBundleName</key>
    <string>MP3 Gain Studio</string>
    <key>CFBundleDisplayName</key>
    <string>MP3 Gain &amp; BPM Studio</string>
    <key>CFBundleIdentifier</key>
    <string>com.mp3gainstudio.mac</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>Mp3GainStudio</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon.icns</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2026 MP3 Gain &amp; BPM Studio</string>
</dict>
</plist>";
                File.WriteAllText(Path.Combine(contentsDir, "Info.plist"), plistContent);

                // Create PkgInfo
                File.WriteAllText(Path.Combine(contentsDir, "PkgInfo"), "APPL????");

                // Create unix shell launcher Mp3GainStudio to make sure permissions & env work seamlessly
                string launcherScript = @"#!/bin/bash
DIR=""$( cd ""$( dirname ""${BASH_SOURCE[0]}"" )"" && pwd )""
chmod +x ""$DIR/Mp3GainApp.Mac""
exec ""$DIR/Mp3GainApp.Mac"" ""$@""
";
                // Convert to LF line endings
                launcherScript = launcherScript.Replace("\r\n", "\n");
                File.WriteAllText(Path.Combine(macOsDir, "Mp3GainStudio"), launcherScript);

                // Add Drag-and-drop installation instructions
                string installReadme = @"===================================================================
MP3 GAIN & BPM STUDIO FOR macOS (" + target.Label + @")
===================================================================

INSTALLATION INSTRUCTIONS:
1. Drag 'Mp3GainStudio.app' into your 'Applications' folder.
2. If macOS displays 'App cannot be opened because it is from an unidentified developer':
   - Right-click (or Control-click) 'Mp3GainStudio.app' and select 'Open'.
   - Click 'Open' in the confirmation prompt.
   OR run this one-time command in Terminal:
     xattr -dr com.apple.quarantine /Applications/Mp3GainStudio.app

AUDIO ENCODING (OPTIONAL):
- The app analyzes audio, plays real-time preview, adjusts gain, and changes BPM natively.
- For high-speed MP3 rendering when saving, if you haven't already, install ffmpeg:
     brew install ffmpeg

Enjoy mastering your tracks!
===================================================================
";
                File.WriteAllText(Path.Combine(stagingDir, "INSTALL_INSTRUCTIONS.txt"), installReadme);

                Console.WriteLine($"[2/4] Assembled .app bundle at: {appBundleDir}");

                // Step 3: Package into .dmg disk image using DiscUtils ISO9660/Joliet
                string dmgPath = Path.Combine(publishDir, $"Mp3GainStudio-macOS-{target.Label}.dmg");
                if (File.Exists(dmgPath)) File.Delete(dmgPath);

                var builder = new CDBuilder
                {
                    UseJoliet = true,
                    VolumeIdentifier = "Mp3GainStudio"
                };

                AddDirectoryToCd(builder, stagingDir, "");
                builder.Build(dmgPath);
                Console.WriteLine($"[3/4] Built macOS DMG disk image: {dmgPath} ({new FileInfo(dmgPath).Length / (1024 * 1024)} MB)");

                // Step 4: Package into .zip archive for direct 1-click unzip
                string zipPath = Path.Combine(publishDir, $"Mp3GainStudio-macOS-{target.Label}.zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);

                ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, false);
                Console.WriteLine($"[4/4] Built macOS ZIP archive: {zipPath} ({new FileInfo(zipPath).Length / (1024 * 1024)} MB)");
            }

            Console.WriteLine("\n=== Packaging Completed Successfully! ===");
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            foreach (var file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (var subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                Directory.CreateDirectory(newDestinationDir);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private static void AddDirectoryToCd(CDBuilder builder, string rootFolder, string currentRelative)
        {
            var dirInfo = new DirectoryInfo(string.IsNullOrEmpty(currentRelative) ? rootFolder : Path.Combine(rootFolder, currentRelative));

            foreach (var file in dirInfo.GetFiles())
            {
                string entryPath = string.IsNullOrEmpty(currentRelative) ? file.Name : $"{currentRelative}/{file.Name}";
                entryPath = entryPath.Replace('\\', '/');
                builder.AddFile(entryPath, file.FullName);
            }

            foreach (var subDir in dirInfo.GetDirectories())
            {
                string nextRelative = string.IsNullOrEmpty(currentRelative) ? subDir.Name : $"{currentRelative}/{subDir.Name}";
                AddDirectoryToCd(builder, rootFolder, nextRelative);
            }
        }

        private static byte[] GenerateAppIconIcns()
        {
            // Create a 512x512 modern squircle macOS icon
            int size = 512;
            using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            // macOS Big Sur squircle container (rounded rect with 115px radius)
            float margin = 24.0f;
            var squircleRect = new SKRect(margin, margin, size - margin, size - margin);
            float cornerRadius = 105.0f;

            // Gradient background (#4F46E5 Indigo to #7C3AED Violet to #0F172A Slate)
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(size, size),
                    new[] { new SKColor(0x4F, 0x46, 0xE5), new SKColor(0x7C, 0x3A, 0xED), new SKColor(0x1E, 0x1B, 0x4B) },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRoundRect(squircleRect, cornerRadius, cornerRadius, paint);

            // Subtle border highlight
            using var borderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4.0f,
                Color = new SKColor(0x81, 0x8C, 0xF8, 0xAA)
            };
            canvas.DrawRoundRect(squircleRect, cornerRadius, cornerRadius, borderPaint);

            // Waveform equalizer bars at bottom of squircle
            float[] barHeights = { 45, 90, 140, 190, 160, 210, 150, 180, 120, 80, 50 };
            float barWidth = 14.0f;
            float barSpacing = 16.0f;
            float totalWaveWidth = barHeights.Length * barWidth + (barHeights.Length - 1) * barSpacing;
            float startX = (size - totalWaveWidth) / 2.0f;
            float baseY = 390.0f;

            using var wavePaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0x38, 0xBD, 0xF8, 0xCC) // Sky blue glow
            };

            for (int i = 0; i < barHeights.Length; i++)
            {
                float h = barHeights[i];
                float x = startX + i * (barWidth + barSpacing);
                var barRect = new SKRect(x, baseY - h, x + barWidth, baseY);
                canvas.DrawRoundRect(barRect, 6.0f, 6.0f, wavePaint);
            }

            // Musical eighth note in white with gold accent
            using var notePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                Style = SKPaintStyle.Fill
            };

            // Note Head
            canvas.DrawCircle(195, 275, 36, notePaint);
            // Note Stem
            var stemRect = new SKRect(220, 130, 234, 275);
            canvas.DrawRoundRect(stemRect, 6, 6, notePaint);
            // Note Flag
            using var flagPath = new SKPath();
            flagPath.MoveTo(230, 130);
            flagPath.CubicTo(310, 140, 310, 200, 260, 230);
            flagPath.CubicTo(285, 180, 255, 160, 230, 165);
            flagPath.Close();
            canvas.DrawPath(flagPath, notePaint);

            // Golden BPM / Tempo sparkle badge in top right
            using var badgePaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0xFB, 0xBF, 0x24) // Amber / Gold
            };
            canvas.DrawCircle(360, 140, 16, badgePaint);

            // Encode SKBitmap to PNG memory stream
            using var image = SKImage.FromBitmap(bitmap);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = pngData.ToArray();

            // Pack into Apple ICNS format
            // Header: 'icns' (4 bytes) + Total Size (4 bytes Big Endian)
            // Chunk: 'ic09' (4 bytes, 512x512) + Chunk Size (4 bytes Big Endian) + PNG Data
            uint chunkSize = (uint)(8 + pngBytes.Length);
            uint totalSize = 8 + chunkSize;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // 'icns' magic
            bw.Write(new[] { (byte)'i', (byte)'c', (byte)'n', (byte)'s' });
            WriteBigEndian(bw, totalSize);

            // 'ic09' chunk for 512x512 PNG
            bw.Write(new[] { (byte)'i', (byte)'c', (byte)'0', (byte)'9' });
            WriteBigEndian(bw, chunkSize);
            bw.Write(pngBytes);

            return ms.ToArray();
        }

        private static void WriteBigEndian(BinaryWriter bw, uint val)
        {
            bw.Write((byte)((val >> 24) & 0xFF));
            bw.Write((byte)((val >> 16) & 0xFF));
            bw.Write((byte)((val >> 8) & 0xFF));
            bw.Write((byte)(val & 0xFF));
        }
    }
}

