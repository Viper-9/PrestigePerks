using System;
using System.Collections.Generic;
using System.Text;
using PrestigePerks.Model;

namespace PrestigePerks.Game
{
    /// <summary>
    /// 클라이언트 패치 특전의 활성 상태 (dev-docs 2_특전.md 6절 "패치 공통 규칙").
    /// state 를 받을 때마다 <see cref="Set"/>으로 덮어쓴다. 받지 못했으면 비어 있다 (= 바닐라).
    /// 패치 본문은 <see cref="Has"/>를 가장 먼저 본다 (배열 읽기만, 할당 없음).
    /// </summary>
    internal static class PerkEffects
    {
        private static bool[] active = new bool[PerkPatchMath.Size];
        private static readonly bool[] Disabled = new bool[PerkPatchMath.Size];

        // 개발용: 특전별 적용 횟수와 마지막 바닐라 값 → 적용 값. 서버가 Dev 빌드일 때만 남긴다
        private static bool dev;
        private static readonly long[] Counts = new long[PerkPatchMath.Size];
        private static readonly float[] Before = new float[PerkPatchMath.Size];
        private static readonly float[] After = new float[PerkPatchMath.Size];

        public static bool Has(int perk) => active[perk];

        public static void Set(IEnumerable<int> perks, bool devMode)
        {
            active = PerkPatchMath.BuildActive(perks, Disabled);
            dev = devMode;
        }

        /// <summary>패치에서 예외가 나면 그 특전만 이번 실행 동안 끈다. 경고는 한 번만.</summary>
        public static void Disable(int perk, Exception e)
        {
            if (Disabled[perk])
            {
                return;
            }

            Disabled[perk] = true;
            active[perk] = false;
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
