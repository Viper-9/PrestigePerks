using System;
using System.Collections.Generic;
using System.IO;
using PrestigePerks.Model;

namespace PrestigePerks.Game
{
    /// <summary>
    /// 플러그인 폴더의 언어 파일 (<see cref="PerkLocalePack"/>). en·kr 을 함께 배포하고, 다른 언어는 사용자가 &lt;언어 코드&gt;.json 을 넣는다.
    /// 부를 때마다 읽는다 (게임 안에서 언어를 바꾸거나 파일을 고친 뒤 페이지를 다시 열면 반영된다).
    /// </summary>
    internal static class PerkLocaleFolder
    {
        public const string FolderName = "Locales";

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static string Folder =>
            Path.Combine(Path.GetDirectoryName(typeof(PerkLocaleFolder).Assembly.Location) ?? "", FolderName);

        /// <summary>그 언어 파일의 특전 문구. 파일이 없거나 읽지 못하면 null.</summary>
        public static Dictionary<string, string> Texts(string culture) => Read(culture)?.Texts;

        /// <summary>그 언어 파일의 게임 문구 ("game:" 을 뗀 키). 없으면 null.</summary>
        public static Dictionary<string, string> Game(string culture)
        {
            var game = Read(culture)?.Game;
            return game != null && game.Count > 0 ? game : null;
        }

        private static (Dictionary<string, string> Texts, Dictionary<string, string> Game)? Read(string culture)
        {
            if (string.IsNullOrEmpty(culture) || culture.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            var path = Path.Combine(Folder, culture + ".json");
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var file = PerkLocale.ParseFile(File.ReadAllText(path));
                if (file.Count == 0)
                {
                    LogOnce(culture, () => Log.Warn($"locale file {path} is empty or not valid JSON, built-in texts used"));
                    return null;
                }

                var split = PerkLocalePack.Split(file);
                LogOnce(culture, () => Log.Info($"locale file {culture}: {path} ({split.Texts.Count} texts, {split.Game.Count} game texts)"));
                return split;
            }
            catch (Exception e)
            {
                LogOnce(culture, () => Log.Warn($"locale file {path} not read: {e.Message}"));
                return null;
            }
        }

        // 페이지를 열 때마다 읽으므로 언어마다 한 번만 남긴다
        private static void LogOnce(string culture, Action log)
        {
            if (Logged.Add(culture))
            {
                log();
            }
        }
    }
}
