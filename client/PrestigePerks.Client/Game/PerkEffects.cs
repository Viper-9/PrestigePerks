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
    /// 배열은 add-on 이 등록한 칸(<see cref="PerkPatchSlots.Register"/>)까지 <see cref="Set"/> 때 늘린다. 범위 밖 칸은 꺼진 것으로 본다.
    /// </summary>
    internal static class PerkEffects
    {
        private static float[] scales = new float[PerkPatchSlots.Count];
        private static bool[] disabled = new bool[PerkPatchSlots.Count];

        // 개발용: 패치 칸별 적용 횟수와 마지막 바닐라 값 → 적용 값. 서버가 Dev 빌드일 때만 남긴다
        private static bool dev;
        private static long[] counts = new long[PerkPatchSlots.Count];
        private static float[] before = new float[PerkPatchSlots.Count];
        private static float[] after = new float[PerkPatchSlots.Count];

        /// <summary>패치 칸(<see cref="PerkPatchSlots"/>)의 효과가 켜져 있는지. 모르는 칸이면 false.</summary>
        public static bool Has(int slot)
        {
            var current = scales;
            return (uint)slot < (uint)current.Length && current[slot] > 0f;
        }

        /// <summary>효과 비율 (단계 I 0.5, II 0.75, III 1). 꺼져 있거나 모르는 칸이면 0.</summary>
        public static float Scale(int slot)
        {
            var current = scales;
            return (uint)slot < (uint)current.Length ? current[slot] : 0f;
        }

        /// <summary>서버가 Dev 빌드인지 (마지막으로 받은 state 기준). 개발용 로그를 남길지 정한다.</summary>
        public static bool DevMode => dev;

        /// <summary>적용 단계(특전 ID → 1~3)와 단계 비율로 덮어쓴다.</summary>
        public static void Set(IDictionary<string, int> tiers, IList<double> ratios, bool devMode)
        {
            // add-on 이 칸을 등록했으면 배열을 늘린다 (레이드 밖)
            var count = PerkPatchSlots.Count;
            if (disabled.Length < count)
            {
                Array.Resize(ref disabled, count);
                Array.Resize(ref counts, count);
                Array.Resize(ref before, count);
                Array.Resize(ref after, count);
            }

            scales = PerkPatchMath.BuildScales(tiers, ratios, disabled);
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
            if ((uint)slot >= (uint)disabled.Length || disabled[slot])
            {
                return;
            }

            disabled[slot] = true;
            if (slot < scales.Length)
            {
                scales[slot] = 0f;
            }

            Log.Warn($"perk {PerkPatchSlots.Ids[slot]} disabled for this session after an error: {e}");
        }

        /// <summary>개발용: 적용 횟수와 마지막 값(바닐라 → 특전). 레이드 중에는 로그를 남기지 않고 배열에 적기만 한다.</summary>
        public static void Record(int slot, float before, float after)
        {
            if (dev && (uint)slot < (uint)counts.Length)
            {
                counts[slot]++;
                PerkEffects.before[slot] = before;
                PerkEffects.after[slot] = after;
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
            for (var slot = 0; slot < counts.Length; slot++)
            {
                if (counts[slot] > 0)
                {
                    line.Append("\n  ").Append(PerkPatchSlots.Ids[slot]).Append(": ").Append(counts[slot]).Append("x, last ")
                        .Append(before[slot].ToString("0.####")).Append(" -> ").Append(after[slot].ToString("0.####"));
                    if (before[slot] != 0f)
                    {
                        line.Append(" (x").Append((after[slot] / before[slot]).ToString("0.###")).Append(')');
                    }

                    counts[slot] = 0;
                }
            }

            if (line.Length > 0)
            {
                Log.Info($"[dev] perk patches applied since last main menu (vanilla -> perk):{line}");
            }
        }
    }
}
