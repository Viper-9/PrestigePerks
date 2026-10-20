using System.Collections.Generic;
using System.Linq;

namespace PrestigePerks.Model
{
    /// <summary>리롤 버튼을 막는 이유.</summary>
    public enum RollBlock
    {
        None,
        /// <summary>요청 중이거나 릴이 도는 중.</summary>
        Busy,
        /// <summary>프레스티지 0 (슬롯 없음).</summary>
        NoSlots,
        AllLocked,
        NotEnoughCoins,
    }

    /// <summary>
    /// 특전 페이지의 표시 상태 계산. 규칙(비용·횟수)은 다시 계산하지 않고 서버 응답을 그대로 쓴다 (dev-docs 2_특전.md 6절).
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkPageModel
    {
        public const int MaxSlots = 6;

        /// <summary>목록에 보여 줄 특전 번호 (server PerkCatalog.Numbers 와 같아야 한다. 테스트가 확인).</summary>
        public static readonly int[] AllPerks = { 1, 2, 3, 4, 8, 9, 10, 11, 14, 15, 16, 17, 24, 25, 26, 27, 29, 30, 31, 32 };

        public static RollBlock CheckRoll(PerkStateDto state, int inventoryCoins, bool busy)
        {
            if (busy)
            {
                return RollBlock.Busy;
            }

            if (state == null || state.Slots.Count == 0)
            {
                return RollBlock.NoSlots;
            }

            if (state.Slots.All(slot => slot.Locked))
            {
                return RollBlock.AllLocked;
            }

            return state.NextRerollCost > inventoryCoins ? RollBlock.NotEnoughCoins : RollBlock.None;
        }

        /// <summary>리롤에서 돌아갈 슬롯 (잠그지 않은 슬롯 번호, 0 부터).</summary>
        public static List<int> SpinTargets(PerkStateDto state) =>
            state == null
                ? new List<int>()
                : state.Slots.Select((slot, index) => new { slot, index }).Where(x => !x.slot.Locked).Select(x => x.index).ToList();

        public static int LockedCount(PerkStateDto state) => state?.Slots.Count(slot => slot.Locked) ?? 0;

        /// <summary>슬롯 칸 상태: 열림(보유 특전), 아직 닫힘(다음 단계에서 열림).</summary>
        public static bool IsSlotOpen(PerkStateDto state, int index) => state != null && index >= 0 && index < state.Slots.Count;

        /// <summary>닫힌 칸이 열리는 단계 (서버 config 의 단계별 슬롯 수로 정한다). 더 높은 단계에서도 열리지 않으면 null.</summary>
        public static int? OpensAt(PerkStateDto state, int index) =>
            state?.SlotOpensAt != null && index >= 0 && index < state.SlotOpensAt.Count ? state.SlotOpensAt[index] : null;
        /// <summary>특전 번호 → 들어 있는 슬롯 번호 (0 부터). 없으면 -1.</summary>
        public static int SlotOf(PerkStateDto state, int perk)
        {
            if (state == null)
            {
                return -1;
            }

            for (var i = 0; i < state.Slots.Count; i++)
            {
                if (state.Slots[i].Perk == perk)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
