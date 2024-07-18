using System.Threading.Tasks;
using Dalamud.Hooking;
using Orchestrion.Game.BGMSystem;
using Orchestrion.Persistence;

namespace Orchestrion.BGMSystem;

public class BGMController
{
    private const SceneFlags SceneZeroFlags = SceneFlags.Resume;
    private const int SceneCount = 12;
    private const int PlayersCount = 2;

    // The last song that the game was previously playing.
    public int OldSongId { get; private set; }
        
    // The scene that OldSongId was playing at.
    public int OldScene { get; private set; }
    
    // The song that the game is currently playing. That is, the song
    // that is currently playing at the highest scene that is not PlayingPriority.
    public int CurrentSongId { get; private set; }
    
    // The scene that CurrentSongId is playing at.
    public int CurrentScene { get; private set; }

    // The song that the game is currently playing behind CurrentSongId.
    public int SecondSongId { get; private set; }
    
    // The scene that SecondSongId is currently playing at.
    public int SecondScene { get; private set; }
    
    // The previous song that the game was playing behind CurrentSongId.
    public int OldSecondSongId { get; private set; }
    
    // The scene that OldSecondSongId was playing at.
    public int OldSecondScene { get; private set; }
    
    // The song that Orchestrion is currently playing.
    public int PlayingSongId { get; private set; }
    
    // The scene that PlayingSongId is playing at.
    public int PlayingScene { get; private set; }
    
    // CurrentSongId if PlayingSongId is 0, otherwise PlayingSongId.
    public int CurrentAudibleSong => PlayingSongId == 0 ? CurrentSongId : PlayingSongId;
    
    // The event that fires when the game changes songs.
    public delegate void SongChangedHandler(int oldSong, int currentSong, int oldSecondSong, int secondSong);
    public SongChangedHandler OnSongChanged;
    
    private unsafe delegate DisableRestart* AddDisableRestartIdPrototype(BGMScene* scene, ushort songId);
    private readonly AddDisableRestartIdPrototype _addDisableRestartId;
        
    private unsafe delegate int GetSpecialModeByScenePrototype(BGMPlayer* bgmPlayer);
    private readonly Hook<GetSpecialModeByScenePrototype> _getSpecialModeForSceneHook;
    
    public unsafe BGMController()
    {
        _addDisableRestartId = Marshal.GetDelegateForFunctionPointer<AddDisableRestartIdPrototype>(BGMAddressResolver.AddRestartId);
        _getSpecialModeForSceneHook = DalamudApi.Hooks.HookFromAddress<GetSpecialModeByScenePrototype>(BGMAddressResolver.GetSpecialMode, GetSpecialModeBySceneDetour);
        _getSpecialModeForSceneHook.Enable();
    }

    public void Dispose()
    {
        _getSpecialModeForSceneHook?.Disable();
        _getSpecialModeForSceneHook?.Dispose();
    }

    public void SetSpecialModeHandling(bool value)
    {
        if (value)
            _getSpecialModeForSceneHook.Enable();
        else
            _getSpecialModeForSceneHook.Disable();
    }

