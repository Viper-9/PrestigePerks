using System;
using System.Collections;
using EFT;
using EFT.HealthSystem;
using PrestigePerks.Model;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>한 번 쓴 액티브 특전의 정보. 효과가 값을 읽는다.</summary>
    internal sealed class ActivePerkUse
    {
        public Player Player;
        public ActiveHealthController Health;
        public string Perk;
        public int Tier;
        public ActivePerkInfoDto Info;
        public float Duration;

        /// <summary>레이드 컴포넌트. 늦은 확인(<see cref="Later"/>)이 이 위에서 돈다 (레이드가 끝나면 같이 멈춘다).</summary>
        public MonoBehaviour Host;

        public float Value(string name) => (float)Info.Value(name, Tier);

        /// <summary>seconds 초 뒤 한 번 (게임 시간). 즉시 효과의 늦은 CHECK 용.</summary>
        public void Later(float seconds, Action action)
        {
            if (Host != null)
            {
                Host.StartCoroutine(Wait(seconds, action));
            }
        }

        private static IEnumerator Wait(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            try
            {
                action();
            }
            catch (Exception e)
            {
                ActivePerkLog.Error("later check", e);
            }
        }
    }

    /// <summary>
    /// 퍽 하나의 효과. 레이드마다 새로 만든다. HasTarget 은 초당 4번, Tick 은 지속 중 매 프레임 (할당 없이).
    /// Stop 은 지속 효과가 끝날 때와 레이드 끝·사망 때 부르며, 바꾼 값을 모두 되돌린다. 예외는 부르는 쪽이 잡고 그 레이드 동안 퍽을 끈다.
    /// </summary>
    internal abstract class ActivePerkEffect
    {
        /// <summary>레이드 시작 때 한 번 (준비·시작 CHECK). 예외면 그 레이드 동안 퍽을 끈다.</summary>
        public virtual void RaidStarted(ActivePerkUse use)
        {
        }

        public virtual bool HasTarget(ActivePerkUse use) => true;

        public abstract void Start(ActivePerkUse use);

        public virtual void Tick(ActivePerkUse use, float deltaTime)
        {
        }

        public virtual void Stop(ActivePerkUse use)
        {
        }

        /// <summary>표시 키(MARK) 상태 줄에 붙일 효과 상태. 없으면 "".</summary>
        public virtual string Describe(ActivePerkUse use) => "";

        /// <summary>레이드 끝 SUMMARY 에 붙일 요약. 없으면 null.</summary>
        public virtual string Summary() => null;
    }

    internal static class ActivePerkEffects
    {
        public static ActivePerkEffect Create(string perk)
        {
            switch (perk)
            {
                case "secondwind":
                    return new SecondWind();
                case "steadfast":
                    return new Steadfast();
                case "resupply":
                    return new Resupply();
                case "fieldrepair":
                    return new FieldRepair();
                case "emergencysurgery":
                    return new EmergencySurgery();
                case "firstaid":
                    return new FirstAid();
                case "focus":
                    return new Focus();
                case "composure":
                    return new Composure();
                case "grit":
                    return new Grit();
                case "sonarpulse":
                    return new SonarPulse();
                default:
                    return null;
            }
        }
    }
}
