# MP3 Gain & Tempo Studio Architecture

## Overview
MP3 Gain & Tempo Studio is a desktop application written in C# and WPF targeting .NET 8, designed specifically for Windows 11 with the Fluent design system.

## Components & DSP Pipeline

### 1. Audio Decoding & Metering
- **NAudio `AudioFileReader`**: Decodes MP3 stream into 32-bit floating-point audio PCM in the $[-1.0, 1.0]$ range.
- **`AudioAnalyzerService`**:
  - Analyzes **Peak dBFS**: $20 \log_{10}(\max |s|)$.
  - Analyzes **Integrated Loudness (ITU-R BS.1770 / EBU R128 LUFS)**:
    - **Stage 1 (Head model high-shelf filter)**: Boosts frequencies above 1.5 kHz to model acoustic human head resonance.
    - **Stage 2 (RLB high-pass filter)**: Attenuates sub-bass below 100 Hz matching human ear sensitivity curves.
    - **Integrated Loudness**: $L = -0.691 + 10 \log_{10}\left(\sum_c z_c\right)$.
  - **Clipping Detection**: Checks if raw samples meet or exceed 0 dBFS ($|s| \ge 0.999$).
  - **High-Precision BPM Detection**: Downmixes stereo frames to mono, samples transient energy flux at 100 Hz, and calculates autocorrelation across 60–200 BPM with **parabolic quadratic interpolation** around the peak lag for 0.1 BPM accuracy.
  - **Recommended Gain**: Calculates the difference between reference target loudness (-14 LUFS standard) and track loudness, capped to keep peak below -0.5 dBFS.

### 2. Time-Stretching (BPM Calibration without Pitch Shift)
- **`WsolaTempoSampleProvider`**:
  - Studio-grade Waveform Similarity Overlap-Add algorithm.
  - Slices audio into ~25 ms Hann-windowed grains with 50% overlap.
  - Performs cross-correlation within a search window around the nominal synthesis hop to align waveform peaks in-phase before blending, eliminating comb-filtering and robotic phasing.
  - **`CircularSampleBuffer`**: Zero-allocation circular ring buffers for input and output FIFO queues, preventing memory allocations and garbage collection pauses during real-time playback.
  - Automatically bypassed (zero latency) when tempo factor is $1.0\times$.

### 3. Volume Gain & Anti-Clipping Limiter
- **`GainAndLimiterSampleProvider`**:
  - Digital decibel gain: $s_{\text{gained}} = s \times 10^{\frac{\text{gainDb}}{20}}$.
  - **Anti-Clipping Soft-Knee Limiter**:
    - Below threshold $T = 0.95$ (-0.45 dBFS): Linear passthrough.
    - Above threshold $T$: Hyperbolic tangent compression curve:
      $$y = \operatorname{sgn}(s) \cdot \left[ T + (1.0 - T) \cdot \tanh\left(\frac{|s| - T}{1.0 - T}\right) \right]$$
    - This mathematically ensures the audio **never exceeds 1.0 (0 dBFS)**, eliminating harsh digital clipping.
  - Live VU meter and clipping warning indicator report clipping events in real time.

### 4. Tag Preservation, Safe Revert & Export
- **`MetadataService`** (`TagLibSharp`):
  - Copies all ID3v1 and ID3v2 frames (Title, Artist, Album, Year, Genre, Track, Comments, and embedded Album Artwork pictures) from source to destination.
  - Updates the official ID3 `TBPM` tag with the new BPM.
- **`BackupService`**:
  - Before modifying an MP3 in-place, creates `<filename>.original.mp3`.
  - The **Revert** button allows instant restoration of the untouched original file bit-for-bit.
- **Export / Save Copy As**:
  - Allows exporting to a new MP3 file with custom naming and location without touching the original file.

### 5. Transport Controls & Keyboard Shortcuts
- **Interactive Scrubber**: Click-to-seek and drag-to-seek timeline slider.
- **Micro-Skipping**: -5s and +5s jump buttons.
- **Keyboard Shortcuts**:
  - `Spacebar`: Play / Pause preview
  - `Esc`: Stop playback and rewind
  - `Left / Right Arrows`: Skip backward / forward 5 seconds
  - `Ctrl + O`: Open MP3
  - `Ctrl + S`: Save changes to MP3 (with backup)
  - `Ctrl + Shift + S`: Save Copy As...
  - `Ctrl + Z`: Revert to original
