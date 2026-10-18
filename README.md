# MP3 Gain & Tempo Studio 🎵🎛️

A high-fidelity, studio-grade desktop audio workstation for Windows, designed for DJs, music producers, and audio enthusiasts. 

**MP3 Gain & Tempo Studio** provides broadcast-standard loudness normalization (ITU-R BS.1770-4 LUFS), dual-engine tempo calibration (authentic turntable vinyl varispeed and phase-locked WSOLA key-lock), true-peak anti-clipping limiting, and complete ID3 metadata preservation.

---

## ✨ Key Features

### 1. 🎚️ Broadcast Loudness & Gain Calibration
* **ITU-R BS.1770-4 Integrated LUFS Analysis:** Measures perceived loudness across the entire track using standard K-weighting filters.
* **Smart Loudness Target:** One-click normalization to the Spotify / streaming standard (**-14.0 LUFS**).
* **Precise Gain Adjustment:** Fine-tune volume from **-15.0 dB to +15.0 dB** with micro-sliders or direct numeric text entry.

### 2. ⚡ Dual-Engine Tempo & BPM Calibration
* **Turntable / Vinyl Mode (Varispeed / Master Tempo OFF):**
  * Replicates the classic club sound of pushing a turntable pitch slider (Technics SL-1200 / Pioneer CDJ).
  * Uses 4-point Hermite cubic interpolation with **zero slicing and zero crossfading**.
  * Delivers **100% pristine audio punch, snappy kick drum attack, and zero digital degradation**. Pitch naturally tracks tempo.
* **Key Lock Mode (Master Tempo ON):**
  * Uses a continuous phase-accumulating **WSOLA (Waveform Similarity Overlap-Add)** engine.
  * Slices and blends micro-grains so the **singer's voice and musical key stay locked at the original pitch** while the tempo changes.
  * Includes high-frequency transient tracking to eliminate comb filtering and "watery" cymbal phasing.

### 3. 🛡️ Studio True-Peak Anti-Clipping Limiter
* **-1.0 dBFS Headroom Ceiling:** Leaves a safety cushion so lossy MP3 sub-band reconstruction cannot cause inter-sample clipping.
* **Hyperbolic Tangent Soft Knee:** Smoothly compresses peaks approaching the ceiling while leaving all audio below -2.0 dBFS **100% bit-perfect and uncolored**.
* Eliminates crunchy distortion on kick drums and bass transients.

### 4. 🏷️ Full ID3 Tag & Artwork Preservation
* Uses TagLib# to read and persist all ID3v2 tags (Title, Artist, Album, Year, Genre, Track Number, and Embedded Album Art).
* Writes the updated tempo directly to the standard `TBPM` ID3 tag.
* Automatically strips stale duration headers (`TLEN`) so media players and DJ software accurately report the new file duration.

### 5. 🎧 Real-Time Audio Preview Engine
* Zero-dropout preview player powered by NAudio.
* Live VU peak meter and red clipping detection warning.
* Interactive timeline scrubber with instant jump-to-point seeking.
* Handy keyboard shortcuts:
  * `Spacebar` — Play / Pause preview
  * `Left / Right Arrow` — Skip backward / forward 5 seconds
  * `Ctrl + O` — Open MP3 file
  * `Ctrl + S` — Save changes

### 6. 🔒 Non-Destructive Safety Net
* Automatic creation of a pristine backup (`.original.mp3`) before any file modification.
* One-click **Revert to Original** button to undo any changes with zero quality loss.

---

## 🏗️ Architecture & Technology Stack

* **Framework:** .NET 8.0 (Windows x64 WPF)
* **UI Controls:** WPF-UI (Fluent Design System, dark mode styling)
* **Audio Engine:** 
  * [NAudio](https://github.com/naudio/NAudio) for audio stream decoding, real-time audio graph routing, and playback.
  * [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame) for broadcast-quality MP3 encoding via `libmp3lame`.
* **Metadata Engine:** [TagLib#](https://github.com/mono/taglib-sharp) for ID3v2.3 / ID3v2.4 and cover artwork persistence.

---

## 🚀 Building & Running from Source

### Prerequisites
* Windows 10 / 11 (64-bit)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Clone the Repository
```bash
git clone https://github.com/paulb-crypto/Mp3Gain-tempo-studio.git
cd Mp3Gain-tempo-studio
```

### 2. Build the Solution
```bash
dotnet build src/Mp3GainApp/Mp3GainApp.csproj -c Release
```

### 3. Run the Application
```bash
dotnet run --project src/Mp3GainApp/Mp3GainApp.csproj -c Release
```

### 4. Publish a Single Standalone Executable
To generate a standalone `.exe` that runs on any 64-bit Windows machine without requiring .NET pre-installed:
```bash
dotnet publish src/Mp3GainApp/Mp3GainApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
The resulting executable will be located in the `publish/` directory.

---

## 📖 Usage Guide

1. **Open an MP3:** Drag & drop any MP3 file into the window or click **Open MP3 File**.
2. **Review Analysis:** Check the track's native LUFS loudness, true-peak dBFS, detected BPM, and clipping status.
3. **Adjust Volume Gain:** Use the slider, direct text input, or click **Spotify (-14 LUFS)** to normalize loudness.
4. **Adjust Tempo (BPM):** Slide or enter your desired target BPM.
5. **Select Tempo Mode:**
   * Leave **Key Lock unchecked** for the classic, punchy **Turntable / Vinyl Mode**.
   * Tick **Key Lock** if you want to keep the vocal pitch locked while changing speed.
6. **Preview:** Press `Spacebar` to listen to your adjustments in real-time.
7. **Save:** Click **Save Changes** to write the edited file or **Save Copy As** to export a new copy.

---

## 📄 License
This project is open-source under the MIT License.

