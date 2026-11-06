// 클라이언트는 nullable 을 끄고 빌드한다. 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

using System;

namespace PrestigePerks.Model
{
    public enum HudLook
    {
        Ready,
        Running,
        Cooldown,
        Used,
        /// <summary>준비됐지만 쓸 대상이 없다. 쿨타임 모양 + "대상 없음" (확인 받을 것 3).</summary>
        NoTarget,
    }

    public static class ActivePerkHudLook
    {
        public static HudLook Of(ActivePhase phase, bool hasTarget)
        {
            switch (phase)
            {
                case ActivePhase.Running:
                    return HudLook.Running;
                case ActivePhase.Cooldown:
                    return HudLook.Cooldown;
                case ActivePhase.Used:
                    return HudLook.Used;
                default:
                    return hasTarget ? HudLook.Ready : HudLook.NoTarget;
            }
        }

        /// <summary>"2:15". 초는 올림 (0:00 이 보이지 않게).</summary>
        public static string Clock(float seconds)
        {
            var total = (int)Math.Ceiling(Math.Max(0, seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
