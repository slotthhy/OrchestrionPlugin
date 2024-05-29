using System.Collections.Generic;
using System.Linq;
using CheapLoc;
using Dalamud.Game.Text;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Orchestrion.BGMSystem;
using Orchestrion.Persistence;
using Orchestrion.Types;

namespace Orchestrion.UI.Windows;

public class SettingsWindow : Window
{
	public SettingsWindow() : base("Orchestrion Settings###orchsettings", ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse)
    {
        SizeCondition = ImGuiCond.FirstUseEver;
    }
    
    public override void PreDraw()
    {
        Size = ImGuiHelpers.ScaledVector2(720, 520);
    }

    private readonly int[] _fateIds = [
        36, // Torn from the Heavens
        68, // The Tug of Fate
        69, // To the Fore
        115, // Hard to Miss
        154, // Thunderer
        165 // Torn from the Heavens
    ];

    private static void Checkbox(string text, Func<bool> get, Action<bool> set, Action<bool> onChange = null)
    {
        var value = get();
        var backup = value;
        if (ImGui.Checkbox($"##orch_{text}", ref value))
        {
            set(value);
            Configuration.Instance.Save();
        }
        if (value != backup)
            onChange?.Invoke(value);
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private static void DropDown(string text,
        Func<string> get,
        Action<string> set, 
        Func<string, bool> isSelected, 
        List<string> items, 
        Func<string, string> displayFunc = null,
        Action<bool> onChange = null)
    {
        var value = get();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        using var combo = ImRaii.Combo(text, value);
        if (!combo.Success)
        {
            // ImGui.PopItemWidth();
            return;
        }
        foreach (var item in items) {
            var display = displayFunc != null ? displayFunc(item) : item;
            if (ImGui.Selectable(display, isSelected(item)))
            {
                set(item);
                Configuration.Instance.Save();
            }
        }
        if (get() != value)
            onChange?.Invoke(true);
        // ImGui.PopItemWidth();
    }
    
    public override void Draw()
	{
        using (OrchestrionPlugin.LargeFont.Push())
        {
            ImGui.Text(Loc.Localize("GeneralSettings", "General Settings"));    
        }
        
        ImGui.PushItemWidth(500f);
        
        Checkbox(Loc.Localize("ShowSongTitleBar",
            "Show current song in player title bar"),
            () => Configuration.Instance.ShowSongInTitleBar,
            b => Configuration.Instance.ShowSongInTitleBar = b);
        
        Checkbox(Loc.Localize("ShowNowPlayingChat",
            "Show \"Now playing\" messages in game chat when the current song changes"), 
            () => Configuration.Instance.ShowSongInChat,
            b => Configuration.Instance.ShowSongInChat = b);

        Checkbox(Loc.Localize("ShowSongServerInfo",
            "Show current song in the \"server info\" UI element in-game"),
            () => Configuration.Instance.ShowSongInNative, 
            b => Configuration.Instance.ShowSongInNative = b);

        if (!Configuration.Instance.ShowSongInNative)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        
        Checkbox(Loc.Localize("ShowSongIdServerInfo", 
            "Show song ID in the \"server info\" UI element in-game"), 
            () => Configuration.Instance.ShowIdInNative, 
            b => Configuration.Instance.ShowIdInNative = b); 
        
        if (!Configuration.Instance.ShowSongInNative)
            ImGui.PopStyleVar();
        
        Checkbox(Loc.Localize("HandleSpecialModes", 
            "Handle special \"in-combat\" and mount movement BGM modes"), 
            () => Configuration.Instance.HandleSpecialModes, 
            b => Configuration.Instance.HandleSpecialModes = b);
        
        if (!BGMAddressResolver.StreamingEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextWrapped(Loc.Localize("AudioStreamingDisabledWarning" , 
                "Audio streaming is disabled. This may be due to Sound Filter or a third-party plugin. The above setting may not work as " +
                              "expected and you may encounter other audio issues such as popping or tracks not swapping channels. This is not" +
                              " related to the Orchestrion Plugin."));
            ImGui.PopStyleColor();
        }
        
        Checkbox(Loc.Localize("UseDalamudChannelSetting", 
                "Use the chat channel selected in Dalamud's settings for Orchestrion's chat messages (default)"),
            () => Configuration.Instance.ChatChannelMatchDalamud, 
            b => Configuration.Instance.ChatChannelMatchDalamud = b,
            b =>
            {
                if (!b) return;
                Configuration.Instance.ChatType = DalamudApi.PluginInterface.GeneralChatType;
                Configuration.Instance.Save();
            });

        ImGui.BeginDisabled(Configuration.Instance.ChatChannelMatchDalamud);
        ImGui.Indent(30f * ImGuiHelpers.GlobalScale);
        DropDown(Loc.Localize("ChatChannelSetting", "Chat channel used for Orchestrion messages"), 
            () => Configuration.Instance.ChatType.ToString(), 
            s => Configuration.Instance.ChatType = Enum.Parse<XivChatType>(s), 
            s => s == Configuration.Instance.ChatType.ToString(), 
            Enum.GetValues<XivChatType>().Select(c => c.ToString()).ToList());
        ImGui.Indent(-1 * 30f * ImGuiHelpers.GlobalScale);
        ImGui.EndDisabled();

        Checkbox(Loc.Localize("DisableFateMusic",
            "Prevent fate music from overriding location music"),
            () => Configuration.Instance.DisableFateMusic,
            b => Configuration.Instance.DisableFateMusic = b,
            ToggleFateSongReplacements);

        using (OrchestrionPlugin.LargeFont.Push())
        {
            ImGui.Text(Loc.Localize("LocSettings", "Localization Settings"));
        }
        
        Checkbox(Loc.Localize("UseDalamudLanguageSetting", 
                "Use the language selected in Dalamud's settings for the Orchestrion Plugin's UI"),
            () => Configuration.Instance.UserInterfaceLanguageMatchDalamud, 
            b => Configuration.Instance.UserInterfaceLanguageMatchDalamud = b,
            b =>
            {
                if (!b) return;
                Configuration.Instance.UserInterfaceLanguageCode = DalamudApi.PluginInterface.UiLanguage;
                Configuration.Instance.Save();
                OrchestrionPlugin.LanguageChanged(Configuration.Instance.UserInterfaceLanguageCode);
            });

        ImGui.BeginDisabled(Configuration.Instance.UserInterfaceLanguageMatchDalamud);
        ImGui.Indent(30f * ImGuiHelpers.GlobalScale);
        DropDown(Loc.Localize("UILanguageSetting",
                "Language used for the Orchestrion Plugin's UI"),
            () => Util.LangCodeToLanguage(Configuration.Instance.UserInterfaceLanguageCode),
            s => Configuration.Instance.UserInterfaceLanguageCode = s,
            s => s == Configuration.Instance.UserInterfaceLanguageCode,
            Util.AvailableLanguages,
            Util.LangCodeToLanguage,
            _ =>
            {
                OrchestrionPlugin.LanguageChanged(Configuration.Instance.UserInterfaceLanguageCode);
            });
        ImGui.Indent(-1 * 30f * ImGuiHelpers.GlobalScale);
        ImGui.EndDisabled();
        
        Checkbox(Loc.Localize("ShowAltLangTitles", 
                "Show alternate language song titles in tooltips"), 
            () => Configuration.Instance.ShowAltLangTitles, 
            b => Configuration.Instance.ShowAltLangTitles = b);
        
        ImGui.BeginDisabled(!Configuration.Instance.ShowAltLangTitles);
        ImGui.Indent(30f * ImGuiHelpers.GlobalScale);
        DropDown(Loc.Localize("AltLangLanguageSetting", 
            "Alternate language for song titles in tooltips"), 
            () => Util.LangCodeToLanguage(Configuration.Instance.AltTitleLanguageCode), 
            s => Configuration.Instance.AltTitleLanguageCode = s, 
            s => s == Configuration.Instance.AltTitleLanguageCode, 
            Util.AvailableTitleLanguages,
            Util.LangCodeToLanguage);
        ImGui.Indent(-1 * 30f * ImGuiHelpers.GlobalScale);
        ImGui.EndDisabled();
        
        DropDown(Loc.Localize("ServerInfoLanguageSetting", 
                "Language used for song titles in the \"server info\" UI element in-game"), 
            () => Util.LangCodeToLanguage(Configuration.Instance.ServerInfoLanguageCode), 
            s => Configuration.Instance.ServerInfoLanguageCode = s, 
            s => s == Configuration.Instance.ServerInfoLanguageCode, 
            Util.AvailableTitleLanguages,
            Util.LangCodeToLanguage);
        
        DropDown(Loc.Localize("ChatMessageLanguageSetting", 
                "Language used for song titles in Orchestrion chat messages in-game"), 
            () => Util.LangCodeToLanguage(Configuration.Instance.ChatLanguageCode), 
            s => Configuration.Instance.ChatLanguageCode = s, 
            s => s == Configuration.Instance.ChatLanguageCode, 
            Util.AvailableTitleLanguages,
            Util.LangCodeToLanguage);
        
        using (OrchestrionPlugin.LargeFont.Push())
        {
            ImGui.Text(Loc.Localize("MiniPlayerSettings", "Mini Player Settings"));   
        }
        
        Checkbox(Loc.Localize("ShowMiniPlayer", "Show mini player"),
            () => Configuration.Instance.ShowMiniPlayer, 
            b => Configuration.Instance.ShowMiniPlayer = b);

        Checkbox(Loc.Localize("LockMiniPlayer", "Lock mini player"), 
            () => Configuration.Instance.MiniPlayerLock, 
            b => Configuration.Instance.MiniPlayerLock = b);
        
        var miniPlayerOpacity = Configuration.Instance.MiniPlayerOpacity;
        ImGui.PushItemWidth(200f);
        if (ImGui.SliderFloat($"##orch_MiniPlayerOpacity", ref miniPlayerOpacity, 0.01f, 1.0f))
        {
            Configuration.Instance.MiniPlayerOpacity = miniPlayerOpacity;
            Configuration.Instance.Save();
        }
        ImGui.SameLine();
        ImGui.TextWrapped(Loc.Localize("MiniPlayerOpacity", "Mini player opacity"));
        ImGui.PopItemWidth();
        ImGui.PopItemWidth();
    }

    private void ToggleFateSongReplacements(bool b)
    {
        if (b)
        {
            foreach (var songReplacement in _fateIds.Select(fateSongId => new SongReplacementEntry { TargetSongId = fateSongId, ReplacementId = -1 }))
            {
                Configuration.Instance.SongReplacements.Add(songReplacement.TargetSongId, songReplacement);
            }

            Configuration.Instance.Save();
        }
        else
        {
            foreach (var fateSongId in _fateIds.Where(Configuration.Instance.SongReplacements.ContainsKey))
            {
                if (Configuration.Instance.SongReplacements[fateSongId].ReplacementId == -1)
                {
                    Configuration.Instance.SongReplacements.Remove(fateSongId);
                }
            }
        }
    }
}