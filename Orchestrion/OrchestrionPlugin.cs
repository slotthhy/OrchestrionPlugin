using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using System.Linq;
using System.Reflection;
using CheapLoc;
using Dalamud;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Orchestrion.Audio;
using Orchestrion.BGMSystem;
using Orchestrion.Persistence;
using Orchestrion.UI.Windows;
using MainWindow = Orchestrion.UI.Windows.MainWindow.MainWindow;

namespace Orchestrion;

// ReSharper disable once ClassNeverInstantiated.Global
public class OrchestrionPlugin : IDalamudPlugin
{
	private const string ConstName = "Orchestrion";
	private const string CommandName = "/porch";
	private const string NativeNowPlayingPrefix = "♪ ";
	
	public static IFontHandle CnFont { get; private set; }
	public static IFontHandle LargeFont { get; private set; }

	public string Name => ConstName;

	private readonly WindowSystem _windowSystem;
	private readonly MiniPlayerWindow _miniPlayerWindow;
	private readonly MainWindow _mainWindow;
	private readonly SettingsWindow _settingsWindow;

	private IDtrBarEntry _dtrEntry;

	private SeString _songEchoMsg;

	public OrchestrionPlugin(IDalamudPluginInterface pi)
	{
		DalamudApi.Initialize(pi);
		LanguageChanged(DalamudApi.PluginInterface.UiLanguage);
		
		BGMAddressResolver.Init();
		BGMManager.OnSongChanged += OnSongChanged;

		_windowSystem = new WindowSystem();
		_mainWindow = new MainWindow(this);
		_settingsWindow = new SettingsWindow();
		_miniPlayerWindow = new MiniPlayerWindow();

		_windowSystem.AddWindow(_mainWindow);
		_windowSystem.AddWindow(_settingsWindow);
		_windowSystem.AddWindow(_miniPlayerWindow);
		
		_dtrEntry = DalamudApi.DtrBar.Get(ConstName);
		_dtrEntry.Shown = Configuration.Instance.ShowSongInNative;
		_dtrEntry.OnClick = _mainWindow.Toggle;

		DalamudApi.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
		{
			HelpMessage = Loc.Localize("HelpMessage", "Displays the Orchestrion window, to view, change, or stop in-game BGM."),
		});

		DalamudApi.PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
		DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;

		DalamudApi.Framework.Update += OrchestrionUpdate;
		DalamudApi.ClientState.Logout += ClientStateOnLogout;
		
		DalamudApi.PluginInterface.LanguageChanged += LanguageChanged;
		
