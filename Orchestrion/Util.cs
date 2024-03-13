using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud;
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
}