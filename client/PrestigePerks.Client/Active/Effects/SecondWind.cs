using EFT;
using EFT.HealthSystem;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 숨 고르기 (실현성조사 3.2): 스태미나·팔 스태미나·산소를 최대로 채우고 만성 피로(ChronicStaminaFatigue)를 끝낸다.
    /// UpdateStamina 는 게임이 동기화에 쓰는 함수라 숨소리·탈진 경계 이벤트도 같이 갱신된다.
    /// </summary>
    internal sealed class SecondWind : ActivePerkEffect
    {
        public override bool HasTarget(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            return NotFull(p.Stamina) || NotFull(p.HandsStamina) || NotFull(p.Oxygen) || HasFatigue(use.Health);
        }

        public override void Start(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            var before = $"{p.Stamina.Current:F0} / {p.HandsStamina.Current:F0} / {p.Oxygen.Current:F0}";
            Fill(p.Stamina);
            Fill(p.HandsStamina);
            Fill(p.Oxygen);
            use.Health.ResidueEffectsWhere(EBodyPart.Common, effect => effect is ActiveHealthController.ChronicStaminaFatigue);

            ActivePerkLog.Check("secondwind.full", !NotFull(p.Stamina) && !NotFull(p.HandsStamina) && !NotFull(p.Oxygen),
                $"stamina / hands / oxygen {before} -> {p.Stamina.Current:F0} / {p.HandsStamina.Current:F0} / {p.Oxygen.Current:F0}"
                + $" (max {(float)p.Stamina.TotalCapacity:F0} / {(float)p.HandsStamina.TotalCapacity:F0} / {(float)p.Oxygen.TotalCapacity:F0})");
            // ForceResidue 는 바로 지우지 않고 끝나는 단계로 보내므로 잠시 뒤 확인한다
            var health = use.Health;
            use.Later(1f, () => ActivePerkLog.Check("secondwind.fatigue", !HasFatigue(health), $"fatigue effect {(HasFatigue(health) ? "still active" : "gone")}"));
        }

        private static bool NotFull(Stamina stamina) => stamina.Current < (float)stamina.TotalCapacity - 0.5f;

        private static void Fill(Stamina stamina) => stamina.UpdateStamina(stamina.TotalCapacity);

        private static bool HasFatigue(ActiveHealthController health) => health.FindActiveEffect<ActiveHealthController.ChronicStaminaFatigue>() != null;
    }
}
