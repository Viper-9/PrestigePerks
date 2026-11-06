using System.Linq;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 정신 집중 (실현성조사 3.5): 진통제와 같은 PainKiller 효과를 지속 시간만큼 건다 (통증·떨림·팔다리 부상 패널티를 게임이 끈다, 체력 화면에 진통제 아이콘).
    /// 진통제가 가리지 않는 터널 시야는 시작 때 끝내고, 지속 중 새로 생기면 바로 끝낸다.
    /// </summary>
    internal sealed class Focus : ActivePerkEffect
    {
        private static readonly EBodyPart[] Limbs = { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };

        // 지속 중 패널티 확인: 진통 효과가 붙은 뒤 초당 4번
        private const float HeldCheckDelay = 1f, HeldCheckInterval = 0.25f;

        private ActiveHealthController subscribed;
        private int tunnelBlocked, heldSamples, heldBroken;
        private float startedAt, nextHeldCheck;
        private string firstBroken;

        public override bool HasTarget(ActivePerkUse use)
        {
            var health = use.Health;
            if (health.FindActiveEffect<ActiveHealthController.Pain>() != null
                || health.FindActiveEffect<ActiveHealthController.Tremor>() != null
                || health.FindActiveEffect<ActiveHealthController.TunnelVision>() != null)
            {
                return true;
            }

            foreach (var limb in Limbs)
            {
                if (health.IsBodyPartDestroyed(limb) || health.FindActiveEffect<ActiveHealthController.Fracture>(limb) != null)
                {
                    return true;
                }
            }

            return false;
        }

        public override void Start(ActivePerkUse use)
        {
            var health = use.Health;
            var before = Penalties(use.Player);
            health.AddEffect<ActiveHealthController.PainKiller>(EBodyPart.Head, 0f, use.Duration, 5f);
            health.ResidueEffectsWhere(EBodyPart.Common, effect => effect is ActiveHealthController.TunnelVision);
            tunnelBlocked = heldSamples = heldBroken = 0;
            firstBroken = null;
            startedAt = Time.time;
            nextHeldCheck = startedAt + HeldCheckDelay;
            subscribed = health;
            health.EffectStartedEvent += OnEffectStarted;
            use.Later(1f, () =>
            {
                ActivePerkLog.Check("focus.painkiller", health.FindActiveEffect<ActiveHealthController.PainKiller>() != null,
                    $"painkiller effect {(health.FindActiveEffect<ActiveHealthController.PainKiller>() != null ? "active" : "missing")} for {use.Duration:F0} s");
                if (before == null)
                {
                    ActivePerkLog.Skip("focus.penalties", "no hand tremor, limp or arm fracture sway at the press");
                    return;
                }

                var after = Penalties(use.Player);
                ActivePerkLog.Check("focus.penalties", after == null && OnPainkillers(use.Player), $"{before} -> {after ?? "none"}, on painkillers {OnPainkillers(use.Player)}");
            });
        }

        // 지속 중: 새로 다치거나 아파도 떨림·절뚝임·팔 부상 흔들림이 생기지 않는지 (게임이 진통 효과로 끈다)
        public override void Tick(ActivePerkUse use, float deltaTime)
        {
            var now = Time.time;
            if (now < nextHeldCheck || now > startedAt + use.Duration - 0.5f)
            {
                return;
            }

            nextHeldCheck = now + HeldCheckInterval;
            heldSamples++;
            var penalties = Penalties(use.Player);
            if (penalties != null)
            {
                heldBroken++;
                firstBroken = firstBroken ?? $"{penalties} at {now - startedAt:F1} s";
            }
        }

        public override void Stop(ActivePerkUse use)
        {
            if (subscribed != null)
            {
                subscribed.EffectStartedEvent -= OnEffectStarted;
                subscribed = null;
            }

            var tunnel = use.Health.FindActiveEffect<ActiveHealthController.TunnelVision>() != null;
            ActivePerkLog.Check("focus.tunnel", !tunnel, $"tunnel vision at the end {tunnel}, new ones ended while running {tunnelBlocked}");
            ActivePerkLog.Check("focus.unsubscribed", subscribed == null, "effect started event unsubscribed");
            if (heldSamples > 0)
            {
                ActivePerkLog.Check("focus.held", heldBroken == 0, $"penalty seen in {heldBroken} / {heldSamples} checks while running{(firstBroken != null ? $" (first: {firstBroken})" : "")}");
            }
        }

        public override string Describe(ActivePerkUse use) => $"tunnel vision ended {tunnelBlocked}, penalties {Penalties(use.Player) ?? "none"}, on painkillers {OnPainkillers(use.Player)}";

        /// <summary>
        /// 게임이 무기·걸음에 실제로 거는 패널티 (ProceduralWeaponAnimation.PhysicalConditionUpdated): 손 떨림, 다리 부상 절뚝임, 팔 부상 흔들림.
        /// 진통 효과 중에는 게임이 셋 다 끈다. 없으면 null.
        /// </summary>
        private static string Penalties(Player player)
        {
            var weapon = player.ProceduralWeaponAnimation;
            var tremor = weapon.Breath.TremorOn;
            var limp = weapon.Walk.CurrentWalkPreset == WalkEffector.EWalkPreset.lame;
            var arm = weapon.Breath.Fracture;
            if (!tremor && !limp && !arm)
            {
                return null;
            }

            return string.Join(" + ", new[] { tremor ? "tremor" : null, limp ? "limp" : null, arm ? "arm fracture" : null }.Where(name => name != null));
        }

        private static bool OnPainkillers(Player player) => player.MovementContext.PhysicalConditionIs(EPhysicalCondition.OnPainkillers);

        private void OnEffectStarted(IHealthEffect effect)
        {
            if (effect is ActiveHealthController.TunnelVision tunnel)
            {
                tunnel.ForceResidue();
                tunnelBlocked++;
            }
        }
    }
}
