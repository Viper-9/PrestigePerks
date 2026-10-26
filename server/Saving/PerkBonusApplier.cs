using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Saving;

/// <summary>
/// 메모리의 PMC 프로필 Bonuses 에 특전 Bonus 를 넣고 뺀다. 파일에는 남기지 않는다 (<see cref="PerkProfileConverter"/>).
/// </summary>
public static class PerkBonusApplier
{
    /// <summary>특전 ID 를 모두 지우고 현재 특전의 Bonus 를 다시 넣는다. 여러 번 불러도 결과가 같다.</summary>
    /// <param name="scales">특전 ID → 효과 비율 (단계 III = 1, <see cref="PerkRules.ActiveScales"/>).</param>
    public static void Apply(PmcData pmc, IReadOnlyDictionary<string, double> scales)
    {
        var bonuses = CreateBonuses(scales);
        if (pmc.Bonuses is null)
        {
            // 빈 프로필(계정만 만든 상태)의 null 을 [] 로 바꾸지 않는다
            if (bonuses.Count > 0)
            {
                pmc.Bonuses = bonuses;
            }

            return;
        }

        pmc.Bonuses.RemoveAll(IsPerkBonus);
        pmc.Bonuses.AddRange(bonuses);
    }

    public static bool IsPerkBonus(Bonus bonus) => PerkCatalog.FilteredBonusIds.Contains(bonus.Id);

    /// <summary>특전 Bonus 를 뺀 새 목록. 원본은 바꾸지 않는다.</summary>
    public static List<Bonus> Strip(IEnumerable<Bonus> bonuses) => bonuses.Where(bonus => !IsPerkBonus(bonus)).ToList();

    /// <summary>단계 III (지금 수치) 그대로. 테스트와 개발용 비교에 쓴다.</summary>
    public static void Apply(PmcData pmc, IEnumerable<string> perks) => Apply(pmc, FullScale(perks));

    /// <summary>단계 III (지금 수치) 그대로.</summary>
    public static List<Bonus> CreateBonuses(IEnumerable<string> perks) => CreateBonuses(FullScale(perks));

    /// <summary>특전 Bonus. 값은 카탈로그 수치(III) × 효과 비율이다. 비율이 0 이하인 특전은 넣지 않는다.</summary>
    public static List<Bonus> CreateBonuses(IReadOnlyDictionary<string, double> scales)
    {
        var result = new List<Bonus>();
        foreach (var (id, scale) in scales)
        {
            if (!(scale > 0) || !PerkCatalog.TryGet(id, out var perk))
            {
                continue;
            }

            foreach (var bonus in perk.Bonuses)
            {
                // 바닐라 은신처 Bonus 와 같은 형식
                result.Add(new Bonus
                {
                    Id = new MongoId(bonus.Id),
                    Type = bonus.Type,
                    Value = bonus.Value * scale,
                    SkillType = bonus.SkillType,
                    IsPassive = true,
                    IsProduction = false,
                    IsVisible = true,
                });
            }
        }

        return result;
    }

    private static Dictionary<string, double> FullScale(IEnumerable<string> perks) => perks.Distinct().ToDictionary(perk => perk, _ => 1.0);
}
