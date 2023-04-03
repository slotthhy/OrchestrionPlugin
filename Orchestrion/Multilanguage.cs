using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrion
{
    public static class Multilanguage
    {
        // Supported SongList language list
        public static string[] SupportedListLanguages = new string[] { "en", "jp" };

        public static Dictionary<string, string> SongListUrls = new Dictionary<string, string>()
        {
            {
                "en",
                @"https://docs.google.com/spreadsheets/d/1oJOGB3UUGHeaLAHQIDftNjUEj6h9lVex3LlfvYuUVk8/gviz/tq?tqx=out:csv&sheet=main"
            },
            {
                "jp",
                @"https://docs.google.com/spreadsheets/d/1oJOGB3UUGHeaLAHQIDftNjUEj6h9lVex3LlfvYuUVk8/gviz/tq?tqx=out:csv&sheet=jp"
            }
        };

        public static bool IfUpdatingLanguage = false;
        private static string previousLanguage = "";
        private static string previousUrl = "";
        public static void UpdateLangugae(string lang)
        {
            // backup settings
            previousLanguage = OrchestrionPlugin.Configuration.ListLanguage;
            previousUrl = OrchestrionPlugin.Configuration.SongListPath;
            // update language
            OrchestrionPlugin.Configuration.ListLanguage = lang;
            OrchestrionPlugin.Configuration.SongListPath = Multilanguage.SongListUrls[lang];
            IfUpdatingLanguage =true;
            SongList.SheetPath = OrchestrionPlugin.Configuration.SongListPath;
            SongList.Init(OrchestrionPlugin.PluginInterface.AssemblyLocation.DirectoryName);
            BGMController.Update();
            OrchestrionPlugin.Configuration.Save();
        }

        public static void UpdateFailed()
        {
            // if update failed, restore previous language setting
            OrchestrionPlugin.Configuration.ListLanguage=previousLanguage;
            OrchestrionPlugin.Configuration.SongListPath=previousUrl;
            OrchestrionPlugin.Configuration.Save();
            IfUpdatingLanguage = false;
        }
    }
}