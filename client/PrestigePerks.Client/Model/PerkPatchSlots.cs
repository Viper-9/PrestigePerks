using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 클라이언트 패치 특전의 ID → 칸 번호. 패치는 칸 번호로 고정 배열(<see cref="PerkPatchMath.BuildScales"/>)을 읽는다.
    /// 표 찾기는 state 를 받을 때(레이드 밖)만 하고, 패치 본문은 배열만 읽는다 (할당 없음).
    /// 내장 15칸 뒤로 클라이언트 add-on(B형)이 <see cref="Register"/> 로 칸을 이어 붙인다 (dev-docs 95 Task 5).
    /// </summary>
    public static class PerkPatchSlots
    {
        public const int Porter = 0, Stamina = 1, Tough = 2, Control = 3, Maintenance = 4, Temperance = 5, Artisan = 6, Mastery = 7,
            Loading = 8, Search = 9, Surgeon = 10, Reload = 11, Handling = 12, Reliability = 13, Agility = 14;

        /// <summary>내장 패치 특전 칸 수. add-on 칸은 이 번호부터.</summary>
        public const int BuiltinCount = 15;

        /// <summary>add-on 특전 ID 최대 길이 (server PerkAddonFile.MaxIdLength).</summary>
        public const int MaxIdLength = 64;

        // 칸 번호 → 특전 ID. 위 상수와 같은 순서 (테스트가 확인)
        private static readonly string[] builtin =
        {
            "porter", "stamina", "tough", "control", "maintenance", "temperance", "artisan", "mastery",
            "loading", "search", "surgeon", "reload", "handling", "reliability", "agility",
        };

        // Register 는 플러그인 Awake 마다 (다른 플러그인이 다른 시점에) 부를 수 있어 잠근다
        private static readonly object gate = new object();
        private static readonly List<string> slots = new List<string>(builtin);
        private static readonly ReadOnlyCollection<string> ids = slots.AsReadOnly();
        private static readonly Dictionary<string, int> ByIdMap = BuildIndex(slots);

        /// <summary>배열 크기 (가장 큰 칸 번호 + 1). 내장 15 + 등록한 add-on.</summary>
        public static int Count
        {
            get
            {
                lock (gate)
                {
                    return slots.Count;
                }
            }
        }

        /// <summary>칸 번호 → 특전 ID (개발용 로그, 테스트). 읽기 전용 보기다.</summary>
        public static IReadOnlyList<string> Ids => ids;

        /// <summary>패치 특전의 칸 번호. 패치 특전이 아니면 (Bonus 특전, 모르는 ID, 등록 안 된 add-on) -1.</summary>
        public static int Index(string id)
        {
            if (id == null)
            {
                return -1;
            }

            lock (gate)
            {
                return ByIdMap.TryGetValue(id, out var slot) ? slot : -1;
            }
        }

        /// <summary>
        /// add-on 패치 특전의 칸을 받는다 (내장 15칸 뒤로 이어 붙인다). ID 형식이 틀리거나(점 없음·대문자), 내장 ID 이거나, 이미 등록됐으면 -1.
        /// </summary>
        public static int Register(string id)
        {
            if (!IsAddonId(id))
            {
                return -1;
            }

            lock (gate)
            {
                if (ByIdMap.ContainsKey(id))
                {
                    return -1;
                }

                slots.Add(id);
                ByIdMap[id] = slots.Count - 1;
                return slots.Count - 1;
            }
        }

        /// <summary>add-on 이 등록한 칸이 있는 특전인가 (내장은 false).</summary>
        public static bool IsRegistered(string id) => Index(id) >= BuiltinCount;

        /// <summary>
        /// add-on 특전 ID 형식: 점이 든 소문자 "author.perk" (server PerkAddonFile.IsValidId 와 같은 규칙, 테스트가 같은 입력표로 확인).
        /// 앞 부분은 [a-z0-9_-]+, 점 뒤는 [a-z0-9_.-]+ 이고 점으로 끝나지 않는다.
        /// </summary>
        public static bool IsAddonId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id[id.Length - 1] == '.')
            {
                return false;
            }

            var dot = id.IndexOf('.');
            if (dot <= 0 || dot == id.Length - 1)
            {
                return false;
            }

            // 첫 점 앞에는 점이 없으므로 점은 어디든 받으면 된다
            foreach (var c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>테스트용: 등록한 add-on 칸을 지운다.</summary>
        internal static void ResetForTests()
        {
            lock (gate)
            {
                slots.RemoveRange(BuiltinCount, slots.Count - BuiltinCount);
                ByIdMap.Clear();
                foreach (var pair in BuildIndex(slots))
                {
                    ByIdMap[pair.Key] = pair.Value;
                }
            }
        }

        private static Dictionary<string, int> BuildIndex(IReadOnlyList<string> ids)
        {
            var map = new Dictionary<string, int>();
            for (var slot = 0; slot < ids.Count; slot++)
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
