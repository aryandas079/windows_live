using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class VolumeManager
{
    public event Action<float, bool>? VolumeChanged;

    private IAudioEndpointVolume? _audioEndpoint;
    private readonly DispatcherTimer _pollTimer;
    private float _lastVolume = 0.5f;
    private bool _lastMute = false;

    public float CurrentVolume => _lastVolume;
    public bool IsMuted => _lastMute;

    public VolumeManager()
    {
        InitializeEndpoint();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _pollTimer.Tick += (s, e) => PollVolumeState();
        _pollTimer.Start();
    }

    private void InitializeEndpoint()
    {
        try
        {
            Type? enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumType == null)
            {
                App.Log("VolumeManager: Failed to get MMDeviceEnumerator CLSID type");
                return;
            }
            var enumerator = Activator.CreateInstance(enumType) as IMMDeviceEnumerator;
            if (enumerator == null)
            {
                App.Log("VolumeManager: Failed to create IMMDeviceEnumerator instance");
                return;
            }

            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            if (hr == 0 && device != null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                hr = device.Activate(ref iid, 1, IntPtr.Zero, out var obj);
                if (hr == 0 && obj is IAudioEndpointVolume ep)
                {
                    _audioEndpoint = ep;
                    App.Log("VolumeManager: AudioEndpoint successfully activated");
                    PollVolumeState(forceNotify: true);
                }
                else
                {
                    App.Log($"VolumeManager: Failed to activate IAudioEndpointVolume, hr={hr}");
                }
            }
            else
            {
                App.Log($"VolumeManager: GetDefaultAudioEndpoint failed, hr={hr}");
            }
        }
        catch (Exception ex)
        {
            App.Log($"VolumeManager InitializeEndpoint error: {ex.Message}");
            _audioEndpoint = null;
        }
    }

    public void PollVolumeState(bool forceNotify = false)
    {
        if (_audioEndpoint == null) return;

        try
        {
            _audioEndpoint.GetMasterVolumeLevelScalar(out float vol);
            _audioEndpoint.GetMute(out bool mute);

            if (forceNotify || Math.Abs(vol - _lastVolume) > 0.01f || mute != _lastMute)
            {
                bool muteChanged = (mute != _lastMute);
                _lastVolume = vol;
                _lastMute = mute;
                if (muteChanged)
                {
                    App.Log($"VolumeManager PollVolumeState: Mute changed to {mute}, Vol={vol:F2}");
                }
                VolumeChanged?.Invoke(vol, mute);
            }
        }
        catch
        {
            // Re-attempt init if device changed
            InitializeEndpoint();
        }
    }

    public void SetVolume(float level)
    {
        level = Math.Clamp(level, 0.0f, 1.0f);
        _lastVolume = level;
        if (_audioEndpoint != null)
        {
            try
            {
                var guid = Guid.Empty;
                _audioEndpoint.SetMasterVolumeLevelScalar(level, ref guid);
            }
            catch { }
        }
        VolumeChanged?.Invoke(_lastVolume, _lastMute);
    }

    public void ToggleMute()
    {
        SetMute(!_lastMute);
    }

    public void SetMute(bool mute)
    {
        _lastMute = mute;
        if (_audioEndpoint != null)
        {
            try
            {
                var guid = Guid.Empty;
                _audioEndpoint.SetMute(mute, ref guid);
            }
            catch { }
        }
        VolumeChanged?.Invoke(_lastVolume, _lastMute);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
