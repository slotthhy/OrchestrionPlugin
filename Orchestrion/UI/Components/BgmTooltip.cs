using System.Numerics;
using CheapLoc;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ImGuiNET;
using Orchestrion.Persistence;
using Orchestrion.Types;

namespace Orchestrion.UI.Components;

public static class BgmTooltip
{
	private static bool _bgmTooltipLock;

	public static void ClearLock() => _bgmTooltipLock = false;

	public static void DrawBgmTooltip(Song bgm)
	{
		if (bgm.Id == 0) return;
		if (_bgmTooltipLock) return;
		_bgmTooltipLock = true;

		ImGui.BeginTooltip();
		ImGui.PushTextWrapPos(450 * ImGuiHelpers.GlobalScale);
		ImGui.TextColored(new Vector4(0, 1, 0, 1), Loc.Localize("SongInfo", "Song Info"));
		
		ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("TitleColon", "Title: "));
		ImGui.SameLine();
		ImGui.TextWrapped(bgm.Name);
		
		if (Configuration.Instance.ShowAltLangTitles)
		{
			var code = Configuration.Instance.AltTitleLanguageCode;

			if (bgm.Strings.TryGetValue(code, out var altLangStrings) && altLangStrings.Name != bgm.Name && !string.IsNullOrEmpty(altLangStrings.Name)) {
				DisplayDetail(altLangStrings);
			} else if (bgm.Strings.TryGetValue("en", out altLangStrings) && altLangStrings.Name != bgm.Name && !string.IsNullOrEmpty(altLangStrings.Name)) {
				DisplayDetail(altLangStrings);
			}

				void DisplayDetail(SongStrings altLangStrings)
			{
				var label = Loc.Localize("TitleColon", "Title: ");
				label = $"[{code}] {label}";
				ImGui.TextColored(ImGuiColors.DalamudGrey, label);
				ImGui.SameLine();
				using var _ = code == "zh" ? OrchestrionPlugin.CnFont.Push() : null;
				ImGui.TextWrapped(altLangStrings.Name);
			}
		}

		if (!string.IsNullOrEmpty(bgm.AlternateName))
		{
			ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("AlternateTitleColon", "Alternate Title: "));
			ImGui.SameLine();
			ImGui.TextWrapped(bgm.AlternateName);

			if (Configuration.Instance.ShowAltLangTitles)
			{
				var code = Configuration.Instance.AltTitleLanguageCode;
				var altLangAltTitle = bgm.Strings[code].AlternateName;
				if (bgm.AlternateName != altLangAltTitle && !string.IsNullOrEmpty(altLangAltTitle))
				{
					var label = Loc.Localize("AlternateTitleColon", "Alternate Title: ");
					label = $"[{code}] {label}";
					ImGui.TextColored(ImGuiColors.DalamudGrey, label);
					ImGui.SameLine();
					using var _ = code == "zh" ? OrchestrionPlugin.CnFont.Push() : null;
					ImGui.TextWrapped(altLangAltTitle);
				}
			}
		}
		
		if (!string.IsNullOrEmpty(bgm.SpecialModeName))
		{
			ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("SpecialModeTitleColon", "Special Mode Title: "));
			ImGui.SameLine();
			ImGui.TextWrapped(bgm.SpecialModeName);
			
			if (Configuration.Instance.ShowAltLangTitles)
			{
				var code = Configuration.Instance.AltTitleLanguageCode;
				var altLangSpecialModeName = bgm.Strings[code].SpecialModeName;
				if (bgm.SpecialModeName != altLangSpecialModeName)
				{
					var label = Loc.Localize("SpecialModeTitleColon", "Special Mode Title: ");
					label = $"[{code}] {label}";
					ImGui.TextColored(ImGuiColors.DalamudGrey, label);
					ImGui.SameLine();
					using var _ = code == "zh" ? OrchestrionPlugin.CnFont.Push() : null;
					ImGui.TextWrapped(altLangSpecialModeName);
				}
			}
		}
		
		ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("LocationColon", "Location: "));
		ImGui.SameLine();
		ImGui.TextWrapped(string.IsNullOrEmpty(bgm.Locations) ? Loc.Localize("Unknown", "Unknown") : bgm.Locations);
		if (!string.IsNullOrEmpty(bgm.AdditionalInfo))
		{
			ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("InfoColon", "Info: "));
			ImGui.SameLine();
			ImGui.TextWrapped(bgm.AdditionalInfo);
		}
		ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("DurationColon", "Duration: "));
		ImGui.SameLine();
		ImGui.TextWrapped($"{bgm.Duration:mm\\:ss}");
		ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
		if (!bgm.FileExists)
			ImGui.TextWrapped(Loc.Localize("SongNotFound", "This song is unavailable; the track is not present in the current game files."));
		ImGui.PopStyleColor();
		if (Configuration.Instance.ShowFilePaths)
		{
			ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Localize("FilePathColon", "File Path: "));
			ImGui.SameLine();
			ImGui.TextWrapped(bgm.FilePath);
		}

		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}
}