    public void Update()
    {
        ushort currentSong = 0;
        ushort secondSong = 0;
        var currentScene = 0;
        var secondScene = 0;

        if (BGMAddressResolver.BGMSceneList != IntPtr.Zero)
        {
            unsafe
            {
                var bgms = (BGMScene*)BGMAddressResolver.BGMSceneList.ToPointer();
                    
                for (int sceneIdx = 0; sceneIdx < SceneCount; sceneIdx++)
                {
                    // Ignore the PlayingScene scene
                    if (PlayingSongId != 0 && sceneIdx == PlayingScene)
                    {
                        // If the game overwrote our song, play it again
                        if (bgms[PlayingScene].BgmId != PlayingSongId)
                            SetSong((ushort)PlayingSongId, PlayingScene);
                        continue;
                    }
                    
                    if (bgms[sceneIdx].BgmReference == 0) continue;

                    if (bgms[sceneIdx].BgmId != 0 && bgms[sceneIdx].BgmId != 9999)
                    {
                        if (currentSong == 0)
                        {
                            currentSong = bgms[sceneIdx].BgmId;
                            currentScene = sceneIdx;
                        }
                        else
                        {
                            secondSong = bgms[sceneIdx].BgmId;
                            secondScene = sceneIdx;
                            break;
                        }
                    }
                }
            }
        }
            
        var oldSongId = 0;
        var currentSongId = 0;
        var oldSecondSongId = 0;
        var secondSongId = 0;
        var currentChanged = false;
        var secondChanged = false;
        
        if (CurrentSongId != currentSong)
        {
            OldSongId = CurrentSongId;
            OldScene = CurrentScene;
            CurrentSongId = currentSong;
            CurrentScene = currentScene;
            currentChanged = true;

            oldSongId = OldSongId;
            currentSongId = CurrentSongId;
        }

        if (SecondSongId != secondSong)
        {
            OldSecondSongId = SecondSongId;
            OldSecondScene = SecondScene;
            SecondSongId = secondSong;
            SecondScene = secondScene;
            secondChanged = true;
            
            oldSecondSongId = OldSecondSongId;
            secondSongId = SecondSongId;
        }
        
        if (currentChanged || secondChanged) OnSongChanged?.Invoke(oldSongId, currentSongId, oldSecondSongId, secondSongId);
    }

    public void SetSong(ushort songId, int priority = 0)
    {
        if (priority is < 0 or >= SceneCount) throw new IndexOutOfRangeException();
        if (songId != 0 && SongList.Instance.TryGetSong(songId, out var song) && !song.FileExists) return;

        if (BGMAddressResolver.BGMSceneList != nint.Zero)
        {
            unsafe
            {
                var bgms = (BGMScene*)BGMAddressResolver.BGMSceneList.ToPointer();
                bgms[priority].BgmReference = songId;
                bgms[priority].BgmId = songId;
                bgms[priority].PreviousBgmId = songId;

                if (songId == 0 && priority == 0)
                    bgms[priority].Flags = SceneZeroFlags;
                
                // these are probably not necessary, but clear them to be safe
                bgms[priority].Timer = 0;
                bgms[priority].TimerEnable = 0;

                PlayingSongId = songId;
                PlayingScene = priority;

                // unk5 is set to 0x100 by the game in some cases for priority 0
                // but I wasn't able to see that it did anything
                
                var disableRestart = SongList.Instance.IsDisableRestart(songId); 
                if (!disableRestart) return;
                
                bgms[priority].Flags = SceneFlags.EnableDisableRestart;
                _addDisableRestartId(&bgms[priority], songId);
                bgms[priority].Flags = SceneFlags.ForceAutoReset;

                Task.Delay(500).ContinueWith(_ =>
                {
                    bgms[priority].Flags = SceneFlags.EnableDisableRestart | SceneFlags.ForceAutoReset;
                });
            }
        }
    }

    private unsafe int GetSpecialModeBySceneDetour(BGMPlayer* player)
    {
        // Let the game do what it needs to do
        if (player->BgmScene != PlayingScene
            || player->BgmId != PlayingSongId
            || player->SpecialModeType == 0) 
            return _getSpecialModeForSceneHook.Original(player);
            
        if (!SongList.Instance.TryGetSong(player->BgmId, out var song)) return _getSpecialModeForSceneHook.Original(player);

        // Default to scene 10 behavior, but if the mode is mount mode, use the mount scene
        uint newScene = 10;
        if (song.SpecialMode == 2)
            newScene = 6;
            
        // Trick the game into giving us the result we want for the scene our song should actually be playing on
        var tempScene = player->BgmScene;
        player->BgmScene = newScene;
        var result = _getSpecialModeForSceneHook.Original(player);
        player->BgmScene = tempScene;
        return result;
    }

    // private static SceneFlags GetSceneFlagsNeededForBgm(Song song)
    // {
    //     // var sceneFlags = SceneFlags.None;
    //     var sceneFlags = SceneZeroFlags;
    //     if (song.DisableRestart) sceneFlags |= SceneFlags.EnableDisableRestart;
    //     // if (bgm.PassEnd) sceneFlags |= SceneFlags.EnablePassEnd;
    //         
    //     // This one is an assumption...
    //     // if (bgm.DisableRestartTimeOut) sceneFlags |= SceneFlags.Resume;
    //
    //     return sceneFlags;
    // }
}