using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CheapLoc;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ImGuiNET;
using Orchestrion.Audio;
using Orchestrion.Persistence;
using Orchestrion.Types;
using Orchestrion.UI.Components;

namespace Orchestrion.UI.Windows.MainWindow;

public partial class MainWindow : Window, IDisposable
{
	private enum TabType
	{
		AllSongs,
		Playlist,
		History,
		Replacements,
		DDMode,
		Debug,
	}
	
	private static readonly string _noChange = Loc.Localize("NoChange", "Do not change BGM");
	private static readonly string _secAgo = Loc.Localize("SecondsAgo", "{0}s ago");
	private static readonly string _minAgo = Loc.Localize("MinutesAgo", "{0}m ago");
	private const string BaseName = "Orchestrion###Orchestrion";

	private readonly OrchestrionPlugin _orch;
	private readonly RenderableSongList _mainSongList;
	private readonly RenderableSongList _playlistSongList;
	
	private string _searchText = string.Empty;
	private TabType _currentTab = TabType.AllSongs;

	public MainWindow(OrchestrionPlugin orch) : base(BaseName, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
	{
		_orch = orch;
		_selectedPlaylist = Configuration.Instance.Playlists.Values.FirstOrDefault(p => p.Name == Configuration.Instance.LastSelectedPlaylist);

		_mainSongList = new RenderableSongList(
			SongList.Instance.GetSongs().Select(s => new RenderableSongEntry(s.Key)).ToList(),
			new SongListRenderStrategy());
		
		_historySongList = new RenderableSongList(
			_songHistory,
			new SongListRenderStrategy
			{
				RenderBackwards = () => true,
				IsPlaying = (entries, i) =>
				{
					// return false;
					var tmp = entries.ToArray();
					if (tmp.Length <= 0) return false;
					var id = tmp[i].Id;
					return i == tmp.Length - 1 && BGMManager.CurrentAudibleSong == id;
					// return BGMManager.CurrentAudibleSong == id;
				},
			});
		
		// Playlist renderer heavily relies on the strategy and is kind of hacky. whoops
		_playlistSongList = new RenderableSongList(
			new List<RenderableSongEntry>(),
			new SongListRenderStrategy
			{
				IsPlaying = (entries, i) =>
					PlaylistManager.IsPlaying &&
					PlaylistManager.CurrentSongIndex == i &&
					BGMManager.CurrentAudibleSong == entries.ElementAtOrDefault(i).Id,
				PlaySong = (entry, index) => PlaylistManager.Play(_selectedPlaylist?.Name, index),
				SourceMutable = () => true,
				RemoveSong = index => _selectedPlaylist?.RemoveSong(index),
			});

		BGMManager.OnSongChanged += SongChanged;
		ResetReplacement();
	}

	private void SongChanged(int oldSong, int currentSong, int oldSecondSong, int secondSong, bool oldPlayedByOrch, bool playedByOrchestrion)
	{
		var currentChanged = oldSong != currentSong;
		if (!currentChanged) return;

		AddSongToHistory(currentSong);

		if (Configuration.Instance.ShowSongInTitleBar)
		{
			if (currentSong == 0)
				WindowName = BaseName;
			else
			{
				DalamudApi.PluginLog.Debug("[UpdateTitle] Updating title bar");
				var songTitle = SongList.Instance.GetSongTitle(currentSong);
				WindowName = $"Orchestrion - [{currentSong}] {songTitle}###Orchestrion";
			}
		}
	}

	public void Dispose()
	{
		BGMManager.Stop();
	}

	private void ResetReplacement()
	{
		var id = SongList.Instance.GetFirstReplacementCandidateId();
		_tmpReplacement = new SongReplacementEntry
		{
			TargetSongId = id,
			ReplacementId = SongReplacementEntry.NoChangeId,
		};
	}

	public override void PreDraw()
	{
		BgmTooltip.ClearLock();
		ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);
		ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, 0);
		ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, 0);
	}

	public override void PostDraw()
	{
		ImGui.PopStyleColor(3);
	}

	public override void Draw()
	{
		ImGui.AlignTextToFramePadding();
		ImGui.Text(Loc.Localize("SearchColon", "Search:"));
		ImGui.SameLine();
		if (ImGui.InputText("##searchbox", ref _searchText, 32))
		{
			_mainSongList.SetSearch(_searchText);
			_historySongList.SetSearch(_searchText);
			_playlistSongList.SetSearch(_searchText);
		}

		ImGui.SameLine();
		ImGui.SetCursorPosX(ImGui.GetWindowSize().X - (35 * ImGuiHelpers.GlobalScale));
		ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1 * ImGuiHelpers.GlobalScale));

		if (ImGuiComponents.IconButton("##orchsettings", FontAwesomeIcon.Cog))
			_orch.OpenSettingsWindow();

		if (ImGui.BeginTabBar("##orchtabs"))
		{
			DrawTab(Loc.Localize("AllSongs", "All Songs"), "orch_AllSongs", DrawSongListTab, TabType.AllSongs);
			DrawTab(Loc.Localize("Playlists", "Playlists"), "orch_Playlists", DrawPlaylistsTab, TabType.Playlist);
			DrawTab(Loc.Localize("History", "History"), "orch_History", DrawSongHistoryTab, TabType.History);
			DrawTab(Loc.Localize("Replacements", "Replacements"), "orch_Replacements", DrawReplacementsTab, TabType.Replacements);
			DrawTab(Loc.Localize("DDMode", "DD Mode"), "orch_DDMode", DrawDeepDungeonModeTab, TabType.DDMode);
#if DEBUG
			DrawTab("Debug", "orch_Debug", DrawDebugTab, TabType.Debug);
#endif
			ImGui.EndTabBar();
		}
		
		NewPlaylistModal.Instance.Draw();
	}

	private void DrawTab(string name, string id, Action render, TabType type)
	{
		if (ImGui.BeginTabItem($"{name}###{id}"))
		{
			render();
			ImGui.EndTabItem();
			_currentTab = type;
		}
	}

	private void DrawFooter()
	{
		var song = GetSelectedSongForTab();
		
		var width = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X;
		var stopText = Loc.Localize("Stop", "Stop");
		var playText = Loc.Localize("Play", "Play");
		var buttonHeight = ImGui.CalcTextSize(stopText).Y + ImGui.GetStyle().FramePadding.Y * 2f;

		ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - buttonHeight - ImGui.GetStyle().WindowPadding.Y);

		if (BGMManager.PlayingSongId == 0) ImGui.BeginDisabled();
		if (ImGui.Button(stopText, new Vector2(width / 2, buttonHeight)))
			BGMManager.Stop();
		if (BGMManager.PlayingSongId == 0) ImGui.EndDisabled();

		ImGui.SameLine();

		ImGui.BeginDisabled(!song.FileExists);
		if (ImGui.Button(playText, new Vector2(width / 2, buttonHeight)))
			BGMManager.Play(song.Id);
		ImGui.EndDisabled();
		if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
			BgmTooltip.DrawBgmTooltip(song);
	}
	
	private Song GetSelectedSongForTab()
	{
		return _currentTab switch
		{
			TabType.AllSongs => _mainSongList.GetFirstSelectedSong(),
			TabType.History => _historySongList.GetFirstSelectedSong(),
			_ => default,
		};
	}
	
	private static void RightAlignButton(float y, string text)
	{
		var style = ImGui.GetStyle();
		var padding = style.WindowPadding.X + style.FramePadding.X * 2 + style.ScrollbarSize;
		ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X - padding);
		ImGui.SetCursorPosY(y);
	}

	private static void RightAlignText(float y, string text)
	{
		var style = ImGui.GetStyle();
		var padding = style.WindowPadding.X + style.ScrollbarSize;
		ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X - padding);
		ImGui.SetCursorPosY(y);
	}

	private static void RightAlignButtons(float y, string[] texts)
	{
		var style = ImGui.GetStyle();
		var padding = style.WindowPadding.X + style.FramePadding.X * 2 + style.ScrollbarSize;

		var cursor = ImGui.GetCursorPosX() + ImGui.GetWindowWidth();
		foreach (var text in texts)
		{
			cursor -= ImGui.CalcTextSize(text).X + padding;
		}

		ImGui.SetCursorPosX(cursor);
		ImGui.SetCursorPosY(y);
	}
}