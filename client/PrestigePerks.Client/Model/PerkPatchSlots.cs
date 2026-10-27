using System.Collections.Generic;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 클라이언트 패치 특전의 ID → 칸 번호. 패치는 칸 번호로 고정 배열(<see cref="PerkPatchMath.BuildScales"/>)을 읽는다.
    /// 표 찾기는 state 를 받을 때(레이드 밖)만 하고, 패치 본문은 배열만 읽는다 (할당 없음).
    /// </summary>
    public static class PerkPatchSlots
    {
        public const int Porter = 0, Stamina = 1, Tough = 2, Control = 3, Maintenance = 4, Temperance = 5, Artisan = 6, Mastery = 7,
            Loading = 8, Search = 9, Surgeon = 10, Reload = 11, Handling = 12, Reliability = 13, Agility = 14;

        // 칸 번호 → 특전 ID. 위 상수와 같은 순서 (테스트가 확인)
        private static readonly string[] slots =
        {
            "porter", "stamina", "tough", "control", "maintenance", "temperance", "artisan", "mastery",
            "loading", "search", "surgeon", "reload", "handling", "reliability", "agility",
        };

        private static readonly Dictionary<string, int> ByIdMap = BuildIndex(slots);

        /// <summary>배열 크기 (가장 큰 칸 번호 + 1).</summary>
        public static int Count => slots.Length;

        /// <summary>칸 번호 → 특전 ID (개발용 로그, 테스트).</summary>
        public static IReadOnlyList<string> Ids => slots;

        /// <summary>패치 특전의 칸 번호. 패치 특전이 아니면 (Bonus 특전, 모르는 ID) -1.</summary>
        public static int Index(string id) => id != null && ByIdMap.TryGetValue(id, out var slot) ? slot : -1;

        private static Dictionary<string, int> BuildIndex(string[] ids)
        {
            var map = new Dictionary<string, int>();
            for (var slot = 0; slot < ids.Length; slot++)
            {
                if (ids[slot] != null)
                {
                    map[ids[slot]] = slot;
                }
            }

            return map;
        }
    }
}
