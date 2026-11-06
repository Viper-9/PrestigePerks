using System;
using EFT;
using EFT.HealthSystem;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 전장 보급 (실현성조사 3.11): 에너지·수분을 최대치의 fill% 만큼 채운다. 음식·음료와 같은 ChangeEnergy·ChangeHydration.
    /// 탈진·탈수 효과는 값이 0 보다 커지면 스스로 끝난다.
    /// </summary>
    internal sealed class Resupply : ActivePerkEffect
    {
        public override bool HasTarget(ActivePerkUse use) => NotFull(use.Health.Energy) || NotFull(use.Health.Hydration);

        public override void Start(ActivePerkUse use)
        {
            var health = use.Health;
            var ratio = use.Value("fill") / 100f;
            var energy = health.Energy;
            var hydration = health.Hydration;
            health.ChangeEnergy(energy.Maximum * ratio);
            health.ChangeHydration(hydration.Maximum * ratio);

            var energyWant = Math.Min(energy.Maximum * ratio, energy.Maximum - energy.Current);
            var hydrationWant = Math.Min(hydration.Maximum * ratio, hydration.Maximum - hydration.Current);
            var energyGot = health.Energy.Current - energy.Current;
            var hydrationGot = health.Hydration.Current - hydration.Current;
            ActivePerkLog.Check("resupply.fill", Math.Abs(energyGot - energyWant) < 0.5f && Math.Abs(hydrationGot - hydrationWant) < 0.5f,
                $"energy {energy.Current:F1} -> {health.Energy.Current:F1} (+{energyGot:F1}, want {energyWant:F1}, max {energy.Maximum:F0}), "
                + $"hydration {hydration.Current:F1} -> {health.Hydration.Current:F1} (+{hydrationGot:F1}, want {hydrationWant:F1}, max {hydration.Maximum:F0})");
            use.Later(3f, () =>
            {
                var exhausted = health.FindActiveEffect<ActiveHealthController.Exhaustion>() != null;
                var dehydrated = health.FindActiveEffect<ActiveHealthController.Dehydration>() != null;
                ActivePerkLog.Check("resupply.effects", !exhausted && !dehydrated, $"3 s later: exhaustion {exhausted}, dehydration {dehydrated}");
            });
        }

        private static bool NotFull(ValueStruct value) => value.Current < value.Maximum - 0.5f;
    }
}
