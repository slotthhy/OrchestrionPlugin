using Dalamud.Logging;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Orchestrion.BGMSystem;

public static class BGMAddressResolver
{
    private static nint _baseAddress;
    private static nint _musicManager;
    
    public static nint AddRestartId { get; private set; }
    public static nint GetSpecialMode { get; private set; }

    public static unsafe void Init()
    {
        _baseAddress = DalamudApi.SigScanner.GetStaticAddressFromSig("48 8B 05 ?? ?? ?? ?? 48 85 C0 74 51 83 78 08 0B");
        AddRestartId = DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 88 9D ?? ?? ?? ?? 84 DB");
        GetSpecialMode = DalamudApi.SigScanner.ScanText("48 89 5C 24 ?? 57 48 83 EC 20 44 8B 41 10");
            
        DalamudApi.PluginLog.Debug($"[BGMAddressResolver] init: base address at {_baseAddress.ToInt64():X}");
            
        var musicLoc = DalamudApi.SigScanner.ScanText("48 8B 8F ?? ?? ?? ?? 39 70 20 0F 94 C2 45 33 C0");
        var musicOffset= Marshal.ReadInt32(musicLoc + 3);
        _musicManager = Marshal.ReadIntPtr(new nint(Framework.Instance()) + musicOffset);
        DalamudApi.PluginLog.Debug($"[BGMAddressResolver] MusicManager found at {_musicManager.ToInt64():X}");
    }
    
    public static nint BGMSceneManager
    {
        get
        {
            var baseObject = Marshal.ReadIntPtr(_baseAddress);

            return baseObject;
        }
    }
        
    public static nint BGMSceneList
    {
        get
        {
            var baseObject = Marshal.ReadIntPtr(_baseAddress);

            // I've never seen this happen, but the game checks for it in a number of places
            return baseObject == nint.Zero ? nint.Zero : Marshal.ReadIntPtr(baseObject + 0xC0);
        }
    }

    public static bool StreamingEnabled
    {
        get
        {
            var ret = Marshal.ReadByte(_musicManager + 50);
            return ret == 1;
        }
    }
}