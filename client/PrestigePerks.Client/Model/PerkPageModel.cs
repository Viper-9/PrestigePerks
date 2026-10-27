// 클라이언트는 nullable 을 끄고 빌드한다 (빈 칸·없는 특전 = null). 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

using System;
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
        /// <summary>리롤할 칸이 없다 (열린 칸이 모두 잠금 또는 확정, 빈 칸도 없음).</summary>
        NothingToRoll,
        /// <summary>뽑을 미보유 특전이 없다 (도감 완성).</summary>
        CollectionComplete,
        NotEnoughCoins,
    }

    /// <summary>슬롯 칸 종류 (dev-docs 2_특전.md 1절 "칸 종류").</summary>
    public enum SlotKind
    {
        /// <summary>아직 닫힌 칸.</summary>
        Closed,
        /// <summary>리롤 칸: 다음 리롤 때 다시 뽑는다.</summary>
        Roll,
        /// <summary>빈 칸: 다음 리롤 때 채운다.</summary>
        Empty,
        Locked,
        /// <summary>확정 칸: 보유한 특전.</summary>
        Fixed,
    }

    /// <summary>"선택한 특전" 칸의 상태 (기획 README "선택한 특전 칸" 표).</summary>
    public enum SelectionKind
    {
        None,
        /// <summary>리롤 칸의 미보유 특전.</summary>
        RollSlot,
        /// <summary>잠긴 칸의 미보유 특전.</summary>
        LockedSlot,
        /// <summary>확정 칸의 보유 특전.</summary>
        Fixed,
        /// <summary>장착 안 된 보유 특전.</summary>
        Owned,
        /// <summary>리롤로 나오지 않은 미보유 특전.</summary>
        NotShown,
    }

    /// <summary>구매 버튼을 끄는 이유.</summary>
    public enum BuyBlock
    {
        None,
        /// <summary>다음 단계가 단계 상한보다 높다 ("Available at Pn").</summary>
        Capped,
        NotEnoughCoins,
    }

    /// <summary>선택한 특전 칸에 보일 내용.</summary>
    public sealed class Selection
    {
        public SelectionKind Kind;

        /// <summary>특전 ID. 없으면 null.</summary>
        public string Perk;

        /// <summary>들어 있는 슬롯 (0 부터). 없으면 -1.</summary>
        public int Slot = -1;

        /// <summary>지금 보여 줄 단계 (적용 중이면 적용 단계, 장착 안 된 보유 특전은 보유 단계, 미보유는 I).</summary>
        public int Tier = 1;

        /// <summary>살 수 있는 단계 (1 = 보유하기). 없으면 0.</summary>
        public int NextTier;

        public int Price;
        public BuyBlock Block;

        /// <summary><see cref="BuyBlock.Capped"/> 일 때 다음 단계가 열리는 프레스티지. 모르면 null.</summary>
        public int? OpensAt;
    }

    /// <summary>
    /// 특전 페이지의 표시 상태 계산. 규칙(비용·횟수)은 다시 계산하지 않고 서버 응답을 그대로 쓴다 (dev-docs 2_특전.md 6절).
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkPageModel
    {
        public const int MaxSlots = 6, MaxTier = 3;

        /// <summary>서버 config 를 받지 못했을 때 쓰는 단계 가격 기본값 (I, II, III).</summary>
        public static readonly int[] DefaultPrices = { 40, 100, 200 };

        /// <summary>목록에 보여 줄 특전 (서버 카탈로그 순서). state 를 받기 전에는 비어 있다.</summary>
        public static IReadOnlyList<string> Perks(PerkStateDto state) => state?.CatalogIds ?? (IReadOnlyList<string>)new string[0];

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

            if (!state.Slots.Any(Rollable))
            {
                return RollBlock.NothingToRoll;
            }

            if (RollPool(state).Count == 0)
            {
                return RollBlock.CollectionComplete;
            }

            return state.NextRerollCost > inventoryCoins ? RollBlock.NotEnoughCoins : RollBlock.None;
        }

        /// <summary>리롤에서 돌아갈 슬롯 (리롤 칸·빈 칸 번호, 0 부터).</summary>
        public static List<int> SpinTargets(PerkStateDto state) =>
            state == null
                ? new List<int>()
                : state.Slots.Select((slot, index) => new { slot, index }).Where(x => Rollable(x.slot)).Select(x => x.index).ToList();

        /// <summary>리롤 후보: 미보유 특전 중 잠긴 칸·확정 칸에 없는 것 (서버 PerkRules 와 같은 규칙). 릴 띠에도 쓴다.</summary>
        public static List<string> RollPool(PerkStateDto state)
        {
            if (state == null)
            {
                return new List<string>();
            }

            var kept = new HashSet<string>(state.Slots.Where(slot => !Rollable(slot) && !slot.IsEmpty).Select(slot => slot.Perk));
            return Perks(state).Where(perk => !kept.Contains(perk) && OwnedTier(state, perk) == 0).ToList();
        }

        public static int LockedCount(PerkStateDto state) => state?.Slots.Count(slot => slot.Locked) ?? 0;

        public static int FixedCount(PerkStateDto state) => state?.Slots.Count(slot => slot.Fixed) ?? 0;

        public static int OwnedCount(PerkStateDto state) => state?.Owned.Count(pair => pair.Value > 0) ?? 0;

        /// <summary>슬롯 칸 상태: 열림(보유 특전), 아직 닫힘(다음 단계에서 열림).</summary>
        public static bool IsSlotOpen(PerkStateDto state, int index) => state != null && index >= 0 && index < state.Slots.Count;

        public static SlotKind KindOf(PerkStateDto state, int index)
        {
            if (!IsSlotOpen(state, index))
            {
                return SlotKind.Closed;
            }

            var slot = state.Slots[index];
            return slot.Fixed ? SlotKind.Fixed : slot.Locked ? SlotKind.Locked : slot.IsEmpty ? SlotKind.Empty : SlotKind.Roll;
        }

        /// <summary>닫힌 칸이 열리는 단계 (서버 config 의 단계별 슬롯 수로 정한다). 더 높은 단계에서도 열리지 않으면 null.</summary>
        public static int? OpensAt(PerkStateDto state, int index) =>
            state?.SlotOpensAt != null && index >= 0 && index < state.SlotOpensAt.Count ? state.SlotOpensAt[index] : null;

        /// <summary>특전 ID → 들어 있는 슬롯 번호 (0 부터). 없으면 -1. 빈 칸(null)은 찾지 않는다.</summary>
        public static int SlotOf(PerkStateDto state, string perk)
        {
            if (state == null || perk == null)
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

        /// <summary>슬롯에 든 특전 (0 부터). 닫힌 칸·빈 칸이면 null.</summary>
        public static string PerkInSlot(PerkStateDto state, int index) =>
            IsSlotOpen(state, index) && !state.Slots[index].IsEmpty ? state.Slots[index].Perk : null;

        /// <summary>특전이 든 첫 열린 슬롯의 특전 (페이지를 열 때 자동 선택). 없으면 null.</summary>
        public static string FirstSlotPerk(PerkStateDto state) =>
            state == null ? null : Enumerable.Range(0, state.Slots.Count).Select(index => PerkInSlot(state, index)).FirstOrDefault(perk => perk != null);

        /// <summary>보유한 단계. 없으면 0.</summary>
        public static int OwnedTier(PerkStateDto state, string perk) =>
            perk != null && state?.Owned != null && state.Owned.TryGetValue(perk, out var tier) ? Math.Max(0, Math.Min(tier, MaxTier)) : 0;

        public static int TierCap(PerkStateDto state) => Math.Max(1, Math.Min(state?.TierCap ?? 1, MaxTier));

        /// <summary>
        /// 화면에 보일 단계: 칸에 있으면 서버가 정한 적용 단계, 장착 안 된 보유 특전은 보유 단계(상한 이하), 미보유는 I.
        /// </summary>
        public static int ShownTier(PerkStateDto state, string perk)
        {
            if (perk != null && SlotOf(state, perk) >= 0 && state.ActiveTiers != null && state.ActiveTiers.TryGetValue(perk, out var active))
            {
                return Math.Max(1, Math.Min(active, MaxTier));
            }

            return Math.Max(1, Math.Min(OwnedTier(state, perk), TierCap(state)));
        }

        /// <summary>지금 효과가 적용되는 단계 (칸에 있으면). 아니면 0.</summary>
        public static int AppliedTier(PerkStateDto state, string perk) => SlotOf(state, perk) >= 0 ? ShownTier(state, perk) : 0;

        /// <summary>단계 가격. 서버 값이 없으면 기본값.</summary>
        public static int Price(PerkStateDto state, int tier)
        {
            var index = Math.Max(1, Math.Min(tier, MaxTier)) - 1;
            return state?.TierPrices != null && state.TierPrices.Count > index ? state.TierPrices[index] : DefaultPrices[index];
        }

        /// <summary>효과 수치 문구 (그 단계 값). pointUnit 은 더해지는 특전의 단위 (<see cref="PerkAmounts.Format"/>).</summary>
        public static string EffectValue(PerkStateDto state, string perk, int tier, string pointUnit = "%") =>
            PerkAmounts.Format(state?.Info(perk), PerkAmounts.Ratio(state?.TierEffectRatio, tier), pointUnit);

        /// <summary>목록에서 끌 수 있는 특전: 장착 안 된 보유 특전.</summary>
        public static bool CanDragRow(PerkStateDto state, string perk) => OwnedTier(state, perk) > 0 && SlotOf(state, perk) < 0;

        /// <summary>슬롯에서 끌 수 있는 칸: 확정 칸 (목록에 놓으면 빠진다).</summary>
        public static bool CanDragSlot(PerkStateDto state, int index) => KindOf(state, index) == SlotKind.Fixed;

        /// <summary>보유 특전을 놓을 수 있는 칸: 리롤 칸·빈 칸·확정 칸 (잠긴 칸·닫힌 칸은 안 됨).</summary>
        public static bool CanDrop(PerkStateDto state, int index)
        {
            var kind = KindOf(state, index);
            return kind == SlotKind.Roll || kind == SlotKind.Empty || kind == SlotKind.Fixed;
        }

        /// <summary>선택한 특전 칸에 보일 내용. 구매 버튼의 단계·가격·막는 이유까지 정한다.</summary>
        public static Selection Select(PerkStateDto state, string perk, int inventoryCoins)
        {
            var selection = new Selection { Perk = perk };
            if (state == null || state.Info(perk) == null)
            {
                selection.Kind = SelectionKind.None;
                return selection;
            }

            var owned = OwnedTier(state, perk);
            selection.Slot = SlotOf(state, perk);
            selection.Tier = ShownTier(state, perk);
            if (selection.Slot >= 0)
            {
                var slot = state.Slots[selection.Slot];
                selection.Kind = slot.Fixed || owned > 0 ? SelectionKind.Fixed : slot.Locked ? SelectionKind.LockedSlot : SelectionKind.RollSlot;
            }
            else
            {
                selection.Kind = owned > 0 ? SelectionKind.Owned : SelectionKind.NotShown;
            }

            selection.NextTier = selection.Kind == SelectionKind.NotShown ? 0 : owned < MaxTier ? owned + 1 : 0;
            if (selection.NextTier == 0)
            {
                return selection;
            }

            selection.Price = Price(state, selection.NextTier);
            if (selection.NextTier > TierCap(state))
            {
                selection.Block = BuyBlock.Capped;
                var opens = selection.NextTier - 2;
                selection.OpensAt = state.TierOpensAt != null && opens >= 0 && opens < state.TierOpensAt.Count ? state.TierOpensAt[opens] : null;
            }
            else if (selection.Price > inventoryCoins)
            {
                selection.Block = BuyBlock.NotEnoughCoins;
            }

            return selection;
        }

        private static bool Rollable(PerkSlotDto slot) => !slot.Locked && !slot.Fixed;
    }
}
