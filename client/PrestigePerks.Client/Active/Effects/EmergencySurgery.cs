using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 긴급 수술 (실현성조사 3.6 결정): 검게 파괴된 부위 1곳을 CMS 처럼 복구한다. 최대 체력은 restore% (수술 스킬 반영), 현재 체력 1.
    /// 순서 머리 → 흉부 → 복부 → 다리 → 팔 (좌우는 남은 최대 체력이 큰 쪽 먼저). 복부·팔·다리는 게임 RestoreBodyPart,
    /// 머리·흉부는 게임 함수가 거부하므로 같은 일을 직접 한다. 복구 뒤 팔·다리 상태(속도 제한, 팔 부상)를 다시 계산한다.
    /// </summary>
    internal sealed class EmergencySurgery : ActivePerkEffect
    {
        private static readonly EBodyPart[] All =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        public override bool HasTarget(ActivePerkUse use)
        {
            foreach (var part in All)
            {
                if (use.Health.IsBodyPartDestroyed(part))
                {
                    return true;
                }
            }

            return false;
        }

        public override void Start(ActivePerkUse use)
        {
            var health = use.Health;
            var part = Pick(health);
            var ratio = use.Value("restore") / 100f;
            var before = health.GetBodyPartHealth(part);
            var restored = part == EBodyPart.Head || part == EBodyPart.Chest ? RestoreVital(use, part, ratio) : health.RestoreBodyPart(part, ratio);
            use.Player.UpdateConditionsAfterBodyPartStateChanged(part);
            var after = health.GetBodyPartHealth(part);
            ActivePerkLog.Check("surgery.restored", restored && !health.IsBodyPartDestroyed(part),
                $"{part}: {before.Current:F0} / {before.Maximum:F0} -> {after.Current:F0} / {after.Maximum:F0} (restore {ratio:P0}, surgery skill {(float)health._skills.SurgeryReducePenalty:F2})");
        }

        // 머리 → 흉부 → 복부 → 다리 → 팔. 좌우는 남은 최대 체력이 큰 쪽
        private static EBodyPart Pick(ActiveHealthController health)
        {
            foreach (var part in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach })
            {
                if (health.IsBodyPartDestroyed(part))
                {
                    return part;
                }
            }

            var leg = Bigger(health, EBodyPart.LeftLeg, EBodyPart.RightLeg);
            return leg != EBodyPart.Common ? leg : Bigger(health, EBodyPart.LeftArm, EBodyPart.RightArm);
        }

        private static EBodyPart Bigger(ActiveHealthController health, EBodyPart left, EBodyPart right)
        {
            var l = health.IsBodyPartDestroyed(left);
            var r = health.IsBodyPartDestroyed(right);
            if (l && r)
            {
                return health.GetBodyPartHealth(left).Maximum >= health.GetBodyPartHealth(right).Maximum ? left : right;
            }

            return l ? left : r ? right : EBodyPart.Common;
        }

        // ActiveHealthController.RestoreBodyPart 와 같은 일 (머리·흉부 거부 조건만 뺐다). 복구 이벤트는 바깥에서 부를 수 없어 플레이어 처리 함수를 직접 부른다
        private static bool RestoreVital(ActivePerkUse use, EBodyPart part, float ratio)
        {
            var health = use.Health;
            var state = health.BodyState[part];
            if (!state.IsDestroyed)
            {
                return false;
            }

            var old = state.Health;
            state.IsDestroyed = false;
            ratio += (1f - ratio) * (float)health._skills.SurgeryReducePenalty;
            state.Health = new HealthValue(1f, Mathf.Max(1f, Mathf.Ceil(state.Health.Maximum * ratio)));
            health.NetworkSyncDestroyedBodyPart(part, EDamageType.Medicine);
            health.NetworkSyncBodyHealth(part);
            use.Player.HealthControllerOnBodyPartRestoredEvent(part, old.CurrentAndMaximum);
            return true;
        }
    }
}
