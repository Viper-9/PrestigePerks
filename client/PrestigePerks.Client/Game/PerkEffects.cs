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
        private static float[] scales = new float[PerkPatchSlots.Count];
        private static readonly bool[] Disabled = new bool[PerkPatchSlots.Count];

        // 개발용: 패치 칸별 적용 횟수와 마지막 바닐라 값 → 적용 값. 서버가 Dev 빌드일 때만 남긴다
        private static bool dev;
        private static readonly long[] Counts = new long[PerkPatchSlots.Count];
        private static readonly float[] Before = new float[PerkPatchSlots.Count];
        private static readonly float[] After = new float[PerkPatchSlots.Count];

        /// <summary>패치 칸(<see cref="PerkPatchSlots"/>)의 효과가 켜져 있는지.</summary>
        public static bool Has(int slot) => scales[slot] > 0f;

        /// <summary>효과 비율 (단계 I 0.5, II 0.75, III 1). 꺼져 있으면 0.</summary>
        public static float Scale(int slot) => scales[slot];

        /// <summary>서버가 Dev 빌드인지 (마지막으로 받은 state 기준). 개발용 로그를 남길지 정한다.</summary>
        public static bool DevMode => dev;

        /// <summary>적용 단계(특전 ID → 1~3)와 단계 비율로 덮어쓴다.</summary>
        public static void Set(IDictionary<string, int> tiers, IList<double> ratios, bool devMode)
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
            for (var slot = 0; slot < scales.Length; slot++)
            {
                if (scales[slot] > 0f)
                {
                    line.Append(line.Length > 0 ? ", " : "").Append(PerkPatchSlots.Ids[slot]).Append(" x").Append(scales[slot].ToString("0.##"));
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
        public static void Disable(int slot, Exception e)
        {
            if (Disabled[slot])
            {
                return;
            }

            Disabled[slot] = true;
            scales[slot] = 0f;
            Log.Warn($"perk {PerkPatchSlots.Ids[slot]} disabled for this session after an error: {e}");
        }

        /// <summary>개발용: 적용 횟수와 마지막 값(바닐라 → 특전). 레이드 중에는 로그를 남기지 않고 배열에 적기만 한다.</summary>
        public static void Record(int slot, float before, float after)
        {
            if (dev)
            {
                Counts[slot]++;
                Before[slot] = before;
                After[slot] = after;
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
            for (var slot = 0; slot < Counts.Length; slot++)
            {
                if (Counts[slot] > 0)
                {
                    line.Append("\n  ").Append(PerkPatchSlots.Ids[slot]).Append(": ").Append(Counts[slot]).Append("x, last ")
                        .Append(Before[slot].ToString("0.####")).Append(" -> ").Append(After[slot].ToString("0.####"));
                    if (Before[slot] != 0f)
                    {
                        line.Append(" (x").Append((After[slot] / Before[slot]).ToString("0.###")).Append(')');
                    }

                    Counts[slot] = 0;
                }
            }

            if (line.Length > 0)
            {
                Log.Info($"[dev] perk patches applied since last main menu (vanilla -> perk):{line}");
            }
        }
    }
}
