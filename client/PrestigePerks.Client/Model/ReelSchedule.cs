using System;
using System.Collections.Generic;

namespace PrestigePerks.Model
{
    /// <summary>마지막 릴의 멈춤 연출.</summary>
    internal enum ReelEffect
    {
        /// <summary>그냥 멈춘다.</summary>
        Normal,

        /// <summary>결과를 한 칸 지나쳐 멈춘 척하다가 위로 끌려 올라가 돌아온다.</summary>
        FakeOut,

        /// <summary>엉뚱한 칸에 멈춘 뒤 갑자기 거꾸로 돌아 결과에서 멈춘다.</summary>
        Reverse,
    }

    /// <summary>
    /// 릴 띠 움직임. 띠는 특전 칸을 세로로 이은 것이고 위치(칸 단위, 소수)가 커질수록 칸이 아래로 흐른다.
    /// 결과가 오면 결과 칸까지 감속하며 멈춘다. 가짜 멈춤이면 결과 칸을 한 칸 지나쳐 멈췄다가 거꾸로 올라가 결과 칸으로 돌아온다.
    /// 게임 DLL 없이 테스트하려고 Unity 를 쓰지 않는다.
    /// </summary>
    internal static class ReelSchedule
    {
        /// <summary>빨리 돌 때 초당 칸 수.</summary>
        public const float Speed = 12f;

        /// <summary>빨리 돌기까지 걸리는 시간(초).</summary>
        public const float SpinUp = 0.25f;

        /// <summary>결과가 오면 결과 칸까지 지나가는 칸 수.</summary>
        public const int LandingItems = 14;

        /// <summary>가짜로 멈춰 있는 시간, 되돌아가는 시간(초).</summary>
        public const float FakeHold = 1.0f, SnapBackTime = 0.3f;

        /// <summary>
        /// 가짜 멈춤 감속 곡선 차수와 시간 배율. 차수가 클수록 미끼 칸이 마지막에 더 오래 기어 들어온다 (보통 멈춤은 3, 1배).
        /// 시간을 차수만큼 늘리면 너무 길어서 1.4배로 자른다 (감속 시작이 조금 빨라지지만 빨리 도는 중이라 티가 안 난다).
        /// </summary>
        public const int FakeEasePower = 5;
        public const float FakeLandingStretch = 1.4f;

        /// <summary>버틸 때 결과 쪽으로 움찔하는 때(멈춘 시간 비율)와 크기(칸). 두 번째가 더 크다.</summary>
        public static readonly (float At, float Size)[] Tugs = { (0.35f, 0.10f), (0.68f, 0.22f) };

        private const float TugWidth = 0.07f;

        /// <summary>
        /// 리롤마다 마지막 릴에 특별 연출이 나올 확률. 지금은 확인용으로 합쳐 50%다.
        /// 출시 때는 가짜 멈춤 약 15%, 역회전 약 5% 로 드물게 한다 (dev-docs 2_특전.md).
        /// </summary>
        public const double FakeOutChance = 0.25, ReverseChance = 0.25;

        /// <summary>역회전: 엉뚱한 칸에 멈춰 있는 시간(초), 거꾸로 지나가는 칸 수, 거꾸로 돌 때 처음 속도 배율.</summary>
        public const float ReverseHold = 0.55f, ReverseSpeedScale = 1.3f;
        public const int ReverseItems = 9;

        /// <summary>
        /// 일반 리롤의 랜덤 연출은 끈다 (2026-10-18, 흔들기와 겹쳐 헷갈려서). 흔들기는 2026-10-25 개편에서 뺐다.
        /// 가짜 멈춤·역회전 코드는 다시 쓸 수 있게 남겨 둔다.
        /// </summary>
        public static ReelEffect PickEffect(Random random)
        {
            // var roll = random.NextDouble();
            // return roll < FakeOutChance ? ReelEffect.FakeOut
            //     : roll < FakeOutChance + ReverseChance ? ReelEffect.Reverse
            //     : ReelEffect.Normal;
            return ReelEffect.Normal;
        }

        /// <summary>역회전 시간. 처음에 가장 빠르고(빨리 돌 때의 배율만큼) 점점 느려진다.</summary>
        public static float ReverseDuration(float distance) => 3f * distance / (Speed * ReverseSpeedScale);

        /// <summary>감속 시간. ease-out 곡선의 처음 기울기가 차수와 같아 처음 속도가 <see cref="Speed"/> 와 이어진다.</summary>
        public static float LandingDuration(float distance) => 3f * distance / Speed;

