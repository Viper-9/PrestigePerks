using System;
using EFT;
using EFT.HealthSystem;
using PrestigePerks.Game;
using PrestigePerks.Model;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 레이드 하나의 액티브 특전: 키 → 시계 → 효과, HUD, 로그. GameWorld 에 붙고 OnDestroy 에서 정리한다.
    /// 슬롯이 비었거나 P3 전이거나 서버 설정이 꺼졌으면(state 에 액티브 없음) 붙지 않는다. PMC 레이드만.
    /// </summary>
    internal sealed class ActivePerkRaid : MonoBehaviour
    {
        private const float TargetCheckInterval = 0.25f;

        // 지속·쿨타임 CHECK 의 허용 오차 (시계는 프레임마다 보므로 한두 프레임 늦게 끝날 수 있다)
        private const float ClockTolerance = 0.25f;

        private const EBodyPart DevBleedPart = EBodyPart.Stomach;

        private ActivePerkUse use;
        private ActivePerkEffect effect;
        private ActivePerkClock clock;
        private ActivePerkHud hud;
        private bool hasTarget = true, broken, ended;
        private float nextTargetCheck;

        // 지속·쿨타임을 잰 시작 시각 (없으면 -1, 초기화 키로 지운다)
        private float runStartedAt = -1, cooldownStartedAt = -1;

        public static void Begin(GameWorld world)
        {
            try
            {
                var player = world.MainPlayer;
                var state = PerkController.Instance.State;
                if (!PerkTarget.IsPmc(player) || state == null)
                {
                    return;
                }

                var (perk, tier) = Pick(state);
                var info = state.ActiveInfo(perk)?.Active;
                var effect = info == null ? null : ActivePerkEffects.Create(perk);
                if (effect == null)
                {
                    ActivePerkLog.Event($"no active perk this raid (slot {state.ActivePerkSlot?.Perk ?? "closed or empty"}, enabled {state.ActivePerksEnabled}, picked {perk ?? "-"})");
                    return;
                }

                var raid = world.gameObject.AddComponent<ActivePerkRaid>();
                ActivePerkLog.RaidStarted(perk, tier);
                raid.Load(player, perk, tier, info, effect);
            }
            catch (Exception e)
            {
                Log.Error($"active perk raid start failed: {e}");
            }
        }

        // 퍽 하나를 붙인다: 레이드 시작, 그리고 개발자 모드 초기화 키로 시험 퍽을 바꿀 때
        private void Load(Player player, string perk, int tier, ActivePerkInfoDto info, ActivePerkEffect next)
        {
            use = new ActivePerkUse { Host = this, Player = player, Health = player.ActiveHealthController, Perk = perk, Tier = tier, Info = info, Duration = (float)info.DurationAt(tier) };
            effect = next;
            clock = new ActivePerkClock(info.IsOncePerRaid, use.Duration, (float)info.CooldownAt(tier));
            hud = ActivePerkHud.Create(PerkController.Instance.Locale, perk, tier);
            broken = false;
            hasTarget = true;
            runStartedAt = cooldownStartedAt = -1;
            try
            {
                effect.RaidStarted(use);
            }
            catch (Exception e)
            {
                broken = true;
                hud.Destroy();
                ActivePerkLog.Error($"active perk {perk} disabled for this raid: start failed", e);
            }
        }

        // 개발자 모드의 시험 퍽·단계가 먼저, 아니면 액티브 칸 (단계 = 서버 적용 단계)
        private static (string Perk, int Tier) Pick(PerkStateDto state)
        {
            var slotPerk = PerkPageModel.PerkInSlot(state, PerkPageModel.ActiveSlot);
            var testPerk = Plugin.DevMode.Value ? Plugin.ActiveTestPerk.Value : null;
            var perk = string.IsNullOrEmpty(testPerk) || testPerk == Plugin.ActiveTestSlot ? slotPerk : testPerk;
            if (perk == null)
            {
                return (null, 0);
            }

            var testTier = Plugin.DevMode.Value ? Plugin.ActiveTestTier.Value : 0;
            var tier = testTier > 0 ? testTier
                : perk == slotPerk && state.ActivePerkTier > 0 ? state.ActivePerkTier
                : Math.Max(1, Math.Min(PerkPageModel.OwnedTier(state, perk), PerkPageModel.TierCap(state)));
            return (perk, Math.Max(1, Math.Min(tier, PerkPageModel.MaxTier)));
        }

        private void Update()
        {
            if (ended)
            {
                return;
            }

            if (broken)
            {
                // 고장 난 퍽도 개발자 모드 초기화 키로 다른 시험 퍽으로 바꿀 수 있다
                if (PerkEffects.DevMode && Plugin.ActiveResetKey.Value.IsDown())
                {
                    ResetOrSwitch();
                }

                return;
            }

            try
            {
                var now = Time.time;
                if (!use.Player.HealthController.IsAlive)
                {
                    End("dead");
                    return;
                }

                if (now >= nextTargetCheck && clock.Phase == ActivePhase.Ready)
                {
                    nextTargetCheck = now + TargetCheckInterval;
                    hasTarget = effect.HasTarget(use);
                }

                if (Plugin.ActiveKey.Value.IsDown())
                {
                    Press(now);
                }

                if (clock.Phase == ActivePhase.Running)
                {
                    effect.Tick(use, Time.deltaTime);
                }

                var phaseBefore = clock.Phase;
                if (clock.Tick(now))
                {
                    effect.Stop(use);
                    ActivePerkLog.Event($"{use.Perk} ended");
                    RunEnded(now);
                }
                else if (phaseBefore == ActivePhase.Cooldown && clock.Phase == ActivePhase.Ready)
                {
                    CooldownEnded(now);
                }

                DevKeys(now);
                hud.Show(ActivePerkHudLook.Of(clock.Phase, hasTarget), clock.Remaining(now), clock.Fraction(now));
            }
            catch (Exception e)
            {
                broken = true;
                ActivePerkLog.Error($"active perk {use?.Perk} disabled for this raid after an error", e);
                SafeStop();
                hud?.Destroy();
            }
        }

        private void Press(float now)
        {
            // 누른 순간의 대상으로 판단한다 (초당 4번 확인 사이에 바뀌었을 수 있다)
            if (clock.Phase == ActivePhase.Ready)
            {
                hasTarget = effect.HasTarget(use);
            }

            var result = clock.Press(now, hasTarget);
            ActivePerkLog.Pressed();
            ActivePerkLog.Event($"press {use.Perk} T{use.Tier}: {result}");
            if (result == PressResult.Started)
            {
                effect.Start(use);
                if (clock.Phase == ActivePhase.Running)
                {
                    runStartedAt = now;
                }
                else
                {
                    // 즉시 효과: 누른 순간이 효과 끝
                    AfterUse(now);
                }

                return;
            }

            if (result == PressResult.NoTarget)
            {
                ActivePerkLog.Check("clock.noTarget", clock.Phase == ActivePhase.Ready, $"no target press left the perk {clock.Phase} (want Ready, not used)");
            }

            if (result != PressResult.AlreadyRunning)
            {
                hud.Deny();
            }
        }

        // 지속 효과가 끝남: 실제 지속 시간을 보고 쿨타임·1회 사용으로 넘어갔는지 본다
        private void RunEnded(float now)
        {
            if (runStartedAt >= 0)
            {
                var ran = now - runStartedAt;
                ActivePerkLog.Check("clock.duration", Mathf.Abs(ran - use.Duration) <= ClockTolerance, $"{use.Perk} ran {ran:F2} s, want {use.Duration:F0} s");
                runStartedAt = -1;
            }

            AfterUse(now);
        }

        // 효과가 끝난 순간 (즉시 효과는 누른 순간): 레이드당 1회면 사용함, 아니면 여기서부터 쿨타임
        private void AfterUse(float now)
        {
            var once = use.Info.IsOncePerRaid;
            var want = once ? ActivePhase.Used : ActivePhase.Cooldown;
            ActivePerkLog.Check("clock.afterUse", clock.Phase == want,
                once ? $"once per raid: {clock.Phase} (want Used)" : $"cooldown {use.Info.CooldownAt(use.Tier):F0} s starts at the effect end: {clock.Phase} (want Cooldown)");
            cooldownStartedAt = once ? -1 : now;
        }

        private void CooldownEnded(float now)
        {
            if (cooldownStartedAt < 0)
            {
                return;
            }

            var cooled = now - cooldownStartedAt;
            var want = (float)use.Info.CooldownAt(use.Tier);
            ActivePerkLog.Check("clock.cooldown", Mathf.Abs(cooled - want) <= ClockTolerance, $"{use.Perk} ready again {cooled:F2} s after the effect end, want {want:F0} s");
            cooldownStartedAt = -1;
        }

        private void OnDestroy() => End("raid end");

        // 레이드 끝·사망: 지속 중이면 효과를 끄고, HUD 를 지우고, 요약을 남긴다. 두 번 불려도 한 번만
        private void End(string why)
        {
            if (ended || use == null)
            {
                return;
            }

            ended = true;
            if (clock.Phase == ActivePhase.Running)
            {
                SafeStop();
                clock.ForceEnd(Time.time);
            }

            hud?.Destroy();
            string extra = null;
            try
            {
                extra = effect.Summary();
            }
            catch (Exception e)
            {
                ActivePerkLog.Error("summary", e);
            }

            ActivePerkLog.RaidEnded(why, extra);
            enabled = false;
        }

        private void SafeStop()
        {
            try
            {
                effect.Stop(use);
            }
            catch (Exception e)
            {
                ActivePerkLog.Error($"{use.Perk} stop", e);
            }
        }

        /// <summary>
        /// 개발자 모드 초기화 키: 지속 중이면 효과를 끄고, F12 시험 퍽·단계를 다시 읽는다. 바뀌었으면 그 퍽으로 갈아 끼우고 (HUD 도 새로),
        /// 같으면 사용 기록·쿨타임만 지운다. 한 레이드에서 여러 퍽을 차례로 시험하는 용도.
        /// </summary>
        private void ResetOrSwitch()
        {
            if (!broken && clock.Phase == ActivePhase.Running)
            {
                SafeStop();
            }

            var state = PerkController.Instance.State;
            var (perk, tier) = state == null ? (null, 0) : Pick(state);
            var info = perk == null ? null : state.ActiveInfo(perk)?.Active;
            if (info != null && (perk != use.Perk || tier != use.Tier))
            {
                var next = ActivePerkEffects.Create(perk);
                if (next != null)
                {
                    var summary = SafeSummary();
                    ActivePerkLog.Event($"switch {use.Perk} T{use.Tier} -> {perk} T{tier}{(string.IsNullOrEmpty(summary) ? "" : $" ({use.Perk}: {summary})")}");
                    hud?.Destroy();
                    ActivePerkLog.Switched(perk, tier);
                    Load(use.Player, perk, tier, info, next);
                    return;
                }
            }

            if (broken)
            {
                return;
            }

            clock.Reset();
            runStartedAt = cooldownStartedAt = -1;
            hasTarget = effect.HasTarget(use);
            ActivePerkLog.Event($"reset {use.Perk}");
        }

        private string SafeSummary()
        {
            try
            {
                return effect.Summary();
            }
            catch (Exception e)
            {
                ActivePerkLog.Error("summary", e);
                return null;
            }
        }

        // 개발자 모드일 때만: 초기화 키(위), 표시 키(스크린샷 + 상태 한 줄), 출혈 키(아래)
        private void DevKeys(float now)
        {
            if (!PerkEffects.DevMode)
            {
                return;
            }

            if (Plugin.ActiveResetKey.Value.IsDown())
            {
                ResetOrSwitch();
            }

            if (Plugin.MarkKey.Value.IsDown())
            {
                var detail = effect.Describe(use);
                ActivePerkLog.Mark($"{use.Perk} T{use.Tier} {clock.Phase} {clock.Remaining(now):F1} s, target {hasTarget}{(string.IsNullOrEmpty(detail) ? "" : " | " + detail)}");
            }

            if (Plugin.BleedKey.Value.IsDown())
            {
                DevBleed();
            }
        }

        // 시험용 출혈: 가벼운 출혈 ↔ 심한 출혈. 출혈 피해는 걸린 부위와 상관없이 모든 부위에 들어간다.
        // 게임처럼 출혈 막기 버프 중에는 걸지 않는다 (응급 처치의 firstaid.block 이 틀리게 실패하지 않도록)
        private void DevBleed()
        {
            var health = use.Health;
            if (health.HasBloodLossBlockers())
            {
                ActivePerkLog.Event("dev bleed: skipped, blood loss blocker is on");
                return;
            }

            var heavy = health.FindExistingEffect<ActiveHealthController.LightBleeding>(DevBleedPart) != null;
            if (heavy)
            {
                health.DoBleed<ActiveHealthController.HeavyBleeding>(DevBleedPart);
            }
            else
            {
                health.DoBleed<ActiveHealthController.LightBleeding>(DevBleedPart);
            }

            ActivePerkLog.Event($"dev bleed: {(heavy ? "heavy" : "light")} on {DevBleedPart}, added {health.FindExistingEffect<ActiveHealthController.Bleeding>(DevBleedPart) != null}");
        }
    }
}
