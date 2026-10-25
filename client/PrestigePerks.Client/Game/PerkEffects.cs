using System;
using System.Collections.Generic;
using System.Text;
using PrestigePerks.Model;

namespace PrestigePerks.Game
{
    /// <summary>
    /// 클라이언트 패치 특전의 활성 상태와 효과 비율 (dev-docs 2_특전.md 6절 "패치 공통 규칙").
    /// state 를 받을 때마다 <see cref="Set"/>으로 덮어쓴다. 받지 못했으면 비어 있다 (= 바닐라).
    /// 패치 본문은 <see cref="Has"/>를 가장 먼저 보고, 계수에 <see cref="Scale"/>(단계 비율)을 넘긴다 (배열 읽기만, 할당 없음).
    /// </summary>
    internal static class PerkEffects
    {
        private static float[] scales = new float[PerkPatchMath.Size];
        private static readonly bool[] Disabled = new bool[PerkPatchMath.Size];

        // 개발용: 특전별 적용 횟수와 마지막 바닐라 값 → 적용 값. 서버가 Dev 빌드일 때만 남긴다
        private static bool dev;
        private static readonly long[] Counts = new long[PerkPatchMath.Size];
        private static readonly float[] Before = new float[PerkPatchMath.Size];
        private static readonly float[] After = new float[PerkPatchMath.Size];

        public static bool Has(int perk) => scales[perk] > 0f;

        /// <summary>효과 비율 (단계 I 0.5, II 0.75, III 1). 꺼져 있으면 0.</summary>
        public static float Scale(int perk) => scales[perk];

        /// <summary>서버가 Dev 빌드인지 (마지막으로 받은 state 기준). 개발용 로그를 남길지 정한다.</summary>
        public static bool DevMode => dev;

        /// <summary>적용 단계(특전 → 1~3)와 단계 비율로 덮어쓴다.</summary>
        public static void Set(IDictionary<int, int> tiers, IList<double> ratios, bool devMode)
        {
            scales = PerkPatchMath.BuildScales(tiers, ratios, Disabled);
            dev = devMode;
            if (dev)
            {
                LogScales();
            }
        }

        private static string lastScales;

        // 개발용: 패치가 쓸 특전별 비율. 바뀌었을 때만 남긴다 (state 를 자주 받으므로)
        private static void LogScales()
        {
            var line = new StringBuilder();
            for (var perk = 0; perk < scales.Length; perk++)
            {
                if (scales[perk] > 0f)
                {
                    line.Append(line.Length > 0 ? ", " : "").Append(perk).Append(" x").Append(scales[perk].ToString("0.##"));
                }
            }

            var text = line.Length > 0 ? line.ToString() : "none";
            if (text != lastScales)
            {
                lastScales = text;
                Log.Info($"[dev] perk patch scales (perk xratio): {text}");
            }
        }

        /// <summary>패치에서 예외가 나면 그 특전만 이번 실행 동안 끈다. 경고는 한 번만.</summary>
        public static void Disable(int perk, Exception e)
        {
            if (Disabled[perk])
            {
                return;
            }

            Disabled[perk] = true;
            scales[perk] = 0f;
            Log.Warn($"perk {perk} disabled for this session after an error: {e}");
        }

        /// <summary>개발용: 적용 횟수와 마지막 값(바닐라 → 특전). 레이드 중에는 로그를 남기지 않고 배열에 적기만 한다.</summary>
        public static void Record(int perk, float before, float after)
        {
            if (dev)
            {
                Counts[perk]++;
                Before[perk] = before;
                After[perk] = after;
            }
        }

        /// <summary>개발용: 메인 메뉴에 돌아왔을 때 (레이드가 끝난 뒤) 지난번부터 센 횟수를 한 줄로 남기고 비운다.</summary>
        public static void FlushDevLog()
        {
            if (!dev)
            {
                return;
            }

            var line = new StringBuilder();
            for (var perk = 0; perk < Counts.Length; perk++)
            {
                if (Counts[perk] > 0)
                {
                    line.Append("\n  ").Append(perk).Append(": ").Append(Counts[perk]).Append("x, last ")
                        .Append(Before[perk].ToString("0.####")).Append(" -> ").Append(After[perk].ToString("0.####"));
                    if (Before[perk] != 0f)
                    {
                        line.Append(" (x").Append((After[perk] / Before[perk]).ToString("0.###")).Append(')');
                    }

                    Counts[perk] = 0;
                }
            }

            if (line.Length > 0)
            {
                Log.Info($"[dev] perk patches applied since last main menu (vanilla -> perk):{line}");
            }
        }
    }
}