		var atlas = DalamudApi.PluginInterface.UiBuilder.FontAtlas;
		CnFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => {
			var config = new SafeFontConfig
			{
				SizePx = UiBuilder.DefaultFontSizePx,
				GlyphRanges = SongList.Instance
					.GetSongs()
					.Values
					.SelectMany(x => x.Strings.GetValueOrDefault("zh", default).Name ?? string.Empty)
					.Concat(Enumerable.Range(1, 127).Select(x => (char)x))
					.ToGlyphRange(),
			};
			tk.Font = tk.AddDalamudAssetFont(DalamudAsset.NotoSansJpMedium, config);
		}));
		if (CnFont.LoadException != null)
		{
			DalamudApi.PluginLog.Debug(CnFont.LoadException.Message);	
			DalamudApi.PluginLog.Debug(CnFont.LoadException.StackTrace);	
		}
		
		LargeFont = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, 24));
	}

	public static void LanguageChanged(string code)
	{
		var stream = 
			Assembly.GetExecutingAssembly().GetManifestResourceStream($"Orchestrion.Loc.{code}.json") 
			?? Assembly.GetExecutingAssembly().GetManifestResourceStream($"Orchestrion.Loc.en.json");
		if (stream == null) return; // we can't recover
		var content = new StreamReader(stream).ReadToEnd();
		Loc.Setup(content);
	}
	
	public void Dispose()
	{
		_mainWindow.Dispose();
		DalamudApi.Framework.Update -= OrchestrionUpdate;
		DalamudApi.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
		// DalamudApi.PluginInterface.UiBuilder.BuildFonts -= BuildFonts;
		DalamudApi.CommandManager.RemoveHandler(CommandName);
		_dtrEntry?.Remove();
		PlaylistManager.Dispose();
		BGMManager.Dispose();
		LargeFont?.Dispose();
		CnFont?.Dispose();
	}

	private void OrchestrionUpdate(IFramework ignored)
	{
		PerformEcho();
		CheckDtr();
		UpdateSettings();
	}

	private void PerformEcho()
	{
		if (_songEchoMsg == null || IsLoadingScreen()) return;

		DalamudApi.ChatGui.Print(new XivChatEntry
		{
			Message = _songEchoMsg,
			Type = Configuration.Instance.ChatType,
		});

		_songEchoMsg = null;
	}

	private void CheckDtr()
	{
		_dtrEntry.Shown = Configuration.Instance.ShowSongInNative;
	}
	
	private void UpdateSettings()
	{
		var dalamudChatType = DalamudApi.PluginInterface.GeneralChatType;
		if (Configuration.Instance.ChatChannelMatchDalamud && Configuration.Instance.ChatType != dalamudChatType)
		{
			Configuration.Instance.ChatType = dalamudChatType;
			Configuration.Instance.Save();
		}
		
		var dalamudLang = DalamudApi.PluginInterface.UiLanguage;
		if (Configuration.Instance.UserInterfaceLanguageMatchDalamud && Configuration.Instance.UserInterfaceLanguageCode != dalamudLang)
		{
			Configuration.Instance.UserInterfaceLanguageCode = dalamudLang;
			Configuration.Instance.Save();
		}
	}

	private void ClientStateOnLogout()
	{
		BGMManager.Stop();
	}

	private void OnSongChanged(int oldSong, int newSong, int oldSecondSong, int oldCurrentSong, bool oldPlayedByOrch, bool playedByOrch)
	{
		if (oldSong == newSong && oldSong == 0) return;
		if (oldSong == newSong && oldPlayedByOrch && playedByOrch) return;
		DalamudApi.PluginLog.Debug($"[OnSongChanged] Changed from {oldSong} to {newSong}, oldPlayedByOrch: {oldPlayedByOrch}, playedByOrch: {playedByOrch}");
		UpdateDtr(newSong, playedByOrch: playedByOrch);
		UpdateChat(newSong, playedByOrch: playedByOrch);
	}

	public void OpenMainWindow()
	{
		_mainWindow.IsOpen = true;
	}

	public void OpenSettingsWindow()
	{
		_settingsWindow.IsOpen = true;
	}

	private void OnCommand(string command, string args)
	{
		var argSplit = args.Split(' ');
		var argLen = argSplit.Length;

		// print args to log
		DalamudApi.PluginLog.Information($"command: {command} args: {args}");
		var argString = "['" + string.Join("', '", argSplit) + "']";
		DalamudApi.PluginLog.Information($"argLen: {argLen} argSplit: {argString}");
		
		var mainArg = argSplit[0].ToLowerInvariant();

		switch (argLen)
		{
			case 1:
				DalamudApi.PluginLog.Verbose("case 1");
				switch (mainArg)
				{
					case "":
						_mainWindow.IsOpen = !_mainWindow.IsOpen;
						break;
					case "help":
						PrintHelp();
						break;
					case "stop":
						BGMManager.Stop();
						break;
					case "play":
						DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("MustSpecifySong", "You must specify a song to play.")));
						break;
					case "random":
						BGMManager.PlayRandomSong();
						break;
					case "next":
						if (PlaylistManager.CurrentPlaylist == null)
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoPlaylistPlaying", "No playlist is currently playing.")));
						else
							PlaylistManager.Next();
						break;
					case "previous":
						if (PlaylistManager.CurrentPlaylist == null)
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoPlaylistPlaying", "No playlist is currently playing.")));
						else
							PlaylistManager.Previous();
						break;
					case "shuffle":
						DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoShuffleModeSpecified", "Please specify a shuffle mode.")));
						break;
					case "repeat":
						DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoRepeatModeSpecified", "Please specify a repeat mode.")));
						break;
					case "ddmode":
						BGMManager.StartDeepDungeonMode();
						break;
					case "check":
						Util.CheckSongData();
						break;
				}
				break;
			case 2:
				DalamudApi.PluginLog.Verbose("case 2");
				var arg2 = argSplit[1].ToLowerInvariant();
				switch (mainArg)
				{
					case "random":
						BGMManager.PlayRandomSong(argSplit[1]);
						break;
					case "play" when int.TryParse(argSplit[1], out var songId):
						if (SongList.Instance.SongExists(songId))
							BGMManager.Play(songId);
						else
							DalamudApi.ChatGui.PrintError(BuildChatMessage(string.Format(Loc.Localize("SongIdNotFound", "Song ID {0} not found."), songId)));
						break;
					case "play" when !int.TryParse(argSplit[1], out var songId):
						DalamudApi.PluginLog.Verbose("play by song name");
						HandlePlayBySongName(argSplit);
						break;
					case "shuffle":
						if (!Enum.TryParse<ShuffleMode>(arg2, true, out var shuffleMode))
						{
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("InvalidMode", "The specified mode is invalid.")));
							break;
						}
						
						if (PlaylistManager.CurrentPlaylist == null)
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoPlaylistPlaying", "No playlist is currently playing.")));
						else
						{
							PlaylistManager.CurrentPlaylist.ShuffleMode = shuffleMode;
							if (shuffleMode == ShuffleMode.On)
								PlaylistManager.CurrentPlaylist.RepeatMode = RepeatMode.All;
						}
							
						break;
					case "repeat":
						if (!Enum.TryParse<RepeatMode>(arg2, true, out var repeatMode))
						{
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("InvalidMode", "The specified mode is invalid.")));
							break;
						}
						
						if (PlaylistManager.CurrentPlaylist == null)
						{
							DalamudApi.ChatGui.PrintError(BuildChatMessage(Loc.Localize("NoPlaylistPlaying", "No playlist is currently playing.")));
						}
						else
						{
							PlaylistManager.CurrentPlaylist.ShuffleMode = ShuffleMode.Off;
							PlaylistManager.CurrentPlaylist.RepeatMode = repeatMode;
						}
						break;
					case "ddmode":
						switch (argSplit[1])
						{
							case "start":
								BGMManager.StartDeepDungeonMode("");
								break;
							case "stop":
								BGMManager.StopDeepDungeonMode();
								break;
							default:
								DalamudApi.ChatGui.PrintError(BuildChatMessageFormatted(Loc.Localize("InvalidDDModeCommand", "Invalid DDMode command <i>{0}</i>."), argSplit[1], false));
								break;
						}
						break;
				}
				break;
			case >= 3 when argSplit[0].ToLowerInvariant() == "ddmode" && argSplit[1].ToLowerInvariant() == "start":
				
				var ddModePlaylistName = argSplit.Skip(2).Aggregate((x, y) => $"{x} {y}");
				
				var ddModePlaylistExists = Configuration.Instance.TryGetPlaylist(ddModePlaylistName, out var ddModePlaylist);
				if (!ddModePlaylistExists)
				{
					DalamudApi.ChatGui.PrintError(BuildChatMessageFormatted(Loc.Localize("PlaylistNotFound", "Playlist <i>{0}</i> not found."), ddModePlaylistName, false));
					break;
				}
				BGMManager.StartDeepDungeonMode(ddModePlaylist.Name);
				break;
			case >= 3 when argSplit[1].ToLowerInvariant() == "playlist":
				DalamudApi.PluginLog.Verbose("case >= 3 when argSplit[1].ToLowerInvariant() == playlist");
				var playlistName = argSplit.Skip(2).Aggregate((x, y) => $"{x} {y}");
				
				var playlistExists = Configuration.Instance.TryGetPlaylist(playlistName, out var playlist);
				if (!playlistExists)
				{
					DalamudApi.ChatGui.PrintError(BuildChatMessageFormatted(Loc.Localize("PlaylistNotFound", "Playlist <i>{0}</i> not found."), playlistName, false));
					break;
				}

				var arg = argSplit[0].ToLowerInvariant();
				switch (arg)
				{
					case "play":
						PlaylistManager.Play(playlist.Name);
						break;
					case "shuffle":
						playlist.ShuffleMode = ShuffleMode.On;
						playlist.RepeatMode = RepeatMode.All;
						PlaylistManager.Play(playlist.Name);
						break;
					case "repeat":
						playlist.ShuffleMode = ShuffleMode.Off;
						playlist.RepeatMode = RepeatMode.All;
						PlaylistManager.Play(playlist.Name);
						break;
				}
				break;
			case >= 2 when argSplit[0].ToLowerInvariant() == "play" && !int.TryParse(argSplit[1], out _):
				DalamudApi.PluginLog.Verbose("case >= 2 when argSplit[0].ToLowerInvariant() == play && !int.TryParse(argSplit[1], out _)");
				HandlePlayBySongName(argSplit);
				break;
			default:
				PrintHelp();
				break;
		}
	}

	private void HandlePlayBySongName(string[] argSplit)
	{
		var songName = argSplit.Skip(1).Aggregate((x, y) => $"{x} {y}");
		if (SongList.Instance.TryGetSongByName(songName, out var songIdFromName))
		{
			BGMManager.Play(songIdFromName);
		}
		else
		{
			DalamudApi.ChatGui.PrintError(
				BuildChatMessageFormatted(
					Loc.Localize("SongNameNotFound", "Song <i>{0}</i> not found."),
					songName,
					false)
			);
		}
	}

	private void PrintHelp()
	{
		DalamudApi.ChatGui.Print(BuildChatMessage(Loc.Localize("HelpColon", "Help:")));
		DalamudApi.ChatGui.Print(BuildChatMessage(Loc.Localize("GeneralCommandsColon", "General Commands:")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch help - " + Loc.Localize("HelpDisplayThisMessage", "Display this message")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch - " + Loc.Localize("HelpOpenOrchestrionWindow", "Open the Orchestrion window")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch play [songId] - " + Loc.Localize("HelpPlaySongWithId", "Play the song with the specified ID")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch play [song name] - " + Loc.Localize("HelpPlaySongWithName", "Play the song with the specified name (both English and Japanese titles work)")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch random - " + Loc.Localize("HelpPlayRandomSong", "Play a random song")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch stop - " + Loc.Localize("HelpStopSong", "Stop the current playing song, replacement song, playlist, or DD mode.")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch ddmode start - " + Loc.Localize("HelpDDModeStart", "Enable Deep Dungeon mode: On every in-game BGM change, replace BGM with a random song")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch ddmode start [playlist name] - " + Loc.Localize("HelpDDModeStartPlaylist", "Enable Deep Dungeon mode: On every in-game BGM change, replace BGM with a random song from the specified playlist")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch ddmode stop - " + Loc.Localize("HelpDDModeStop", "Disable Deep Dungeon mode.")));
		DalamudApi.ChatGui.Print(BuildChatMessage(Loc.Localize("PlaylistCommandsColon", "Playlist Commands:")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch random [playlist] - " + Loc.Localize("HelpPlayRandomSongFromPlaylist", "Play a random song from the specified playlist (does not begin the playlist)")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch play playlist [playlist name] - " + Loc.Localize("HelpPlayPlaylist", "Play the specified playlist with its current settings")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch shuffle playlist [playlist name] - " + Loc.Localize("HelpPlayPlaylistShuffle", "Play the specified playlist, changing the playlist's settings to shuffle")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch repeat playlist [playlist name] - " + Loc.Localize("HelpPlayPlaylistRepeat", "Play the specified playlist, changing the playlist's settings to 'repeat all'")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch shuffle [on, off] - " + Loc.Localize("HelpPlaylistShuffle", "Set the current playlist to the specified shuffle mode")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch repeat [all, one, once] - " + Loc.Localize("HelpPlaylistRepeat", "Set the current playlist to the specified repeat mode")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch next - " + Loc.Localize("HelpPlaylistNext", "Play the next song in the current playlist")));
		DalamudApi.ChatGui.Print(BuildChatMessage("/porch previous - " + Loc.Localize("HelpPlaylistPrevious", "Play the previous song in the current playlist")));
	}

	private void UpdateDtr(int songId, bool playedByOrch = false)
	{
		if (_dtrEntry == null) return;
		if (!SongList.Instance.TryGetSong(songId, out var song)) return;
		var songName = song.Strings[Configuration.Instance.ServerInfoLanguageCode].Name;
		var locations = song.Strings[Configuration.Instance.ServerInfoLanguageCode].Locations;
		var info = song.Strings[Configuration.Instance.ServerInfoLanguageCode].AdditionalInfo;
		if (string.IsNullOrEmpty(songName)) return;

		var suffix = "";
		if (Configuration.Instance.ShowIdInNative)
		{
			if (!string.IsNullOrEmpty(songName))
				suffix = " - ";
			suffix += $"{songId}";
		}

		var text = songName + suffix;

		text = playedByOrch ? $"{NativeNowPlayingPrefix}[{text}]" : $"{NativeNowPlayingPrefix}{text}";
		
		_dtrEntry.Text = text;

		var locEmpty = string.IsNullOrEmpty(locations);
		var infoEmpty = string.IsNullOrEmpty(info);
		if (locEmpty && infoEmpty)
			_dtrEntry.Tooltip = "";
		if (!locEmpty && infoEmpty)
			_dtrEntry.Tooltip = $"{locations}";
		if (locEmpty && !infoEmpty)
			_dtrEntry.Tooltip = $"{info}";
		if (!locEmpty && !infoEmpty)
			_dtrEntry.Tooltip = $"{locations}\n{info}";
	}

	private void UpdateChat(int songId, bool playedByOrch = false)
	{
		if (!Configuration.Instance.ShowSongInChat) return;
		if (!SongList.Instance.TryGetSong(songId, out var song)) return;
		var songName = song.Strings[Configuration.Instance.ChatLanguageCode].Name;

		// the actual echoing is done during framework update
		if (!string.IsNullOrEmpty(songName))
			_songEchoMsg = BuildChatMessageFormatted(Loc.Localize("NowPlayingEcho", "Now playing <i>{0}</i>."), songName, playedByOrch);
	}

	private unsafe bool IsLoadingScreen()
	{
		var titleCard = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("_LocationTitle");
		var blackScreen = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("FadeMiddle");
		return titleCard != null && titleCard->IsVisible || blackScreen != null && blackScreen->IsVisible;
	}

	private SeString BuildChatMessage(string message)
	{
		return new SeStringBuilder()
			.AddUiForeground("[Orchestrion] ", 35)
			.AddText(message)
			.Build();
	}

	private SeString BuildChatMessageFormatted(string message, string param, bool playedByOrch)
	{
		var tmp1 = message.Split("<i>");
		var tmp2 = tmp1[1].Split("</i>");
		var pre = tmp1[0];
		var mid = tmp2[0];
		var post = tmp2[1];

		var midFormatAddtl = playedByOrch ? $"[{mid}]" : $"{mid}";
		
		return new SeStringBuilder()
			.AddUiForeground("[Orchestrion] ", 35)
			.AddText(pre)
			.AddItalics(string.Format(midFormatAddtl, param))
			.AddText(post)
			.Build();
	}
}
