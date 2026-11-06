namespace PrestigePerks.Active
{
    /// <summary>
    /// 버티기 (실현성조사 3.4): 지속 동안 PhysicalBase.EncumberDisabled 를 켠다 (게임이 봇에 쓰는 스위치). 무게 한도가 9000 이 되어
    /// 과적 속도 제한·스태미나 소모가 사라진다. 관성은 남는다. 과적 아이콘은 스위치가 켜진 동안 갱신되지 않아 직접 지우고, 끝나면 맞춘다.
    /// </summary>
    internal sealed class Steadfast : ActivePerkEffect
    {
        private const float DisabledLimit = 9000f;

        public override bool HasTarget(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            return p.IsEncumbered || p.Overweight > 0f;
        }

        public override void Start(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            var before = p.WalkOverweightLimits;
            p.EncumberDisabled = true;
            use.Health.SetEncumbered(false);
            use.Health.SetOverEncumbered(false);
            ActivePerkLog.Check("steadfast.on", p.WalkOverweightLimits.x >= DisabledLimit,
                $"walk overweight limits {before} -> {p.WalkOverweightLimits}, overweight {p.Overweight:F2}");
        }

        public override void Stop(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            p.EncumberDisabled = false;
            use.Health.SetEncumbered(p.IsEncumbered);
            use.Health.SetOverEncumbered(p.Overweight >= 1f);
            ActivePerkLog.Check("steadfast.restored", p.WalkOverweightLimits.x < DisabledLimit,
                $"walk overweight limits {p.WalkOverweightLimits}, encumbered {p.IsEncumbered}, overweight {p.Overweight:F2}");
        }

        public override string Describe(ActivePerkUse use)
        {
            var p = use.Player.Physical;
            return $"walk limits {p.WalkOverweightLimits}, overweight {p.Overweight:F2}, encumbered {p.IsEncumbered}";
        }
    }
}
