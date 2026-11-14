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

        // 개발용: 패치 칸별 적용 횟수와 마지막 바닐라 값 → 적용 값, config 수치로 계산한 기대값과 맞은·틀린 횟수. F12 개발자 모드일 때만 남긴다
        private static bool dev;
        private static long[] counts = new long[PerkPatchSlots.Count];
        private static float[] before = new float[PerkPatchSlots.Count];
        private static float[] after = new float[PerkPatchSlots.Count];
        private static float[] want = new float[PerkPatchSlots.Count];
        private static float[] checkedBefore = new float[PerkPatchSlots.Count];
        private static float[] checkedAfter = new float[PerkPatchSlots.Count];
        private static long[] ok = new long[PerkPatchSlots.Count];
        private static long[] fail = new long[PerkPatchSlots.Count];
        private static long[] unchecked_ = new long[PerkPatchSlots.Count];

        // 칸별 적용 단계와 그 단계의 화면 수치 (서버 catalog tiers, 예: 반동 −40). 꺼진 칸은 0 / NaN
        private static int[] slotTiers = new int[PerkPatchSlots.Count];
        private static double[] tierValues = new double[PerkPatchSlots.Count];

        /// <summary>패치 칸(<see cref="PerkPatchSlots"/>)의 효과가 켜져 있는지. 모르는 칸이면 false.</summary>
        public static bool Has(int slot)
        {
            var current = scales;
            return (uint)slot < (uint)current.Length && current[slot] > 0f;
        }

        /// <summary>효과 비율 (그 단계 효과량 ÷ III, 기본 I 0.5, II 0.75, III 1). 꺼져 있거나 모르는 칸이면 0.</summary>
        public static float Scale(int slot)
        {
            var current = scales;
            return (uint)slot < (uint)current.Length ? current[slot] : 0f;
        }

        /// <summary>F12 개발자 모드 (<see cref="Plugin.DevMode"/>). 개발용 로그를 남길지 정한다.</summary>
        public static bool DevMode
        {
            get => dev;
            set => dev = value;
        }

        /// <summary>적용 단계(특전 ID → 1~3)와 서버 catalog(단계별 수치)로 덮어쓴다. 패치 계수(<see cref="PerkPatchMath.Configure"/>)도 catalog 로 정한다.</summary>
        public static void Set(IDictionary<string, int> tiers, IList<PerkCatalogEntryDto> catalog)
        {
            PerkPatchMath.Configure(catalog);
            LogFactors();
            // add-on 이 칸을 등록했으면 배열을 늘린다 (레이드 밖)
            var count = PerkPatchSlots.Count;
            if (disabled.Length < count)
            {
                Array.Resize(ref disabled, count);
                Array.Resize(ref counts, count);
                Array.Resize(ref before, count);
                Array.Resize(ref after, count);
                Array.Resize(ref want, count);
                Array.Resize(ref checkedBefore, count);
                Array.Resize(ref checkedAfter, count);
                Array.Resize(ref ok, count);
                Array.Resize(ref fail, count);
                Array.Resize(ref unchecked_, count);
            }

            SetTierValues(tiers, catalog, count);
            scales = PerkPatchMath.BuildScales(tiers, catalog, disabled);
            if (dev)
            {
                LogScales();
            }
        }

        private static string lastScales;
        private static string lastFactors;

        // 판정용: 칸마다 적용 단계와 그 단계 수치 (state 를 받을 때, 레이드 밖)
        private static void SetTierValues(IDictionary<string, int> tiers, IList<PerkCatalogEntryDto> catalog, int count)
        {
            var newTiers = new int[count];
            var values = new double[count];
            for (var slot = 0; slot < count; slot++)
            {
                values[slot] = double.NaN;
                var id = PerkPatchSlots.Ids[slot];
                if (tiers == null || !tiers.TryGetValue(id, out var tier) || catalog == null)
                {
                    continue;
                }

                foreach (var entry in catalog)
                {
                    if (entry != null && entry.Id == id)
                    {
                        newTiers[slot] = tier;
                        values[slot] = entry.Amount * PerkAmounts.Ratio(entry, tier);
                        break;
                    }
                }
            }

            slotTiers = newTiers;
            tierValues = values;
        }

        // 패치 계수 (단계 III, 서버 config perkAmounts). 바뀌었을 때만 남긴다 (서버 수치가 들어왔는지 로그로 확인하려고 개발자 모드가 아니어도 남긴다)
        private static void LogFactors()
        {
            var text = PerkPatchMath.Describe();
            if (text != lastFactors)
            {
                lastFactors = text;
                Log.Info($"perk patch factors (tier III): {text}");
            }
        }

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

        /// <summary>개발용: 적용 횟수와 마지막 값(바닐라 → 특전). 자동 판정 없이 센다. 레이드 중에는 로그를 남기지 않고 배열에 적기만 한다.</summary>
        public static void Record(int slot, float before, float after) => Record(slot, before, after, PerkCheckForm.None);

        /// <summary>
        /// 개발용: 적용 횟수와 마지막 값, 그리고 config 수치로 계산한 기대값과 맞는지 (<see cref="PerkPatchCheck"/>). form 은 이 패치가 값을 바꾸는 방식,
        /// extra 는 방식에 필요한 값 (스태미나 기본 최대치, 수술 속도 스킬). 레이드 중에는 배열에 적기만 한다 (할당 없음).
        /// </summary>
        public static void Record(int slot, float before, float after, PerkCheckForm form, float extra = 1f)
        {
            if (!dev || (uint)slot >= (uint)counts.Length)
            {
                return;
            }

            counts[slot]++;
            PerkEffects.before[slot] = before;
            PerkEffects.after[slot] = after;
            var values = tierValues;
            var value = slot < values.Length ? values[slot] : double.NaN;
            if (form == PerkCheckForm.None || double.IsNaN(value))
            {
                unchecked_[slot]++;
                return;
            }

            var expected = PerkPatchCheck.Expected(form, before, value, extra);
            want[slot] = expected;
            checkedBefore[slot] = before;
            checkedAfter[slot] = after;
            if (PerkPatchCheck.Matches(after, expected))
            {
                ok[slot]++;
            }
            else
            {
                fail[slot]++;
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
            int okPerks = 0, failPerks = 0, uncheckedPerks = 0;
            for (var slot = 0; slot < counts.Length; slot++)
            {
                if (counts[slot] == 0)
                {
                    continue;
                }

                line.Append("\n  ").Append(PerkPatchSlots.Ids[slot]).Append(": ").Append(counts[slot]).Append("x, last ")
                    .Append(before[slot].ToString("0.####")).Append(" -> ").Append(after[slot].ToString("0.####"));
                if (before[slot] != 0f)
                {
                    line.Append(" (x").Append((after[slot] / before[slot]).ToString("0.###")).Append(')');
                }

                // [perk][CHECK]: config 의 그 단계 수치로 계산한 값과 패치가 만든 값이 같은지 (게임 화면 없이 로그만으로 판정)
                var config = $"T{(slot < slotTiers.Length ? slotTiers[slot] : 0)} config {Signed(slot < tierValues.Length ? tierValues[slot] : double.NaN)}";
                if (ok[slot] + fail[slot] > 0)
                {
                    var passed = fail[slot] == 0;
                    var detail = $"[perk][CHECK] {(passed ? "OK  " : "FAIL")} {PerkPatchSlots.Ids[slot]} {config}: {ok[slot]} ok / {fail[slot]} fail of {counts[slot]}, "
                        + $"last {checkedBefore[slot]:0.####} -> {checkedAfter[slot]:0.####} (want {want[slot]:0.####})"
                        + (unchecked_[slot] > 0 ? $", {unchecked_[slot]} more without auto check" : "");
                    if (passed)
                    {
                        okPerks++;
                        Log.Info(detail);
                    }
                    else
                    {
                        failPerks++;
                        Log.Warn(detail);
                    }
                }
                else
                {
                    uncheckedPerks++;
                    Log.Info($"[perk][CHECK] SKIP {PerkPatchSlots.Ids[slot]} {config}: {counts[slot]}x without auto check, last {before[slot]:0.####} -> {after[slot]:0.####}");
                }

                counts[slot] = 0;
                ok[slot] = 0;
                fail[slot] = 0;
                unchecked_[slot] = 0;
            }

            if (line.Length > 0)
            {
                Log.Info($"[dev] perk patches applied since last main menu (vanilla -> perk):{line}");
                Log.Info($"[perk][SUMMARY] patch checks: ok {okPerks} / fail {failPerks} / skip {uncheckedPerks} perks");
            }
        }

        private static string Signed(double value) =>
            double.IsNaN(value) ? "?" : (value >= 0 ? "+" : "") + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
