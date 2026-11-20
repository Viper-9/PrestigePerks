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

    /// <summary>HUD 모양이 바뀌는 순간 띄우는 연출 (dev-docs 6 "사용 연출").</summary>
    public enum HudCue
    {
        None,
        /// <summary>썼다: 화면 가장자리 금빛, 육각 튐 + 금색 링.</summary>
        Use,
        /// <summary>지속 효과가 끝났다: 화면 가장자리 회색 빛, 육각 튐 + 회색 링.</summary>
        End,
        /// <summary>쿨타임이 끝났다: 금색 링 + 육각 반짝, "준비됨" 깜빡임.</summary>
        ReadyAgain,
    }

    public static class ActivePerkHudLook
    {
        public const int EndingSoonSeconds = 5, ReadySoonSeconds = 3;

        public static HudCue CueOf(HudLook from, HudLook to)
        {
            var wasReady = from == HudLook.Ready || from == HudLook.NoTarget;
            if (wasReady && (to == HudLook.Running || to == HudLook.Cooldown || to == HudLook.Used))
            {
                return HudCue.Use;
            }

            if (from == HudLook.Running && (to == HudLook.Cooldown || to == HudLook.Used))
            {
                return HudCue.End;
            }

            if (from == HudLook.Cooldown && (to == HudLook.Ready || to == HudLook.NoTarget))
            {
                return HudCue.ReadyAgain;
            }

            // 개발자 초기화 키(지속·사용함 → 준비됨), 대상 있음 ↔ 없음은 알리지 않는다
            return HudCue.None;
        }

        /// <summary>지속 효과 남은 5초 (막대·상태 글자 붉게 깜빡임). wholeSeconds 는 올림한 남은 초 (HUD 숫자와 같다).</summary>
        public static bool EndingSoon(HudLook look, int wholeSeconds) => look == HudLook.Running && wholeSeconds <= EndingSoonSeconds;

        /// <summary>쿨타임 남은 3초 (가운데 숫자 금색).</summary>
        public static bool ReadySoon(HudLook look, int wholeSeconds) => look == HudLook.Cooldown && wholeSeconds <= ReadySoonSeconds;

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
