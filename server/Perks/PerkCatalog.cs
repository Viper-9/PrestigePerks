using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Perks;

/// <summary>특전 효과 방식. Bonus 는 메모리의 PMC 프로필 Bonuses 에 넣고, Patch 는 클라이언트 패치(3차)로 적용한다.</summary>
public enum PerkKind
{
    Bonus,
    Patch,
}

/// <summary>Bonus 특전이 PMC 프로필에 넣는 Bonus 하나. Id 는 고정이며 저장 때 이 ID 로 걸러 낸다.</summary>
public sealed record PerkBonus(string Id, BonusType Type, double Value, BonusSkillType? SkillType = null);

/// <summary>특전 하나. Number 는 고정 ID 로 특전 데이터 파일에 저장된다. Key 는 언어 파일·표시용 이름(가칭).</summary>
public sealed record PerkDefinition(int Number, string Key, PerkKind Kind, IReadOnlyList<PerkBonus> Bonuses);

/// <summary>
/// 특전 20개 정의 (dev-docs 2_특전.md 3절, 6절).
/// 번호와 Bonus ID 는 한 번 정하면 바꾸거나 재사용하지 않는다. 테스트가 값을 고정한다.
/// </summary>
public static class PerkCatalog
{
    public static IReadOnlyList<PerkDefinition> All { get; } =
    [
        Bonus(1, "experience", new PerkBonus("8bcbeb65b78be907af6ee2b0", BonusType.ExperienceRate, 30)),
        Bonus(2, "conditioning", new PerkBonus("5dad7e290a1ca8cc07e58bb7", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Physical)),
        Bonus(3, "combat", new PerkBonus("f1360d70ed3bb18289339797", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Combat)),
        Bonus(4, "learning",
            new PerkBonus("26b6ac04721b710794837e2c", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Practical),
            new PerkBonus("24656f22d00ab74d124b8174", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Mental)),
        // 음수 값 타입은 음수일 때 은신처 관리 증폭이 걸린다. 반드시 음수로 넣는다
        Bonus(8, "scav", new PerkBonus("bfa913d120f3845fffd516e0", BonusType.ScavCooldownTimer, -30)),
        Patch(9, "porter"),
        Patch(10, "stamina"),
        Patch(11, "tough"), // 골절·출혈. 12번(출혈)을 합쳤다 (2026-10-19)
        Patch(14, "control"),
        Patch(15, "maintenance"), // 무기·방어구 내구도. 28번(방어구)을 합쳤다 (2026-10-19)
        Patch(16, "temperance"),
        Patch(17, "artisan"),
        Patch(24, "mastery"),
        Patch(25, "loading"),
        Patch(26, "search"),
        Patch(27, "surgeon"),
        Patch(29, "reload"),
        Patch(30, "handling"),
        Patch(31, "reliability"),
        Patch(32, "agility"),
    ];

    /// <summary>제외·통합한 번호. 다시 쓰지 않는다.</summary>
    public static IReadOnlyList<int> RetiredNumbers { get; } = [5, 6, 7, 12, 13, 18, 19, 20, 21, 22, 23, 28];

    /// <summary>삭제한 Bonus 특전의 ID. 예전 파일에 남아 있을 수 있어 저장·로드 때 계속 걸러 낸다.</summary>
    public static IReadOnlyList<string> RetiredBonusIds { get; } = [];

    /// <summary>프로필 Bonuses 에서 걸러 낼 ID 전체 (현재 + 삭제한 특전).</summary>
    public static IReadOnlySet<MongoId> FilteredBonusIds { get; } =
        All.SelectMany(perk => perk.Bonuses).Select(bonus => bonus.Id)
            .Concat(RetiredBonusIds)
            .Select(id => new MongoId(id))
            .ToHashSet();

    public static IReadOnlyList<int> Numbers { get; } = All.Select(perk => perk.Number).ToList();

    private static readonly Dictionary<int, PerkDefinition> ByNumber = All.ToDictionary(perk => perk.Number);

    public static bool TryGet(int number, out PerkDefinition perk) => ByNumber.TryGetValue(number, out perk!);

    public static bool IsKnown(int number) => ByNumber.ContainsKey(number);

    /// <summary>"mastery(24)" 형식. 개발용 명령 출력에 쓴다. EFT 채팅은 "#숫자" 를 해시태그로 색칠하다 태그가 깨지므로 # 을 쓰지 않는다.</summary>
    public static string Describe(int number) => TryGet(number, out var perk) ? $"{perk.Key}({number})" : $"?({number})";

    private static PerkDefinition Bonus(int number, string key, params PerkBonus[] bonuses) => new(number, key, PerkKind.Bonus, bonuses);

    private static PerkDefinition Patch(int number, string key) => new(number, key, PerkKind.Patch, []);
}
