using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Lumina.Excel.GeneratedSheets;
using Orchestrion.Types;

namespace Orchestrion.Persistence;

public class SongList
{
    private const string SheetPath = @"https://docs.google.com/spreadsheets/d/1qAkxPiXWF-EUHbIXdNcO-Ilo2AwLnqvdpW9tjKPitPY/gviz/tq?tqx=out:csv&sheet={0}";
    private const string SheetFileName = "xiv_bgm_{0}.csv";
    private readonly Dictionary<int, Song> _songs;
    private readonly HttpClient _client = new(){Timeout = TimeSpan.FromSeconds(10)};
    
    private const string SheetPathCN = @"https://s3.ffxiv.wang/xlassets/pluginfiles/Orch_{0}.csv";
    //Update at: https://github.com/ottercorp/XLAssets/tree/master/pluginfiles

    private static SongList _instance;
    public static SongList Instance => _instance ??= new SongList();

    private SongList()
    {
        _songs = new Dictionary<int, Song>();
        
        try
        {
            DalamudApi.PluginLog.Information("[SongList] Checking for updated bgm sheets");
            
            LoadMetadataSheet(GetRemoteSheet("metadata"));
            LoadLangSheet(GetRemoteSheet("en"), "en");
            LoadLangSheet(GetRemoteSheet("ja"), "ja");
            LoadLangSheet(GetRemoteSheet("de"), "de");
            LoadLangSheet(GetRemoteSheet("fr"), "fr");
            LoadLangSheet(GetRemoteSheet("zh"), "zh");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[SongList] Orchestrion failed to update bgm sheet; using previous version");
            LoadMetadataSheet(GetLocalSheet("metadata"));
            LoadLangSheet(GetLocalSheet("en"), "en");
            LoadLangSheet(GetLocalSheet("ja"), "ja");
            LoadLangSheet(GetLocalSheet("de"), "de");
            LoadLangSheet(GetLocalSheet("fr"), "fr");
            LoadLangSheet(GetLocalSheet("zh"), "zh");
        }

        // DebugLogSongs();
    }

    private void DebugLogSongs()
    {
        DalamudApi.PluginLog.Debug($"Songs:");
        foreach (var song in _songs)
        {
            DalamudApi.PluginLog.Debug($"{song.Key}: {song.Value.Id} {song.Value.Strings} {song.Value.FilePath}");
        }
    }
    
    private string GetRemoteSheet(string code)
    {
        if ((uint)DalamudApi.ClientState.ClientLanguage == 4)
            return _client.GetStringAsync(string.Format(SheetPathCN, code)).Result;
        return _client.GetStringAsync(string.Format(SheetPath, code)).Result;
    }

    private string GetLocalSheet(string code)
    {
        return File.ReadAllText(Path.Combine(DalamudApi.PluginInterface.AssemblyLocation.DirectoryName!, string.Format(SheetFileName, code)));
    }

    private void SaveLocalSheet(string text, string code)
    {
        File.WriteAllText(Path.Combine(DalamudApi.PluginInterface.AssemblyLocation.DirectoryName!, string.Format(SheetFileName, code)), text);
    }

    private void LoadMetadataSheet(string sheetText)
    {
        _songs.Clear();
        var bgms = DalamudApi.DataManager.Excel.GetSheet<BGM>()!.ToDictionary(k => k.RowId, v => v);
        var sheetLines = sheetText.Split('\n'); // gdocs provides \n
        for (int i = 1; i < sheetLines.Length; i++)
        {
            // The formatting is odd here because gdocs adds quotes around columns and doubles each single quote
            var elements = sheetLines[i].Split(new[] { "\"," }, StringSplitOptions.None);
            var id = int.Parse(elements[0].Substring(1));
            var durationStr = elements[1].Substring(1, elements[1].Length - 2).Replace("\"\"", "\"");
            var parsed = double.TryParse(durationStr, out var durationDbl);
            var duration = parsed ? TimeSpan.FromSeconds(durationDbl) : TimeSpan.Zero;
            if (!parsed) DalamudApi.PluginLog.Debug($"failed parse {id}: {durationStr}");

            bgms.TryGetValue((uint)id, out var bgm);
            var song = new Song
            {
                Id = id,
                FilePath = bgm?.File ?? "",
                SpecialMode = bgm?.SpecialMode ?? 0,
                DisableRestart = bgm?.DisableRestart ?? false,
                FileExists = bgm != null && DalamudApi.DataManager.FileExists(bgm.File),
                Duration = duration,
            };

            _songs[id] = song;
        }
        SaveLocalSheet(sheetText, "metadata");
    }

