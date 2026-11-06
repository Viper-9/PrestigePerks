using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrestigePerks.Game;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 액티브 특전 로그. 게임 화면을 못 봐도 BepInEx 로그만으로 판단할 수 있게 (dev-docs 93 Global Constraints, 시험 플러그인 LabLog 와 같은 방식).
    /// 늘 남기는 줄은 레이드마다 몇 줄 ([active] 시작 퍽·단계, 누름 결과, 끝). CHECK·SUMMARY·MARK 는 개발자 모드일 때만.
    /// </summary>
    internal static class ActivePerkLog
    {
        private sealed class CheckStat
        {
            public int Ok, Fail, Skip;
        }

        private static readonly Dictionary<string, CheckStat> checks = new Dictionary<string, CheckStat>();
        private static readonly Dictionary<string, int> errors = new Dictionary<string, int>();
        private static string perk;
        private static int tier, presses, mark;
        private static string stamp;
        private static float startedAt = -1;

        public static void Event(string message) => Log.Info($"[active] {message}");

        /// <summary>자동 판정 한 줄 ([active][CHECK] OK|FAIL 이름: 내용). 같은 이름이 여러 번이면 SUMMARY 에서 합친다.</summary>
        public static void Check(string name, bool ok, string detail)
        {
            if (!PerkEffects.DevMode)
            {
                return;
            }

            var stat = Stat(name);
            if (ok)
            {
                stat.Ok++;
            }
            else
            {
                stat.Fail++;
            }

            var line = $"[active][CHECK] {(ok ? "OK  " : "FAIL")} {name}: {detail}";
            if (ok)
            {
                Log.Info(line);
            }
            else
            {
                Log.Warn(line);
            }
        }

        /// <summary>판정할 거리가 없었다 ([active][CHECK] SKIP 이름: 이유). 예: 투혼 지속 중 한 대도 안 맞음. OK 로 세지 않는다.</summary>
        public static void Skip(string name, string reason)
        {
            if (!PerkEffects.DevMode)
            {
                return;
            }

            Stat(name).Skip++;
            Log.Info($"[active][CHECK] SKIP {name}: {reason}");
        }

        private static CheckStat Stat(string name)
        {
            if (!checks.TryGetValue(name, out var stat))
            {
                checks[name] = stat = new CheckStat();
            }

            return stat;
        }

        public static void Pressed() => presses++;

        /// <summary>예외: 같은 내용은 처음 한 번만 전체를 남기고 이후는 횟수만 센다.</summary>
        public static void Error(string context, Exception e)
        {
            var key = $"{context}|{e.GetType().Name}|{e.Message}";
            errors.TryGetValue(key, out var count);
            errors[key] = ++count;
            if (count == 1)
            {
                Log.Error($"[active] {context}: {e}");
            }
        }

        /// <summary>표시 키: 번호 + 스크린샷 + 그 순간 상태. 사용자 말·사진과 로그를 맞춘다.</summary>
        public static void Mark(string state)
        {
            if (!PerkEffects.DevMode)
            {
                return;
            }

            mark++;
            string shot = null;
            try
            {
                var folder = Path.Combine(BepInEx.Paths.BepInExRootPath, "PrestigePerks", "marks", stamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(folder);
                shot = Path.Combine(folder, $"mark_{mark:00}.png");
                ScreenCapture.CaptureScreenshot(shot);
            }
            catch (Exception e)
            {
                Error("mark screenshot", e);
            }

            Log.Info($"[active][MARK] #{mark} {shot ?? "screenshot failed"} {state}");
        }

        public static void RaidStarted(string startedPerk, int startedTier)
        {
            checks.Clear();
            errors.Clear();
            perk = startedPerk;
            tier = startedTier;
            presses = 0;
            mark = 0;
            stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            startedAt = Time.time;
            Event($"raid start: {perk} T{tier}");
        }

        /// <summary>개발자 모드 초기화 키로 시험 퍽을 바꿨다. 레이드 끝 줄에 거친 퍽을 차례로 적는다 (CHECK 수는 레이드 전체를 합친다).</summary>
        public static void Switched(string nextPerk, int nextTier)
        {
            perk = $"{perk} T{tier} > {nextPerk}";
            tier = nextTier;
        }

        /// <summary>레이드 끝·사망. extra 는 효과가 덧붙이는 요약 (음파 탐지의 파동·잔상 수).</summary>
        public static void RaidEnded(string why, string extra)
        {
            if (startedAt < 0)
            {
                return;
            }

            Event($"raid end ({why}): {perk} T{tier}, {Time.time - startedAt:F0} s, presses {presses}");
            startedAt = -1;
            if (!PerkEffects.DevMode)
            {
                return;
            }

            var ok = checks.Values.Sum(stat => stat.Ok);
            var fail = checks.Values.Sum(stat => stat.Fail);
            var skip = checks.Values.Sum(stat => stat.Skip);
            var failed = checks.Where(pair => pair.Value.Fail > 0).Select(pair => $"{pair.Key} x{pair.Value.Fail}").ToList();
            var skipped = checks.Where(pair => pair.Value.Skip > 0).Select(pair => $"{pair.Key} x{pair.Value.Skip}").ToList();
            var line = $"[active][SUMMARY] {perk} T{tier}: checks ok {ok} / fail {fail} / skip {skip}, presses {presses}, errors {errors.Values.Sum()}"
                + (failed.Count > 0 ? $" | FAIL {string.Join(", ", failed)}" : "")
                + (skipped.Count > 0 ? $" | SKIP {string.Join(", ", skipped)}" : "")
                + (string.IsNullOrEmpty(extra) ? "" : $" | {extra}");
            if (fail > 0 || errors.Count > 0)
            {
                Log.Warn(line);
            }
            else
            {
                Log.Info(line);
            }
        }
    }
}
