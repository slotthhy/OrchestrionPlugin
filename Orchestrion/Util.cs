using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud;
using System.Reflection;
using Dalamud.Interface;
using ImGuiNET;
using Orchestrion.Persistence;
using Orchestrion.Types;

namespace Orchestrion;

public static class Util
{
	public static List<string> AvailableLanguages => new() { "en", "ja", "de", "fr", "it", "zh" };
	public static List<string> AvailableTitleLanguages => new() { "en", "ja", "zh" };
	
	public static string LangCodeToLanguage(string code)
	{
		return CultureInfo.GetCultureInfo(code).NativeName;
	}
	
	public static Vector2 GetIconSize(FontAwesomeIcon icon)
	{
		ImGui.PushFont(UiBuilder.IconFont);
		var size = ImGui.CalcTextSize(icon.ToIconString());
		ImGui.PopFont();
		return size;
	}

	public static bool SearchMatches(string searchText, int songId)
	{
		return SongList.Instance.TryGetSong(songId, out var song) && SearchMatches(searchText, song);
	}

	public static bool SearchMatches(string searchText, Song song)
	{
		if (searchText.Length == 0) return true;

		var lang = Lang();

		var matchesSearch = false;

		try
		{
			foreach (var titleLang in AvailableTitleLanguages)
			{
				matchesSearch |= song.Strings[titleLang].Name.ToLower().Contains(searchText.ToLower());
				matchesSearch |= song.Strings[titleLang].AlternateName.ToLower().Contains(searchText.ToLower());
				matchesSearch |= song.Strings[titleLang].SpecialModeName.ToLower().Contains(searchText.ToLower());
			}

			// Id check
			matchesSearch |= song.Id.ToString().Contains(searchText);

			// Localized addtl info check
			var strings = song.Strings["en"];
			song.Strings.TryGetValue(lang, out strings);
			matchesSearch |= strings.Locations.ToLower().Contains(searchText.ToLower());
			matchesSearch |= strings.AdditionalInfo.ToLower().Contains(searchText.ToLower());
		}
		catch (Exception ignore)
		{
			
		}
		
		return matchesSearch;
	}

	public static string Lang()
	{
		return Configuration.Instance.UserInterfaceLanguageCode;
	}

	public static void CheckSongData()
	{
		var uiLang = Lang();
		const string uiLangFallback = "en";

		var songs = SongList.Instance.GetSongs();
		var errors = 0;
		var fallbacks = 0;

		foreach (var (songId, song) in songs) {
			foreach (var titleLang in AvailableTitleLanguages) {
				if (!song.Strings.TryGetValue(titleLang, out var strings)) {
					DalamudApi.PluginLog.Warning($"Song ID {songId} is missing string table for title language [{titleLang}]");
					errors++;
				}
				else {
					if (strings.Name == null) {
						DalamudApi.PluginLog.Warning($"Song ID {songId} is missing Name for title language [{titleLang}]");
						errors++;
					}
					if (strings.AlternateName == null) {
						DalamudApi.PluginLog.Warning($"Song ID {songId} is missing AlternateName for title language [{titleLang}]");
						errors++;
					}
					if (strings.SpecialModeName == null) {
						DalamudApi.PluginLog.Warning($"Song ID {songId} is missing SpecialModeName for title language [{titleLang}]");
						errors++;
					}
				}
			}

			if (!song.Strings.TryGetValue(uiLang, out var strings2)) {
				DalamudApi.PluginLog.Info($"Song ID {songId} is missing title info for UI language [{uiLang}], using fallback [{uiLangFallback}]");
				fallbacks++;
			}
			else {
				if (strings2.Locations == null) {
					DalamudApi.PluginLog.Warning($"Song ID {songId} is missing Locations for UI language [{uiLang}]");
					errors++;
				}
				if (strings2.AdditionalInfo == null) {
					DalamudApi.PluginLog.Warning($"Song ID {songId} is missing AdditionalInfo for UI language [{uiLang}]");
					errors++;
				}
				continue;
			}

			if (!song.Strings.TryGetValue(uiLangFallback, out strings2)) {
				DalamudApi.PluginLog.Info($"Song ID {songId} is missing title info for UI fallback language [{uiLangFallback}]");
				errors++;
			}
			else {
				if (strings2.Locations == null) {
					DalamudApi.PluginLog.Warning($"Song ID {songId} is missing Locations for UI fallback language [{uiLangFallback}]");
					errors++;
				}

				if (strings2.AdditionalInfo == null) {
					DalamudApi.PluginLog.Warning($"Song ID {songId} is missing AdditionalInfo for UI fallback language [{uiLangFallback}]");
					errors++;
				}
			}
		}

		var version = Assembly.GetExecutingAssembly().GetName().Version;
		var msg = $"Checked {songs.Count} songs with {errors} errors and {fallbacks} fallbacks found (lang {DalamudApi.PluginInterface.UiLanguage}/{uiLang}, version {(version != null ? version : "unknown")}).";
		DalamudApi.PluginLog.Info(msg);
		DalamudApi.ChatGui.Print(msg);
	}
}