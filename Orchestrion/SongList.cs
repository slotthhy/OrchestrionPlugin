using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using Dalamud.Logging;
using Lumina.Excel.GeneratedSheets;

namespace Orchestrion;

public struct Song
{
    public int Id;
    public string Name;
    public string Locations;
    public string AdditionalInfo;
    public bool DisableRestart;
    public byte SpecialMode;
    public bool FileExists;
}

public static class SongList
{
    public static string SheetPath = @"https://docs.google.com/spreadsheets/d/1oJOGB3UUGHeaLAHQIDftNjUEj6h9lVex3LlfvYuUVk8/gviz/tq?tqx=out:csv&sheet=main";
    private const string SheetFileName = "xiv_bgm.csv";

    private static Dictionary<int, Song> _songs;

    static SongList()
    {
        _songs = new Dictionary<int, Song>();
    }

    public static void Init(string pluginDirectory)
    {
        var sheetPath = Path.Join(pluginDirectory, SheetFileName);
        _songs = new Dictionary<int, Song>();

        var existingText = File.ReadAllText(sheetPath);
        if (!OrchestrionPlugin.Configuration.AutoUpdate)
        {
            // if user disabled auto-update, just load the existing sheet
            LoadSheet(existingText);
            return;
        }
        using var client = new HttpClient();
        try
        {
            PluginLog.Log("Checking for updated bgm sheet");
            var newText = client.GetStringAsync(SheetPath).Result;
#if DEBUG
            PluginLog.Log($"Got updated bgm sheet{newText}");
#endif
            LoadSheet(newText);

            // would really prefer some kind of proper versioning here
            if (newText != existingText)
            {
                File.WriteAllText(sheetPath, newText);
                PluginLog.Log("Updated bgm sheet to new version");
            }

            Multilanguage.IfUpdatingLanguage = false;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Orchestrion failed to update bgm sheet; using previous version");
            if (Multilanguage.IfUpdatingLanguage)
            {
                Multilanguage.UpdateFailed();
            }
            // if this throws, something went horribly wrong and we should just break completely
            LoadSheet(existingText);
        }
    }

    // Attempts to load supplemental bgm data from the csv file
    // This throws all internal errors
    private static void LoadSheet(string sheetText)
    {
        _songs.Clear();
        var bgms = OrchestrionPlugin.DataManager.Excel.GetSheet<BGM>()!.ToDictionary(k => k.RowId, v => v);
        var sheetLines = sheetText.Split('\n'); // gdocs provides \n
        for (int i = 1; i < sheetLines.Length; i++)
        {
            // The formatting is odd here because gdocs adds quotes around columns and doubles each single quote
            var elements = sheetLines[i].Split(new[] { "\"," }, StringSplitOptions.None);
            var id = int.Parse(elements[0].Substring(1));
            var name = elements[1].Substring(1).Replace("\"\"", "\"");

            // Any track without an official name is "???"
            // While Null BGM tracks are also pretty invalid
            if (string.IsNullOrEmpty(name) || name == "Null BGM") continue;

            var location = elements[2].Substring(1).Replace("\"\"", "\"");
            var additionalInfo = elements[3].Substring(1, elements[3].Substring(1).Length - 1).Replace("\"\"", "\"");

            bgms.TryGetValue((uint)id, out var bgm);
            var song = new Song
            {
                Id = id,
                Name = name,
                Locations = location,
                AdditionalInfo = additionalInfo,
                SpecialMode = bgm?.SpecialMode ?? 0,
                DisableRestart = bgm?.DisableRestart ?? false,
                FileExists = bgm != null && OrchestrionPlugin.DataManager.FileExists(bgm.File),
            };

            _songs[id] = song;
        }
    }

    public static bool IsFavorite(int songId) => OrchestrionPlugin.Configuration.FavoriteSongs.Contains(songId);

    public static void AddFavorite(int songId)
    {
        OrchestrionPlugin.Configuration.FavoriteSongs.Add(songId);
        OrchestrionPlugin.Configuration.Save();
    }

    public static void RemoveFavorite(int songId)
    {
        OrchestrionPlugin.Configuration.FavoriteSongs.Remove(songId);
        OrchestrionPlugin.Configuration.Save();
    }

    public static int GetFirstReplacementCandidateId()
    {
        return _songs.Keys.First(x => !OrchestrionPlugin.Configuration.SongReplacements.ContainsKey(x));
    }

    public static Dictionary<int, Song> GetSongs()
    {
        return _songs;
    }

    public static Song GetSong(int id)
    {
        return _songs.TryGetValue(id, out var song) ? song : default;
    }

    public static bool TryGetSong(int id, out Song song)
    {
        return _songs.TryGetValue(id, out song);
    }

    public static string GetSongName(int id)
    {
        return _songs.TryGetValue(id, out var song) ? song.Name : "";
    }

    public static string GetSongTitle(int id)
    {
        try
        {
            return _songs.ContainsKey(id) ? _songs[id].Name : "";
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "GetSongTitle");
        }

        return "";
    }

    public static bool SongExists(int id)
    {
        return _songs.ContainsKey(id);
    }

    public static bool TryGetSongByName(string name, out int songId)
    {
        songId = 0;
        foreach (var song in _songs)
        {
            if (string.Equals(song.Value.Name, name, StringComparison.InvariantCultureIgnoreCase))
            {
                songId = song.Key;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetRandomSong(bool limitToFavorites, out int songId)
    {
        songId = 0;

        ICollection<int> source = limitToFavorites ? OrchestrionPlugin.Configuration.FavoriteSongs : _songs.Keys;
        if (source.Count == 0) return false;

        var max = source.Max();
        var random = new Random();
        var found = false;
        while (!found)
        {
            songId = random.Next(2, max + 1);

            if (!_songs.ContainsKey(songId)) continue;
            if (limitToFavorites && !IsFavorite(songId)) continue;

            found = true;
        }

        return found;
    }
}
