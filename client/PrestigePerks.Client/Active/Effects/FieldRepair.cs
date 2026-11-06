using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using PrestigePerks.Game;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 현장 수선 (실현성조사 3.10): 방탄복(방탄판 포함)·방탄 리그·헬멧의 ArmorComponent 내구도를 MaxDurability 의 repair% 만큼 올린다.
    /// 게임의 ArmorComponent.ApplyDurabilityDamage 를 거꾸로 하고, 끝에 RecalculateEquipmentParams 로 0 에서 살린 방탄판의 판정을 다시 켠다.
    /// 레이드 뒤 저장은 메인 메뉴에서 프로필 값과 비교한다 (<see cref="CheckSaved"/>, 살아 나왔을 때만 의미가 있다).
    /// </summary>
    internal sealed class FieldRepair : ActivePerkEffect
    {
        private static readonly EquipmentSlot[] Slots = { EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Headwear };

        // 아이템 ID → (수선 전, 수선 후). 메인 메뉴에서 한 번 비교하고 지운다
        private static readonly Dictionary<string, (float Before, float After)> repaired = new Dictionary<string, (float, float)>();

        // 대상 확인은 초당 4번이라 목록을 재사용한다
        private readonly List<ArmorComponent> armors = new List<ArmorComponent>();

        public override bool HasTarget(ActivePerkUse use)
        {
            Collect(use);
            foreach (var armor in armors)
            {
                if (Worn(armor.Repairable))
                {
                    return true;
                }
            }

            return false;
        }

        public override void Start(ActivePerkUse use)
        {
            Collect(use);
            var ratio = use.Value("repair") / 100f;
            var lines = new List<string>();
            var ok = true;
            foreach (var armor in armors)
            {
                var r = armor.Repairable;
                if (!Worn(r))
                {
                    continue;
                }

                var before = r.Durability;
                r.Durability = Math.Min(r.MaxDurability, r.Durability + r.MaxDurability * ratio);
                armor.Item.RaiseRefreshEvent(false, false);
                var want = Math.Min(r.MaxDurability, before + r.MaxDurability * ratio);
                ok &= Math.Abs(r.Durability - want) < 0.01f;
                repaired[armor.Item.Id] = (before, r.Durability);
                lines.Add($"{armor.Item.StringTemplateId} {armor.Item.Id} {before:F1} -> {r.Durability:F1} / {r.MaxDurability:F1}");
            }

            use.Player.RecalculateEquipmentParams();
            ActivePerkLog.Check("fieldrepair.durability", ok && lines.Count > 0, lines.Count == 0 ? "nothing repaired" : string.Join("; ", lines));
        }

        /// <summary>메인 메뉴: 수선한 방어구가 프로필에 저장됐는지 (수선 뒤 다시 맞았으면 낮을 수 있다. 수선 전보다 높으면 저장된 것으로 본다).</summary>
        public static void CheckSaved()
        {
            if (repaired.Count == 0)
            {
                return;
            }

            var equipment = PerkGame.Profile?.Inventory?.Equipment;
            var found = new List<ArmorComponent>();
            if (equipment != null)
            {
                foreach (var slot in Slots)
                {
                    equipment.GetSlot(slot).ContainedItem?.GetItemComponentsInChildrenNonAlloc(found);
                }
            }

            var armors = found.GroupBy(armor => armor.Item.Id).ToDictionary(group => group.Key, group => group.First());
            foreach (var pair in repaired)
            {
                if (!armors.TryGetValue(pair.Key, out var armor))
                {
                    ActivePerkLog.Event($"field repair: {pair.Key} not in the profile equipment (lost or died), not checked");
                    continue;
                }

                var saved = armor.Repairable.Durability;
                ActivePerkLog.Check("fieldrepair.saved", saved > pair.Value.Before + 0.01f || Math.Abs(saved - pair.Value.After) < 0.01f,
                    $"{pair.Key}: before {pair.Value.Before:F1}, after {pair.Value.After:F1}, profile {saved:F1}");
            }

            repaired.Clear();
        }

        private void Collect(ActivePerkUse use)
        {
            armors.Clear();
            var equipment = use.Player.Equipment;
            foreach (var slot in Slots)
            {
                equipment.GetSlot(slot).ContainedItem?.GetItemComponentsInChildrenNonAlloc(armors);
            }
        }

        private static bool Worn(RepairableComponent r) => r.Durability < r.MaxDurability - 0.01f;
    }
}
