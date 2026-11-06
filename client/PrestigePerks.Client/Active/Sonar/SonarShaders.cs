using System;
using System.Linq;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 음파 탐지 셰이더 두 개 (tools/shaders). DLL 리소스의 번들을 처음 한 번 읽어 계속 쓴다 (번들은 내려놓지 않는다).
    /// 못 읽으면 음파 탐지는 "대상 없음"이 된다.
    /// </summary>
    internal static class SonarShaders
    {
        public const string EchoName = "PrestigePerks/SonarEcho", RingName = "PrestigePerks/SonarRing";
        private const string Resource = "PrestigePerks.Shaders.prestigeperks_shaders.bundle";

        private static bool tried;
        private static AssetBundle bundle;

        public static Shader Echo { get; private set; }
        public static Shader Ring { get; private set; }

        /// <summary>읽은 결과 (CHECK 글).</summary>
        public static string Status { get; private set; } = "not loaded";

        public static bool Load()
        {
            if (tried)
            {
                return Echo != null;
            }

            tried = true;
            try
            {
                using (var stream = typeof(SonarShaders).Assembly.GetManifestResourceStream(Resource))
                {
                    if (stream == null)
                    {
                        Status = $"resource {Resource} missing";
                        return false;
                    }

                    var bytes = new byte[stream.Length];
                    var read = 0;
                    while (read < bytes.Length)
                    {
                        var n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0)
                        {
                            break;
                        }

                        read += n;
                    }

                    bundle = AssetBundle.LoadFromMemory(bytes);
                }

                if (bundle == null)
                {
                    Status = "bundle load failed";
                    return false;
                }

                var shaders = bundle.LoadAllAssets<Shader>();
                Echo = shaders.FirstOrDefault(shader => shader.name == EchoName);
                Ring = shaders.FirstOrDefault(shader => shader.name == RingName);
                Status = $"shaders [{string.Join(", ", shaders.Select(shader => shader.name))}], echo supported {Echo != null && Echo.isSupported}, ring supported {Ring != null && Ring.isSupported}";
                if (Echo != null && !Echo.isSupported)
                {
                    Echo = null;
                }

                if (Ring != null && !Ring.isSupported)
                {
                    Ring = null;
                }

                return Echo != null;
            }
            catch (Exception e)
            {
                Status = $"load failed: {e.Message}";
                return false;
            }
        }
    }
}
