using PrestigePerks.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Perks;

/// <summary>자극제 버프 한 줄 (globals Stimulator.Buffs 의 항목과 같은 뜻). 테스트할 수 있게 SPT 형식과 떼어 둔다.</summary>
public sealed record ActiveBuffLine(string BuffType, double Value, bool AbsoluteValue, double Duration);

/// <summary>
/// 응급 처치(41)의 자극제 버프를 globals 에 넣는다 (실현성조사 2.2, 3.1). 클라이언트가 아이템 없이 ActiveHealthController.DoExternalBuff(이름) 으로 건다.
/// 값은 카탈로그에서 읽어 서버 한 곳에서만 관리한다. 설정(activePerks.enabled)과 상관없이 늘 넣는다 (쓰지 않으면 해가 없다).
/// 체력 재생(HealthRate)과 새 출혈 막기(RemoveAllBloodLosses, Zagustin 과 같은 동작)가 같은 시간 동안 걸린다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class ActivePerkBuffs(ISptLogger<PerksFileLogger> logger, GlobalTable globalTable) : IOnLoad
{
    /// <summary>클라이언트 응급 처치 효과가 같은 이름을 쓴다.</summary>
    public static string FirstAidBuffName(int tier) => $"PrestigePerks_FirstAid_{tier}";

    internal static IReadOnlyDictionary<string, IReadOnlyList<ActiveBuffLine>> Build()
    {
        var spec = PerkCatalog.BuiltinActive.Single(perk => perk.Id == "firstaid").Active!;
        var buffs = new Dictionary<string, IReadOnlyList<ActiveBuffLine>>();
        for (var tier = 1; tier <= PerkConfig.MaxTier; tier++)
        {
            buffs[FirstAidBuffName(tier)] =
            [
                new ActiveBuffLine("HealthRate", spec.Values["regen"][tier - 1], true, spec.Duration[tier - 1]),
                new ActiveBuffLine("RemoveAllBloodLosses", 0, true, spec.Values["bloodstop"][tier - 1]),
            ];
        }

        return buffs;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var table = globalTable.Configuration.Health?.Effects?.Stimulator?.Buffs;
        if (table is null)
        {
            logger.Error("[PrestigePerks] globals has no Health.Effects.Stimulator.Buffs. First Aid (active perk) will have no regeneration buff");
            return Task.CompletedTask;
        }

        var buffs = Build();
        foreach (var (name, lines) in buffs)
        {
            // 게임 globals 의 버프와 같은 모양 (Zagustin 등): Chance 1, Delay 0, 스킬 아닌 버프는 SkillName ""
            table[name] = lines.Select(line => new Buff
            {
                BuffType = line.BuffType,
                Value = line.Value,
                AbsoluteValue = line.AbsoluteValue,
                Duration = line.Duration,
                Chance = 1,
                Delay = 0,
                SkillName = "",
            }).ToList();
        }

        logger.Info($"[PrestigePerks] active perk buffs added to globals: {string.Join(", ", buffs.Keys)}");
        return Task.CompletedTask;
    }
}
