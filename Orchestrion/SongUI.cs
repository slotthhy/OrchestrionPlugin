using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Logging;
using Dalamud.Game.Text;

namespace Orchestrion;

public struct SongReplacement
{
    public const int NoChangeId = -1;
    
    /// <summary>
    /// The ID of the song to replace.
    /// </summary>
    public int TargetSongId;

    /// <summary>
    /// The ID of the replacement track to play. -1 (NoChangeId) means "ignore this song"
    /// </summary>
    public int ReplacementId;
}

public struct SongHistoryEntry
{
    public int Id;
    public DateTime TimePlayed;
}

public class SongUI : IDisposable
{
    private static float Scale => ImGui.GetIO().FontGlobalScale;

    private const string NoChange = "Do not change BGM";
    private const string SecAgo = "s ago";
    private const string MinAgo = "m ago";
    
    private int selectedSong;
    private int selectedHistoryEntry;
    private string searchText = string.Empty;
    private ImGuiScene.TextureWrap favoriteIcon;
    private ImGuiScene.TextureWrap settingsIcon;

    private readonly OrchestrionPlugin orch;
    private readonly List<SongHistoryEntry> songHistory = new();
    private readonly List<int> removalList = new();
    private SongReplacement tmpReplacement;
    private bool bgmTooltipLock;

    private bool visible;
    public bool Visible
    {
        get => visible;
        set => visible = value;
    }

    private bool settingsVisible;
    public bool SettingsVisible
    {
        get => settingsVisible;
        set => settingsVisible = value;
    }

    public SongUI(OrchestrionPlugin orch)
    {
        this.orch = orch;

        ResetReplacement();
    }

    public void Dispose()
    {
        Stop();
        favoriteIcon?.Dispose();
        settingsIcon?.Dispose();
    }

    private void ResetReplacement()
    {
        var id = SongList.GetFirstReplacementCandidateId();
        tmpReplacement = new SongReplacement
        {
            TargetSongId = id,
            ReplacementId = SongReplacement.NoChangeId,
        };
    }

    private void Play(int songId)
    {
        orch.PlaySong(songId);
    }

    private void Stop()
    {
        orch.StopSong();
    }

    public void AddSongToHistory(int id)
    {
        // Don't add silence
        if (id == 1 || !SongList.TryGetSong(id, out _))
            return;

        var newEntry = new SongHistoryEntry
        {
            Id = id,
            TimePlayed = DateTime.Now
        };

        var currentIndex = songHistory.Count - 1;

        // Check if we have history, if yes, then check if ID is the same as previous, if not, add to history
        if (currentIndex < 0 || songHistory[currentIndex].Id != id)
        {
            songHistory.Add(newEntry);
            PluginLog.Debug($"Added {id} to history. There are now {currentIndex + 1} songs in history.");
        }
    }

    public void Draw()
    {
        DrawMainWindow();
        DrawSettings();
    }

