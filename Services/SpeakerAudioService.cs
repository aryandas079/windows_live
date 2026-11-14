using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsLive.Services;

public static class SpeakerAudioService
{
    private const int MMSYSERR_NOERROR = 0;
    private const int CALLBACK_EVENT = 0x00050000;
    private const int TIME_MS = 0x0001;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int waveOutGetDevCaps(IntPtr uDeviceID, out WAVEOUTCAPS pwoc, int cbwoc);

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr phwo, int uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, int fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hwo, ref WAVEHDR pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hwo, ref WAVEHDR pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hwo, ref WAVEHDR pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hwo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WAVEOUTCAPS
    {
        public short wMid;
        public short wPid;
        public int vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public int dwFormats;
        public short wChannels;
        public short wReserved1;
        public int dwSupport;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public int dwBufferLength;
        public int dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public int dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    public static int FindBuiltInSpeakerDeviceIndex()
    {
        try
        {
            int numDevs = waveOutGetNumDevs();
            int bestMatch = -1;

            for (int i = 0; i < numDevs; i++)
            {
                WAVEOUTCAPS caps;
                if (waveOutGetDevCaps((IntPtr)i, out caps, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0)
                {
                    string name = caps.szPname ?? "";
                    string lower = name.ToLowerInvariant();

                    // Reject any connected bluetooth / headphone / external display audio
                    if (lower.Contains("bluetooth") || lower.Contains("headphone") ||
                        lower.Contains("headset") || lower.Contains("earphone") ||
                        lower.Contains("airpod") || lower.Contains("hands-free") ||
                        lower.Contains("wireless") || lower.Contains("nvidia") ||
                        lower.Contains("hdmi"))
                    {
                        continue;
                    }

                    // Look for internal speakers
                    if (lower.Contains("realtek") || lower.Contains("speaker") ||
                        lower.Contains("internal") || lower.Contains("built-in") ||
                        lower.Contains("lautsprecher") || lower.Contains("synaptics") ||
                        lower.Contains("conexant") || lower.Contains("audio(sst)"))
                    {
                        App.Log($"SpeakerAudioService: Identified built-in speaker device {i}: '{name}'");
                        return i;
                    }

                    if (bestMatch == -1)
                    {
                        bestMatch = i;
                    }
                }
            }

            return bestMatch >= 0 ? bestMatch : 0;
        }
        catch (Exception ex)
        {
            App.Log($"SpeakerAudioService FindBuiltInSpeakerDeviceIndex error: {ex.Message}");
            return 0;
        }
    }

    public static void PlayUnlockSoundOnSpeakers(string wavFilePath)
    {
        Task.Run(() =>
        {
            try
            {
                if (!File.Exists(wavFilePath)) return;

                byte[] wavBytes = File.ReadAllBytes(wavFilePath);
                if (wavBytes.Length < 44) return;

                // Parse standard WAV header
                short channels = BitConverter.ToInt16(wavBytes, 22);
                int sampleRate = BitConverter.ToInt32(wavBytes, 24);
                short bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

                // Find 'data' chunk
                int dataOffset = 36;
                while (dataOffset < wavBytes.Length - 8)
                {
                    if (wavBytes[dataOffset] == 'd' &&
                        wavBytes[dataOffset + 1] == 'a' &&
                        wavBytes[dataOffset + 2] == 't' &&
                        wavBytes[dataOffset + 3] == 'a')
                    {
                        break;
                    }
                    dataOffset++;
                }

                if (dataOffset >= wavBytes.Length - 8) return;

                int pcmLength = BitConverter.ToInt32(wavBytes, dataOffset + 4);
                int pcmStart = dataOffset + 8;
                if (pcmStart + pcmLength > wavBytes.Length)
                {
                    pcmLength = wavBytes.Length - pcmStart;
                }

                int deviceIndex = FindBuiltInSpeakerDeviceIndex();

                var wfx = new WAVEFORMATEX
                {
                    wFormatTag = 1, // PCM
                    nChannels = channels,
                    nSamplesPerSec = sampleRate,
                    wBitsPerSample = bitsPerSample,
                    nBlockAlign = (short)(channels * (bitsPerSample / 8)),
                    nAvgBytesPerSec = sampleRate * (channels * (bitsPerSample / 8)),
                    cbSize = 0
                };

                IntPtr hWaveOut = IntPtr.Zero;
                int openResult = waveOutOpen(out hWaveOut, deviceIndex, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);

                if (openResult != MMSYSERR_NOERROR)
                {
                    // Fallback to default wave mapper
                    openResult = waveOutOpen(out hWaveOut, -1, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
                }

                if (openResult != MMSYSERR_NOERROR || hWaveOut == IntPtr.Zero)
                {
                    // Fallback safely to SoundPlayer
                    using var player = new System.Media.SoundPlayer(wavFilePath);
                    player.Play();
                    return;
                }

                byte[] pcmData = new byte[pcmLength];
                Buffer.BlockCopy(wavBytes, pcmStart, pcmData, 0, pcmLength);

                IntPtr pData = Marshal.AllocHGlobal(pcmLength);
                Marshal.Copy(pcmData, 0, pData, pcmLength);

                var header = new WAVEHDR
                {
                    lpData = pData,
                    dwBufferLength = pcmLength,
                    dwFlags = 0,
                    dwLoops = 0
                };

                try
                {
                    waveOutPrepareHeader(hWaveOut, ref header, Marshal.SizeOf(typeof(WAVEHDR)));
                    waveOutWrite(hWaveOut, ref header, Marshal.SizeOf(typeof(WAVEHDR)));

                    // Wait for the short audio buffer to finish playing
                    int timeoutMs = (int)((pcmLength / (double)wfx.nAvgBytesPerSec) * 1000.0) + 120;
                    Thread.Sleep(timeoutMs);

                    waveOutUnprepareHeader(hWaveOut, ref header, Marshal.SizeOf(typeof(WAVEHDR)));
                }
                finally
                {
                    Marshal.FreeHGlobal(pData);
                    waveOutClose(hWaveOut);
                }
            }
            catch (Exception ex)
            {
                App.Log($"SpeakerAudioService PlayUnlockSoundOnSpeakers error: {ex.Message}");
                try
                {
                    using var player = new System.Media.SoundPlayer(wavFilePath);
                    player.Play();
                }
                catch { }
            }
        });
    }
}