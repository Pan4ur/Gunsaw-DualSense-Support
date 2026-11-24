using System.Runtime.InteropServices;

namespace GunsawDualSenseSupport;

[StructLayout(LayoutKind.Sequential)]
internal struct SdlAudioSpec
{
    internal int Frequency;
    internal ushort Format;
    internal byte Channels, Silence;
    internal ushort Samples, Padding;
    internal uint Size;
    internal IntPtr Callback, UserData;
}

internal static class Sdl
{
    private const string Library = "gunsaw-dualsense-sdl";
    internal const int DualSense = 7;
    internal const ushort Float32LittleEndian = 0x8120;
    internal const uint Controller = 0x2000, Audio = 0x10;

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetHint(string name, string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GameControllerEventState(int state);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_GameControllerUpdate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_NumJoysticks();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GameControllerTypeForIndex(int index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GameControllerOpen(int index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GameControllerGetAttached(IntPtr pad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_GameControllerClose(IntPtr pad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern short SDL_GameControllerGetAxis(IntPtr pad, int axis);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern byte SDL_GameControllerGetButton(IntPtr pad, int button);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GameControllerGetTouchpadFinger(IntPtr pad, int touchpad, int finger, out byte state, out float x, out float y, out float pressure);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GameControllerSendEffect(IntPtr pad, byte[] data, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GameControllerGetFirmwareVersion(IntPtr pad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetNumAudioDevices(int capture);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetAudioDeviceName(int index, int capture);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetAudioDeviceSpec(int index, int capture, out SdlAudioSpec spec);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_OpenAudioDevice(IntPtr name, int capture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_PauseAudioDevice(uint device, int pause);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetAudioDeviceStatus(uint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseAudioDevice(uint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_GetQueuedAudioSize(uint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_QueueAudio(uint device, float[] data, uint size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GetError();

    internal static string Error => Marshal.PtrToStringAnsi(SDL_GetError()) ?? "";
}
