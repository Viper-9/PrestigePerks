// 클라이언트는 nullable 을 끄고 빌드한다. 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

using System;

namespace PrestigePerks.Model
{
    public enum ActivePhase
    {
        Ready,
        /// <summary>효과 지속 중.</summary>
        Running,
        Cooldown,
        /// <summary>레이드당 1회 퍽을 이번 레이드에 썼다.</summary>
        Used,
    }

    public enum PressResult
    {
        Started,
        /// <summary>지속 중에 다시 눌렀다. 아무 일도 없다 (중복·연장 없음).</summary>
        AlreadyRunning,
        Cooling,
        Spent,
        /// <summary>쓸 대상이 없다 (실현성조사 2.5). 쓰지 않은 것으로 한다.</summary>
        NoTarget,
    }

    /// <summary>
    /// 액티브 특전 한 레이드의 시계. 레이드를 시작할 때 새로 만든다 (쿨타임·1회는 레이드마다 초기화).
    /// 쿨타임은 효과가 끝난 뒤부터 센다 (즉시 효과는 누른 순간). 시간은 부르는 쪽이 넘긴다 (Time.time). 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public sealed class ActivePerkClock
    {
        private readonly bool oncePerRaid;
        private readonly float duration, cooldown;
        private float endsAt;

        public ActivePerkClock(bool oncePerRaid, float duration, float cooldown)
        {
            this.oncePerRaid = oncePerRaid;
            this.duration = Math.Max(0, duration);
            this.cooldown = Math.Max(0, cooldown);
        }

        public ActivePhase Phase { get; private set; } = ActivePhase.Ready;

        public PressResult Press(float now, bool hasTarget)
        {
            switch (Phase)
            {
                case ActivePhase.Running:
                    return PressResult.AlreadyRunning;
                case ActivePhase.Cooldown:
                    return PressResult.Cooling;
                case ActivePhase.Used:
                    return PressResult.Spent;
            }

            if (!hasTarget)
            {
                return PressResult.NoTarget;
            }

            if (duration > 0)
            {
                Phase = ActivePhase.Running;
                endsAt = now + duration;
            }
            else
            {
                AfterUse(now);
            }

            return PressResult.Started;
        }

        /// <summary>매 프레임. 지속 효과가 이번에 끝났으면 true (부르는 쪽이 효과를 끈다).</summary>
        public bool Tick(float now)
        {
            if (Phase == ActivePhase.Running && now >= endsAt)
            {
                AfterUse(now);
                return true;
            }

            if (Phase == ActivePhase.Cooldown && now >= endsAt)
            {
                Phase = ActivePhase.Ready;
            }

            return false;
        }

        public float Remaining(float now) => Phase == ActivePhase.Running || Phase == ActivePhase.Cooldown ? Math.Max(0, endsAt - now) : 0;

        /// <summary>남은 비율 (지속 중이면 지속 시간, 쿨타임이면 쿨타임 기준). 막대·부채꼴용.</summary>
        public float Fraction(float now)
        {
            var total = Phase == ActivePhase.Running ? duration : Phase == ActivePhase.Cooldown ? cooldown : 0;
            return total > 0 ? Math.Min(1, Remaining(now) / total) : 0;
        }

        /// <summary>레이드 끝·사망: 지속 중이면 사용한 것으로 끝낸다.</summary>
        public void ForceEnd(float now)
        {
            if (Phase == ActivePhase.Running)
            {
                AfterUse(now);
            }
        }

        /// <summary>개발용: 처음 상태로.</summary>
        public void Reset() => Phase = ActivePhase.Ready;

        private void AfterUse(float now)
        {
            if (oncePerRaid)
            {
                Phase = ActivePhase.Used;
                return;
            }

            Phase = ActivePhase.Cooldown;
            endsAt = now + cooldown;
        }
    }
}