    private void DrawMainWindow()
    {
        // temporary bugfix for a race condition where it was possible that
        // we would attempt to load the icon before the ImGuiScene was created in dalamud
        // which would fail and lead to this icon being null
        // Hopefully later the UIBuilder API can add an event to notify when it is ready
        if (favoriteIcon == null)
        {
            favoriteIcon = LoadUIImage("favoriteIcon.png");
            settingsIcon = LoadUIImage("settings.png");
        }

        if (!Visible) return;

        var windowTitle = new StringBuilder("Orchestrion");
        if (OrchestrionPlugin.Configuration.ShowSongInTitleBar)
        {
            // TODO: subscribe to the event so this only has to be constructed on change?
            var songId = orch.CurrentSong;
            if (SongList.TryGetSong(songId, out var song))
                windowTitle.Append($" - [{song.Id}] {song.Name}");
        }
        windowTitle.Append("###Orchestrion");
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, ScaledVector2(370, 400));

        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, 0);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, 0);
        
        ImGui.SetNextWindowSize(ScaledVector2(370, 400), ImGuiCond.FirstUseEver);
        // these flags prevent the entire window from getting a secondary scrollbar sometimes, and also keep it from randomly moving slightly with the scrollwheel
        if (ImGui.Begin(windowTitle.ToString(), ref visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            bgmTooltipLock = false;
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Search: ");
            ImGui.SameLine();
            ImGui.InputText("##searchbox", ref searchText, 32);

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X - (35 * ImGuiHelpers.GlobalScale));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1 * ImGuiHelpers.GlobalScale));
            if (ImGui.ImageButton(settingsIcon.ImGuiHandle, ScaledVector2(16, 16)))
                settingsVisible = true;

            if (ImGui.BeginTabBar("##songlist tabs"))
            {
                if (ImGui.BeginTabItem("All songs"))
                {
                    DrawSonglist(false);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Favorites"))
                {
                    DrawSonglist(true);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("History"))
                {
                    DrawSongHistory();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Replacements"))
                {
                    DrawReplacements();
                    ImGui.EndTabItem();
                }
#if DEBUG
                if (ImGui.BeginTabItem("Debug"))
                {
                    DrawDebug();
                    ImGui.EndTabItem();
                }
#endif
                ImGui.EndTabBar();
            }
        }

        ImGui.End();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private void DrawFooter(bool isHistory = false)
    {
        var songId = selectedSong;

        if (isHistory && songHistory.Count > selectedHistoryEntry)
            songId = songHistory[selectedHistoryEntry].Id;
        else if (isHistory)
            songId = 0;
        
        if (SongList.TryGetSong(songId, out var song))
        {
            // ImGui.TextWrapped(song.Locations);
            // ImGui.TextWrapped(song.AdditionalInfo);
        }

        var width = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X;
        var buttonHeight = ImGui.CalcTextSize("Stop").Y + ImGui.GetStyle().FramePadding.Y * 2f;

        ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - buttonHeight - ImGui.GetStyle().WindowPadding.Y);
        
        if (BGMController.PlayingSongId == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Stop", new Vector2(width / 2, buttonHeight)))
            Stop();
        if (BGMController.PlayingSongId == 0) ImGui.EndDisabled();
        
        ImGui.SameLine();
        
        ImGui.BeginDisabled(!song.FileExists);
        if (ImGui.Button("Play", new Vector2(width / 2, buttonHeight)))
            Play(selectedSong);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawBgmTooltip(song);
    }

    private void DrawReplacements()
    {
        ImGui.BeginChild("##replacementlist");
        DrawCurrentReplacement();
        DrawReplacementList();
        ImGui.EndChild();
    }

    private void DrawReplacementList()
    {
        foreach (var replacement in OrchestrionPlugin.Configuration.SongReplacements.Values)
        {
            ImGui.Spacing();
            SongList.TryGetSong(replacement.TargetSongId, out var target);

            var targetText = $"{replacement.TargetSongId} - {target.Name}";
            string replText = replacement.ReplacementId == SongReplacement.NoChangeId ? NoChange : $"{replacement.ReplacementId} - {SongList.GetSong(replacement.ReplacementId).Name}";
            
            ImGui.TextWrapped($"{targetText}");
            if (ImGui.IsItemHovered())
                DrawBgmTooltip(target);

            ImGui.Text($"will be replaced with");
            ImGui.TextWrapped($"{replText}");
            if (ImGui.IsItemHovered() && replacement.ReplacementId != SongReplacement.NoChangeId)
                DrawBgmTooltip(SongList.GetSong(replacement.ReplacementId));

            // Buttons in bottom right of area
            RightAlignButtons(ImGui.GetCursorPosY(), new[] {"Edit", "Delete"});
            if (ImGui.Button($"Edit##{replacement.TargetSongId}"))
            {
                removalList.Add(replacement.TargetSongId);
                tmpReplacement.TargetSongId = replacement.TargetSongId;
                tmpReplacement.ReplacementId = replacement.ReplacementId;
            }
            ImGui.SameLine();
            if (ImGui.Button($"Delete##{replacement.TargetSongId}"))
                removalList.Add(replacement.TargetSongId);

            ImGui.Separator();
        }

        if (removalList.Count > 0)
        {
            foreach (var toRemove in removalList)
                OrchestrionPlugin.Configuration.SongReplacements.Remove(toRemove);
            removalList.Clear();
            OrchestrionPlugin.Configuration.Save();
        }
    }

    private void DrawDebug()
    {
        var addr = BGMAddressResolver.BGMSceneManager;
        if (addr == IntPtr.Zero) return;
        var addrStr = $"{addr.ToInt64():X}";
        ImGui.Text(addrStr);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            ImGui.SetClipboardText(addrStr);
        ImGui.Text($"streaming enabled: {BGMAddressResolver.StreamingEnabled}");
        ImGui.Text($"PlayingScene: {BGMController.PlayingScene}");
        ImGui.Text($"PlayingSongId: {BGMController.PlayingSongId}");
        ImGui.Text($"OldScene: {BGMController.OldScene}");
        ImGui.Text($"OldSongId: {BGMController.OldSongId}");
        ImGui.Text($"OldSecondScene: {BGMController.OldSecondScene}");
        ImGui.Text($"OldSecondSongId: {BGMController.OldSecondSongId}");
    }

    private void RightAlignButton(float y, string text)
    {
        var style = ImGui.GetStyle();
        var padding = style.WindowPadding.X + style.FramePadding.X * 2 + style.ScrollbarSize;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X - padding);
        ImGui.SetCursorPosY(y);
    }
    
    private void RightAlignButtons(float y, string[] texts)
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

    private void DrawCurrentReplacement()
    {
        ImGui.Spacing();

        var targetText = $"{SongList.GetSong(tmpReplacement.TargetSongId).Id} - {SongList.GetSong(tmpReplacement.TargetSongId).Name}";
        string replacementText;
        if (tmpReplacement.ReplacementId == SongReplacement.NoChangeId)
            replacementText = NoChange;
        else
            replacementText = $"{SongList.GetSong(tmpReplacement.ReplacementId).Id} - {SongList.GetSong(tmpReplacement.ReplacementId).Name}";

        // This fixes the ultra-wide combo boxes, I guess
        var width = ImGui.GetWindowWidth() * 0.60f;

        if (ImGui.BeginCombo("Target Song", targetText))
        {
            foreach (var song in SongList.GetSongs().Values)
            {
                if (!SearchMatches(song)) continue;
                if (OrchestrionPlugin.Configuration.SongReplacements.ContainsKey(song.Id)) continue;
                var tmpText = $"{song.Id} - {song.Name}";
                var tmpTextSize = ImGui.CalcTextSize(tmpText);
                var isSelected = tmpReplacement.TargetSongId == song.Id;
                if (ImGui.Selectable(tmpText, isSelected, ImGuiSelectableFlags.None, new Vector2(width, tmpTextSize.Y)))
                    tmpReplacement.TargetSongId = song.Id;
                if (ImGui.IsItemHovered())
                    DrawBgmTooltip(song);
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        if (ImGui.BeginCombo("Replacement Song", replacementText))
        {
            if (ImGui.Selectable(NoChange))
                tmpReplacement.ReplacementId = SongReplacement.NoChangeId;

            foreach (var song in SongList.GetSongs().Values)
            {
                if (!SearchMatches(song)) continue;
                var tmpText = $"{song.Id} - {song.Name}";
                var tmpTextSize = ImGui.CalcTextSize(tmpText);
                var isSelected = tmpReplacement.ReplacementId == song.Id;
                if (ImGui.Selectable(tmpText, isSelected, ImGuiSelectableFlags.None, new Vector2(width, tmpTextSize.Y)))
                    tmpReplacement.ReplacementId = song.Id;
                if (ImGui.IsItemHovered())
                    DrawBgmTooltip(song);
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        RightAlignButton(ImGui.GetCursorPosY(), "Add as song replacement");
        if (ImGui.Button("Add as song replacement"))
        {
            OrchestrionPlugin.Configuration.SongReplacements.Add(tmpReplacement.TargetSongId, tmpReplacement);
            OrchestrionPlugin.Configuration.Save();
            ResetReplacement();
        }

        ImGui.Separator();
    }

    private bool SearchMatches(Song song)
    {
        var matchesSearch = searchText.Length != 0
                            && (song.Name.ToLower().Contains(searchText.ToLower())
                                || song.Locations.ToLower().Contains(searchText.ToLower())
                                || song.AdditionalInfo.ToLower().Contains(searchText.ToLower())
                                || song.Id.ToString().Contains(searchText));
        var searchEmpty = searchText.Length == 0;
        return matchesSearch || searchEmpty;
    }

    private void DrawSonglist(bool favoritesOnly)
    {
        // to keep the tab bar always visible and not have it get scrolled out
        ImGui.BeginChild("##songlist_internal", ScaledVector2(-1f, -25f));

        if (ImGui.BeginTable("songlist table", 4, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("fav", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("id", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("title", ImGuiTableColumnFlags.WidthStretch);
            
            foreach (var s in SongList.GetSongs())
            {
                var song = s.Value;
                if (!SearchMatches(song))
                    continue;

                var isFavorite = SongList.IsFavorite(song.Id);

                if (favoritesOnly && !isFavorite)
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                if (isFavorite)
                {
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
                    ImGui.Image(favoriteIcon.ImGuiHandle, new Vector2(13, 13));
                }

                ImGui.TableNextColumn();

                ImGui.Text(song.Id.ToString());
                ImGui.TableNextColumn();

                var flags = song.FileExists ? ImGuiSelectableFlags.AllowDoubleClick : ImGuiSelectableFlags.None;
                if (!song.FileExists)
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);

                if (ImGui.Selectable($"{song.Name}##{song.Id}", selectedSong == song.Id, flags))
                {
                    selectedSong = song.Id;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        Play(selectedSong);
                }

                if (!song.FileExists)
                    ImGui.PopStyleColor();

                if (ImGui.IsItemHovered())
                    DrawBgmTooltip(song);

                if (ImGui.BeginPopupContextItem())
                {
                    if (!isFavorite)
                    {
                        if (ImGui.Selectable("Add to favorites"))
                            SongList.AddFavorite(song.Id);
                    }
                    else
                    {
                        if (ImGui.Selectable("Remove from favorites"))
                            SongList.RemoveFavorite(song.Id);
                    }
                    if (ImGui.Selectable("Copy to clipboard"))
                        ImGui.SetClipboardText(song.Name);
                    ImGui.EndPopup();
                }

                ImGui.NextColumn();
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        DrawFooter();
    }

    private void DrawSongHistory()
    {
        // to keep the tab bar always visible and not have it get scrolled out
        ImGui.BeginChild("##songlist_internal", new Vector2(-1f, -60f));
        
        if (ImGui.BeginTable("songlist table", 4, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("fav", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("id", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("title", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("time", ImGuiTableColumnFlags.WidthFixed);

            var now = DateTime.Now;

            // going from the end of the list
            for (int i = songHistory.Count - 1; i >= 0; i--)
            {
                var songHistoryEntry = songHistory[i];
                var song = SongList.GetSong(songHistoryEntry.Id);

                if (!SearchMatches(song))
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var isFavorite = SongList.IsFavorite(song.Id);

                if (isFavorite)
                {
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
                    ImGui.Image(favoriteIcon.ImGuiHandle, new Vector2(13, 13));
                }

                ImGui.TableNextColumn();

                ImGui.Text(song.Id.ToString());

                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{song.Name}##{i}", selectedHistoryEntry == i, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    selectedSong = song.Id;
                    selectedHistoryEntry = i;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        Play(selectedSong);
                }

                if (ImGui.IsItemHovered())
                    DrawBgmTooltip(song);

                if (ImGui.BeginPopupContextItem())
                {
                    if (!isFavorite)
                    {
                        if (ImGui.Selectable("Add to favorites"))
                            SongList.AddFavorite(song.Id);
                    }
                    else
                    {
                        if (ImGui.Selectable("Remove from favorites"))
                            SongList.RemoveFavorite(song.Id);
                    }
                    if(ImGui.Selectable("Copy to clipboard"))
                        ImGui.SetClipboardText(song.Name);
                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn();

                var deltaTime = now - songHistoryEntry.TimePlayed;
                var unit = deltaTime.TotalMinutes >= 1 ? (int)deltaTime.TotalMinutes : (int)deltaTime.TotalSeconds;
                var label = deltaTime.TotalMinutes >= 1 ? MinAgo : SecAgo;
                ImGui.Text($"{unit}{label}");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        DrawFooter(true);
    }

    private void DrawSettings()
    {
        if (!settingsVisible)
            return;

        var stream = BGMAddressResolver.StreamingEnabled;
        //var height = stream ? 170 : 240;
        
        //ImGui.SetNextWindowSize(ScaledVector2(520, 200), ImGuiCond.Always);
        if (ImGui.Begin("Orchestrion Settings", ref settingsVisible/*, ImGuiWindowFlags.NoResize |*ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse*/))
        {
            // ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            // Choose ListLanguage
            if (ImGui.BeginCombo("Song List ListLanguage", OrchestrionPlugin.Configuration.ListLanguage))
            {
                foreach (string lang in Multilanguage.SupportedListLanguages)
                {
                    bool isSelected = OrchestrionPlugin.Configuration.ListLanguage == lang;
                    if (ImGui.Selectable(lang, ref isSelected))
                    {
                        PluginLog.Information($"Song List ListLanguage changed to {lang}.Downloading from {Multilanguage.SongListUrls[lang]}");
                        Multilanguage.UpdateLangugae(lang);
                        orch.UpdateDteOnLanguageChanged();
                    }
                }

                ImGui.EndCombo();
            }
            // Enable Auto Update
            var autoUpdate = OrchestrionPlugin.Configuration.AutoUpdate;
            if (ImGui.Checkbox("Auto Update Song List", ref autoUpdate))
            {
                OrchestrionPlugin.Configuration.AutoUpdate = autoUpdate;
                OrchestrionPlugin.Configuration.Save();
            }
            var showSongInTitlebar = OrchestrionPlugin.Configuration.ShowSongInTitleBar;
            if (ImGui.Checkbox("Show current song in player title bar", ref showSongInTitlebar))
            {
                OrchestrionPlugin.Configuration.ShowSongInTitleBar = showSongInTitlebar;
                OrchestrionPlugin.Configuration.Save();
            }

            var showSongInChat = OrchestrionPlugin.Configuration.ShowSongInChat;
            if (ImGui.Checkbox("Show \"Now playing\" messages in game chat when the current song changes", ref showSongInChat))
            {
                OrchestrionPlugin.Configuration.ShowSongInChat = showSongInChat;
                OrchestrionPlugin.Configuration.Save();
            }

            var showNative = OrchestrionPlugin.Configuration.ShowSongInNative;
            if (ImGui.Checkbox("Show current song in the \"server info\" UI element in-game", ref showNative))
            {
                OrchestrionPlugin.Configuration.ShowSongInNative = showNative;
                OrchestrionPlugin.Configuration.Save();
            }

            if (!showNative)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            var showIdNative = OrchestrionPlugin.Configuration.ShowIdInNative;
            if (ImGui.Checkbox("Show song ID in the \"server info\" UI element in-game", ref showIdNative) && showNative)
            {
                OrchestrionPlugin.Configuration.ShowIdInNative = showIdNative;
                OrchestrionPlugin.Configuration.Save();
            }

            if (!showNative)
                ImGui.PopStyleVar();

            var handleSpecial = OrchestrionPlugin.Configuration.HandleSpecialModes;
            if (ImGui.Checkbox("Handle special \"in-combat\" and mount movement BGM modes", ref handleSpecial))
            {
                OrchestrionPlugin.Configuration.HandleSpecialModes = handleSpecial;
                OrchestrionPlugin.Configuration.Save();
            }

            if (!stream)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.TextWrapped("Audio streaming is disabled. This may be due to Sound Filter or a third-party plugin. The above setting may not work as " +
                                  "expected and you may encounter other audio issues such as popping or tracks not swapping channels. This is not" +
                                  " related to the Orchestrion Plugin.");
                ImGui.PopStyleColor();
            }
        }

        ImGui.End();
    }

    private void DrawBgmTooltip(Song bgm)
    {
        if (bgm.Id == 0) return;
        if (bgmTooltipLock) return;
        bgmTooltipLock = true;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(450 * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Song Info");
        ImGui.TextWrapped($"Title: {bgm.Name}");
        ImGui.TextWrapped(string.IsNullOrEmpty(bgm.Locations) ? "Location: Unknown" : $"Location: {bgm.Locations}");
        if (!string.IsNullOrEmpty(bgm.AdditionalInfo))
            ImGui.TextWrapped($"Info: {bgm.AdditionalInfo}");
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
        if (!bgm.FileExists)
            ImGui.TextWrapped("This song is unavailable; the track is not present in the current game files.");
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // static void HelpMarker(string desc)
    // {
    //     ImGui.TextDisabled("(?)");
    //     if (ImGui.IsItemHovered())
    //     {
    //         ImGui.BeginTooltip();
    //         ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
    //         ImGui.TextUnformatted(desc);
    //         ImGui.PopTextWrapPos();
    //         ImGui.EndTooltip();
    //     }
    // }

    private ImGuiScene.TextureWrap LoadUIImage(string imageFile)
    {
        var path = Path.Combine(Path.GetDirectoryName(OrchestrionPlugin.PluginInterface.AssemblyLocation.FullName), imageFile);
        return OrchestrionPlugin.PluginInterface.UiBuilder.LoadImage(path);
    }

    private static Vector2 ScaledVector2(float x, float y)
    {
        return new Vector2(x * Scale, y * Scale);
    }
}