        /// <summary>1 - (1-u)^power. 차수가 클수록 끝에서 오래 천천히 움직인다.</summary>
        public static float EaseOut(float u, int power = 3) => 1 - (float)Math.Pow(1 - Clamp01(u), power);

        public static float EaseOutCubic(float u) => EaseOut(u, 3);

        /// <summary>끝을 지나쳤다가 돌아오는 곡선 (되돌아갈 때 반동). overshoot 가 클수록 더 튄다.</summary>
        public static float EaseOutBack(float u, float overshoot = 2.4f)
        {
            var c3 = overshoot + 1;
            var v = Clamp01(u) - 1;
            return 1 + c3 * v * v * v + overshoot * v * v;
        }

        /// <summary>버티는 동안 결과 쪽으로 끌려가는 정도(칸). t 는 멈춘 시간 비율 0~1.</summary>
        public static float Tug(float t)
        {
            var offset = 0f;
            foreach (var (at, size) in Tugs)
            {
                var d = (t - at) / TugWidth;
                offset += size * (float)Math.Exp(-d * d);
            }

            return offset;
        }

        /// <summary>
        /// 띠 뒤에 감속할 칸을 붙이고 (결과 칸, 처음 멈출 칸)을 돌려준다. from 보다 <see cref="LandingItems"/> 뒤에서 처음 멈춘다.
        /// 보통: 그 칸이 결과. 가짜 멈춤: 결과 다음 칸(미끼)에서 멈춘다. 역회전: 미끼에서 멈추고 결과는 <see cref="ReverseItems"/> 칸 앞(이미 지나간 칸)에 둔다.
        /// 멈춘 칸 위로 보일 칸도 몇 개 더 붙인다.
        /// </summary>
        public static (int Result, int Stop) Extend(List<string> strip, int from, string target, ReelEffect effect, Random random, IReadOnlyList<string> pool,
            ICollection<string> held = null)
        {
            var first = Math.Max(from + LandingItems, strip.Count);
            Fill(strip, first, random, pool, target);
            int result, stop;
            switch (effect)
            {
                case ReelEffect.FakeOut:
                    strip.Add(target);
                    strip.Add(Pick(random, pool, target, target));
                    result = first;
                    stop = first + 1;
                    break;
                case ReelEffect.Reverse:
                    strip.Add(Pick(random, pool, strip[strip.Count - 1], target));
                    stop = first;
                    // 지나간 칸에 결과를 심는다. 보이는 범위(가운데 ±2) 밖이라 바뀌는 게 안 보인다
                    result = Math.Max(stop - ReverseItems, 0);
                    strip[result] = target;
                    for (var k = Math.Max(result - 1, 0); k <= result + 1; k++)
                    {
                        if (k != result && strip[k] == target)
                        {
                            strip[k] = Pick(random, pool, target, k > 0 ? strip[k - 1] : target);
                        }
                    }

                    break;
                default:
                    strip.Add(target);
                    result = stop = first;
                    // 멈출 칸 바로 위(다음 칸)에는 다른 슬롯이 가진 특전을 두지 않는다 (겹쳐 보이지 않게)
                    strip.Add(PickFree(random, pool, target, held));
                    break;
            }

            Fill(strip, strip.Count + 3, random, pool, target);
            return (result, stop);
        }

        /// <summary>띠를 count 칸까지 채운다. 바로 앞 칸, avoid 와 겹치지 않게.</summary>
        public static void Fill(List<string> strip, int count, Random random, IReadOnlyList<string> pool, string avoid)
        {
            while (strip.Count < count)
            {
                strip.Add(Pick(random, pool, strip.Count > 0 ? strip[strip.Count - 1] : avoid, avoid));
            }
        }

        private static string PickFree(Random random, IReadOnlyList<string> pool, string notThis, ICollection<string> held)
        {
            var free = new List<string>();
            foreach (var perk in pool)
            {
                if (perk != notThis && (held == null || !held.Contains(perk)))
                {
                    free.Add(perk);
                }
            }

            return free.Count > 0 ? free[random.Next(free.Count)] : Pick(random, pool, notThis, notThis);
        }

        private static string Pick(Random random, IReadOnlyList<string> pool, string notA, string notB)
        {
            for (var tries = 0; tries < 32; tries++)
            {
                var perk = pool[random.Next(pool.Count)];
                if (perk != notA && perk != notB)
                {
                    return perk;
                }
            }

            foreach (var perk in pool)
            {
                if (perk != notA && perk != notB)
                {
                    return perk;
                }
            }

            return pool[0];
        }

        private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