    private void LoadLangSheet(string sheetText, string code)
    {
        var sheetLines = sheetText.Split('\n'); // gdocs provides \n
        for (int i = 1; i < sheetLines.Length; i++)
        {
            // The formatting is odd here because gdocs adds quotes around columns and doubles each single quote
            var elements = sheetLines[i].Split(new[] { "\"," }, StringSplitOptions.None);
            var id = int.Parse(elements[0].Substring(1));
            var name = elements[1].Substring(1);
            var altName = elements[2].Substring(1);
            var specialName = elements[3].Substring(1);
            var locations = elements[4].Substring(1);
            var addtlInfo = elements[5].Substring(1, elements[5].Length - 2).Replace("\"\"", "\"");

            if (!_songs.TryGetValue(id, out var song))
                continue;

            if ((code == "en" && string.IsNullOrEmpty(name)) || name == "Null BGM" || name == "test")
                _songs.Remove(id);
            
            song.Strings[code] = new SongStrings
            {
                Name = name,
                AlternateName = altName,
                SpecialModeName = specialName,
                Locations = locations,
                AdditionalInfo = addtlInfo,
            };
        }
        SaveLocalSheet(sheetText, code);
    }

    public bool IsDisableRestart(int id)
    {
        return _songs.TryGetValue(id, out var song) && song.DisableRestart;
    }

    public Dictionary<int, Song> GetSongs()
    {
        return _songs;
    }

    public Song GetSong(int id)
    {
        return _songs.TryGetValue(id, out var song) ? song : default;
    }

    public bool TryGetSong(int id, out Song song)
    {
        return _songs.TryGetValue(id, out song);
    }

    public string GetSongTitle(int id)
    {
        return _songs.TryGetValue(id, out var song) ? song.Name : "";
    }

    public bool SongExists(int id)
    {
        return _songs.ContainsKey(id);
    }

    public bool TryGetSongByName(string name, out int songId)
    {
        songId = 0;
        foreach (var song in _songs.Values)
        {
            foreach (var lang in Util.AvailableTitleLanguages)
            {
                if (string.Equals(song.Strings[lang].Name, name, StringComparison.InvariantCultureIgnoreCase)
                    || string.Equals(song.Strings[lang].AlternateName, name, StringComparison.InvariantCultureIgnoreCase))
                {
                    songId = song.Id;
                    return true;    
                }
            }
        }

        return false;
    }

    public int GetFirstReplacementCandidateId()
    {
        return _songs.Keys.First(x => !Configuration.Instance.SongReplacements.ContainsKey(x));
    }
    
    public bool TryGetRandomSong(string limitToPlaylist, out int songId)
    {
        songId = 0;
        var playlistExists = Configuration.Instance.Playlists.TryGetValue(limitToPlaylist, out var playlist);
        var isAllSongs = limitToPlaylist == string.Empty;
        if (!playlistExists && !isAllSongs) return false;

        ICollection<int> source = !isAllSongs ? playlist.Songs : _songs.Keys;
        if (source.Count == 0) return false;
        if (!source.Any(x => _songs.ContainsKey(x))) return false;
        if (!source.Any(x => _songs[x].FileExists)) return false;

        while (true)
        {
            var index = Random.Shared.Next(source.Count);
            var id = source.ElementAt(index);
            if (!_songs.ContainsKey(id)) continue;
            songId = id;
            return true;
        }
    }
}
