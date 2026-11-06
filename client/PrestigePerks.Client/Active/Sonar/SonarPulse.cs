using System.Collections.Generic;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 음파 탐지 (실현성조사 5.1 확정 값): 지속 동안 4초마다 내 위치에서 파동이 퍼지고 (반경까지 1초), 닿은 적의 그 순간 모습을 잔상으로 3초 남긴다
    /// (앞 30% 그대로, 나머지 동안 사라짐). 잔상은 벽 너머로 보인다. 파동 고리는 발 높이에 보이고 벽·지형에 가려진다 (F12 로 끔).
    /// </summary>
    internal sealed class SonarPulse : ActivePerkEffect
    {
        private const float Period = 4f, WaveTime = 1f, EchoShare = 0.75f;
        private const float FillAlpha = 0.045f, RimPower = 4f;
        private static readonly Color EchoColor = new Color(0.86f, 0.88f, 0.25f, 1f);

        private readonly HashSet<Player> hit = new HashSet<Player>();
        private readonly Stopwatch watch = new Stopwatch();
        private SonarEchoes echoes;
        private SonarRing ring;
        private Vector3 origin;
        private float radius, waveStart = -1, nextPulse, lastPulse = -1;
        private int pulses, pulseHits, pulseTargets, totalPulses, totalEchoes;
        private float pulseMs, frameMaxMs, maxOffset;

        public override void RaidStarted(ActivePerkUse use)
        {
            var ok = SonarShaders.Load();
            ActivePerkLog.Check("sonar.shaders", ok, SonarShaders.Status);
        }

        // 셰이더가 없으면 그릴 수 없으므로 "대상 없음"
        public override bool HasTarget(ActivePerkUse use) => SonarShaders.Echo != null;

        public override void Start(ActivePerkUse use)
        {
            radius = use.Value("radius");
            echoes = new SonarEchoes(EchoColor, FillAlpha, RimPower);
            ring = Plugin.SonarWaveRing.Value ? SonarRing.Create(EchoColor) : null;
            hit.Clear();
            pulses = 0;
            waveStart = -1;
            lastPulse = -1;
            nextPulse = Time.time;
            ActivePerkLog.Event($"sonar: radius {radius:F0} m, {use.Duration:F0} s, ring {(ring != null ? "on" : "off")}");
        }

        public override void Tick(ActivePerkUse use, float deltaTime)
        {
            var now = Time.time;
            var world = Singleton<GameWorld>.Instance;
            if (waveStart < 0 && now >= nextPulse && world != null)
            {
                StartWave(use, world, now);
            }

            var intensity = Brightness();
            if (waveStart >= 0)
            {
                var wave = Mathf.Min(radius, (now - waveStart) * radius / WaveTime);
                watch.Restart();
                pulseHits += echoes.Hit(origin, wave, hit, ref maxOffset);
                var ms = (float)watch.Elapsed.TotalMilliseconds;
                pulseMs += ms;
                frameMaxMs = Mathf.Max(frameMaxMs, ms);
                ring?.Show(origin, wave, radius, intensity);
                if (wave >= radius)
                {
                    EndWave();
                }
            }

            echoes.Fade(now, Period * EchoShare, intensity);
        }

        public override void Stop(ActivePerkUse use)
        {
            if (waveStart >= 0)
            {
                EndWave();
            }

            ring?.Destroy();
            ring = null;
            if (echoes == null)
            {
                return;
            }

            var wrong = echoes.Clear();
            var alive = echoes.Alive;
            echoes.Destroy();
            echoes = null;
            ActivePerkLog.Check("sonar.restored", wrong == 0 && alive == 0, $"culling / occlusion not restored {wrong}, echoes left {alive}, pulses {pulses}");
        }

        public override string Describe(ActivePerkUse use) => $"pulses {pulses}, echoes alive {echoes?.Alive ?? 0}, brightness {Brightness():F2}";

        public override string Summary() => $"sonar pulses {totalPulses}, echoes {totalEchoes}";

        private void StartWave(ActivePerkUse use, GameWorld world, float now)
        {
            if (lastPulse >= 0)
            {
                var gap = now - lastPulse;
                ActivePerkLog.Check("sonar.period", Mathf.Abs(gap - Period) <= 0.05f, $"pulse #{pulses + 1} {gap:F2} s after the last start");
            }

            pulses++;
            totalPulses++;
            lastPulse = now;
            waveStart = now;
            nextPulse = now + Period;
            origin = use.Player.Position;
            hit.Clear();
            pulseHits = 0;
            pulseMs = 0;
            frameMaxMs = 0;
            maxOffset = 0;
            watch.Restart();
            pulseTargets = echoes.Gather(world, use.Player, origin, radius);
            pulseMs += (float)watch.Elapsed.TotalMilliseconds;
        }

        private void EndWave()
        {
            waveStart = -1;
            ring?.Hide();
            totalEchoes += pulseHits;
            if (pulseTargets == 0)
            {
                ActivePerkLog.Skip("sonar.echoes", $"pulse #{pulses}: no target in radius");
            }
            else
            {
                // 반경 안 대상이 있는데 잔상이 하나도 없으면 실패
                ActivePerkLog.Check("sonar.echoes", pulseHits > 0 && maxOffset <= SonarEchoes.MaxOffset,
                    $"pulse #{pulses}: targets in radius {pulseTargets}, echoes {pulseHits}, max offset from body {maxOffset:F2} m");
            }
            ActivePerkLog.Check("sonar.cost", frameMaxMs <= 5f, $"pulse #{pulses}: bake total {pulseMs:F1} ms, max in one frame {frameMaxMs:F1} ms");
        }

        // 야간 투시경·열화상을 켜고 끄면 그에 맞는 밝기로 (둘 다 켜져 있으면 열화상)
        private static float Brightness()
        {
            var cameras = CameraManager.Instance;
            var thermal = cameras.ThermalVision != null && cameras.ThermalVision.On;
            var night = cameras.NightVision != null && cameras.NightVision.On;
            return thermal ? Plugin.SonarBrightnessThermal.Value : night ? Plugin.SonarBrightnessNight.Value : Plugin.SonarBrightness.Value;
        }
    }
}
