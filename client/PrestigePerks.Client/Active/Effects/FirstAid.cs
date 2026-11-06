using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 응급 처치 (실현성조사 3.1): 모든 출혈을 끝내고, 서버가 globals 에 넣은 자극제 버프 PrestigePerks_FirstAid_{단계} 를 아이템 없이 건다
    /// (DoExternalBuff, 겨울 이벤트 동상과 같은 방식). 버프 = 체력 재생 + 새 출혈 막기, 같은 시간 (server ActivePerkBuffs). 끝은 게임이 처리한다.
    /// </summary>
    internal sealed class FirstAid : ActivePerkEffect
    {
        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        /// <summary>server ActivePerkBuffs.FirstAidBuffName 과 같은 이름.</summary>
        public static string BuffName(int tier) => $"PrestigePerks_FirstAid_{tier}";

        public override void RaidStarted(ActivePerkUse use)
        {
            var buffs = Singleton<GlobalConfiguration>.Instance.Health.Effects.Stimulator.Buffs;
            var missing = Enumerable.Range(1, 3).Select(BuffName).Where(name => !buffs.ContainsKey(name)).ToList();
            ActivePerkLog.Check("firstaid.buffDefined", missing.Count == 0, missing.Count == 0 ? "globals has the 3 First Aid buffs" : $"missing in globals: {string.Join(", ", missing)}");
        }

        // 새 출혈 막기 확인: 버프가 켜질 때까지 기다린 뒤 초당 4번 본다
        private const float BlockCheckDelay = 1.5f, BlockCheckInterval = 0.25f;

        private float startedAt, nextBlockCheck;
        private int blockSamples, blockerOff, bleedingSeen;
        private bool wasBleeding;

        public override bool HasTarget(ActivePerkUse use) => Bleeding(use.Health) || Damaged(use.Health);

        public override void Start(ActivePerkUse use)
        {
            var health = use.Health;
            var bleedingBefore = Bleeding(health);
            var damagedBefore = Damaged(health);
            var stimsBefore = Stimulators(health);
            var healthBefore = HealthSum(health);
            health.RemoveBleedingEffects(EBodyPart.Common);
            health.DoExternalBuff(BuffName(use.Tier));
            ActivePerkLog.Event($"first aid: bleeding {bleedingBefore}, damaged {damagedBefore}, buff {BuffName(use.Tier)}");
            startedAt = Time.time;
            nextBlockCheck = startedAt + BlockCheckDelay;
            blockSamples = blockerOff = bleedingSeen = 0;
            wasBleeding = false;

            // 출혈은 ForceResidue 로 끝나는 단계로 가고, 버프는 다음 갱신에 켜지므로 잠시 뒤 확인한다
            use.Later(1f, () =>
            {
                ActivePerkLog.Check("firstaid.bleeding", !Bleeding(health), $"bleeding before {bleedingBefore}, 1 s later {Bleeding(health)}");
                var stims = Stimulators(health);
                ActivePerkLog.Check("firstaid.buff", stims == stimsBefore + 1, $"active stimulator effects {stimsBefore} -> {stims}");
            });
            use.Later(10f, () =>
            {
                var after = HealthSum(health);
                if (!damagedBefore)
                {
                    ActivePerkLog.Skip("firstaid.regen", $"no damaged body part at the press (health sum {healthBefore:F0})");
                    return;
                }

                ActivePerkLog.Check("firstaid.regen", after > healthBefore + 0.5f, $"health sum {healthBefore:F0} -> {after:F0} after 10 s");
            });
        }

        // 지속 중: 게임의 출혈 차단(버프 RemoveAllBloodLosses)이 켜져 있는지, 출혈이 새로 생기지 않는지
        public override void Tick(ActivePerkUse use, float deltaTime)
        {
            // 버프와 이 효과는 길이가 같고 버프가 한 갱신 늦게 켜지므로, 끝나기 직전은 보지 않는다
            var now = Time.time;
            if (now < nextBlockCheck || now > startedAt + use.Duration - 0.5f)
            {
                return;
            }

            nextBlockCheck = now + BlockCheckInterval;
            var health = use.Health;
            blockSamples++;
            if (!health.HasBloodLossBlockers())
            {
                blockerOff++;
            }

            var bleeding = Bleeding(health);
            if (bleeding && !wasBleeding)
            {
                bleedingSeen++;
                ActivePerkLog.Event($"first aid: new bleeding {now - startedAt:F1} s after the press");
            }

            wasBleeding = bleeding;
        }

        public override void Stop(ActivePerkUse use)
        {
            if (blockSamples == 0)
            {
                ActivePerkLog.Skip("firstaid.block", "effect ended before the first bleeding block check");
                return;
            }

            ActivePerkLog.Check("firstaid.block", blockerOff == 0 && bleedingSeen == 0,
                $"bleeding blocker on in {blockSamples - blockerOff} / {blockSamples} checks, new bleedings while running {bleedingSeen}");
        }

        public override string Describe(ActivePerkUse use) => $"blocker {use.Health.HasBloodLossBlockers()}, bleeding {Bleeding(use.Health)}, new bleedings {bleedingSeen}";

        private static bool Damaged(ActiveHealthController health)
        {
            foreach (var part in Parts)
            {
                if (!health.IsBodyPartDestroyed(part))
                {
                    var value = health.GetBodyPartHealth(part);
                    if (value.Current < value.Maximum - 0.5f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Bleeding(ActiveHealthController health) => health.FindActiveEffect<ActiveHealthController.Bleeding>() != null;

        private static int Stimulators(ActiveHealthController health) => health.FindActiveEffects<ActiveHealthController.Stimulator>().Count();

        private static float HealthSum(ActiveHealthController health) => Parts.Where(part => !health.IsBodyPartDestroyed(part)).Sum(part => health.GetBodyPartHealth(part).Current);
    }
}
