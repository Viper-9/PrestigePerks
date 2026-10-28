using System.Globalization;
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

/// <summary>화면 단위. Points 는 게임 보너스 합에 더해지는 % (한국어 %p), Percent 는 게임 결과에 곱하는 %, Flat 은 단위 없는 고정값.</summary>
public enum PerkUnit
{
    Percent,
    Points,
    Flat,
}

/// <summary>add-on 특전 문구 한 언어 (perks.json texts). 빈 값은 null.</summary>
public sealed record PerkTexts(string? Name, string? Effect, string? Detail);

/// <summary>add-on 특전의 출처(perks/ 아래 폴더 이름), 언어 코드 → 문구, 아이콘 경로(add-on 폴더 기준, 검사 전).</summary>
public sealed record PerkAddonInfo(string Source, IReadOnlyDictionary<string, PerkTexts> Texts, string? IconPath);

/// <summary>
/// 특전 하나. Id 는 특전 데이터 파일(schema 4)에 저장되고 클라이언트와 주고받는 영구 ID 이자 언어 파일·아이콘 이름이다.
/// Number 는 schema 3 까지의 파일을 옮길 때와 개발용 명령 입력에만 쓴다.
/// Amount 는 단계 III 효과량(문구 방향의 부호 포함, % 는 숫자만)이다. 화면 수치이고, Bonus 특전은 Bonus 값과 같다.
/// add-on 특전(dev-docs 95)은 Number 0 이고 Addon 에 출처·문구·아이콘 경로가 있다.
/// </summary>
public sealed record PerkDefinition(int Number, string Id, PerkKind Kind, double Amount, PerkUnit Unit, IReadOnlyList<PerkBonus> Bonuses, PerkAddonInfo? Addon = null);

/// <summary>
/// 특전 정의: 내장 20개 (dev-docs 2_특전.md 3절, 6절) + 서버 시작 때 설치한 add-on 특전 (dev-docs 95, <see cref="Install"/>).
/// 번호·ID·Bonus ID 는 한 번 정하면 바꾸거나 재사용하지 않는다. 테스트가 값을 고정한다.
/// 번호(<see cref="Numbers"/>, <see cref="TryGet(int, out PerkDefinition)"/>, <see cref="IdOf"/>)는 내장만 본다. ID 로 찾는 것은 설치한 add-on 까지 본다.
/// </summary>
public static class PerkCatalog
{
    /// <summary>내장 특전 20개. 바뀌지 않는다.</summary>
    public static IReadOnlyList<PerkDefinition> Builtin { get; } =
    [
        Bonus(1, "experience", PerkUnit.Points, new PerkBonus("8bcbeb65b78be907af6ee2b0", BonusType.ExperienceRate, 30)),
        Bonus(2, "conditioning", PerkUnit.Points, new PerkBonus("5dad7e290a1ca8cc07e58bb7", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Physical)),
        Bonus(3, "combat", PerkUnit.Points, new PerkBonus("f1360d70ed3bb18289339797", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Combat)),
        Bonus(4, "learning", PerkUnit.Points,
            new PerkBonus("26b6ac04721b710794837e2c", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Practical),
            new PerkBonus("24656f22d00ab74d124b8174", BonusType.SkillGroupLevelingBoost, 30, BonusSkillType.Mental)),
        // 음수 값 타입은 음수일 때 은신처 관리 증폭이 걸린다. 반드시 음수로 넣는다
        Bonus(8, "scav", PerkUnit.Points, new PerkBonus("bfa913d120f3845fffd516e0", BonusType.ScavCooldownTimer, -30)),
        Patch(9, "porter", 20, PerkUnit.Points),       // 소지 무게 한도
        Patch(10, "stamina", 15, PerkUnit.Points),     // 스태미나
        Patch(11, "tough", -20, PerkUnit.Percent),     // 골절·출혈. 12번(출혈)을 합쳤다 (2026-10-19)
        Patch(14, "control", -10, PerkUnit.Points),    // 반동
        Patch(15, "maintenance", -20, PerkUnit.Percent), // 무기·방어구 내구도. 28번(방어구)을 합쳤다 (2026-10-19)
        Patch(16, "temperance", -15, PerkUnit.Percent),  // 에너지·수분 소모
        Patch(17, "artisan", -20, PerkUnit.Percent),   // 은신처 생산 시간
        Patch(24, "mastery", 30, PerkUnit.Percent),    // 무기 숙련도 성장
        Patch(25, "loading", -20, PerkUnit.Percent),   // 삽탄·탄 빼기 시간
        Patch(26, "search", 30, PerkUnit.Percent),     // 수색 속도
        Patch(27, "surgeon", 25, PerkUnit.Points),     // 수술 속도
        Patch(29, "reload", 15, PerkUnit.Percent),     // 탄창 교체 속도
        Patch(30, "handling", 10, PerkUnit.Flat),      // 무기 인체공학 (고정값)
        Patch(31, "reliability", -20, PerkUnit.Percent), // 고장·과열
        Patch(32, "agility", 5, PerkUnit.Percent),     // 이동 속도
    ];
    /// <summary>제외·통합한 번호. 다시 쓰지 않는다.</summary>
    public static IReadOnlyList<int> RetiredNumbers { get; } = [5, 6, 7, 12, 13, 18, 19, 20, 21, 22, 23, 28];

    /// <summary>삭제한 Bonus 특전의 ID. 예전 파일에 남아 있을 수 있어 저장·로드 때 계속 걸러 낸다.</summary>
    public static IReadOnlyList<string> RetiredBonusIds { get; } = [];

    public static IReadOnlyList<int> Numbers { get; } = Builtin.Select(perk => perk.Number).ToList();

    private static readonly Dictionary<int, PerkDefinition> ByNumber = Builtin.ToDictionary(perk => perk.Number);

    // 설치 때 한 번에 바꾼다 (읽는 쪽이 반쯤 바뀐 표를 보지 않게). 서버 시작(Preload + 10)에 한 번이고, 그 뒤로는 읽기만 한다
    private sealed record Tables(
        IReadOnlyList<PerkDefinition> All,
        IReadOnlyList<PerkDefinition> Addons,
        IReadOnlyList<string> Ids,
        IReadOnlyList<PerkCatalogEntry> Entries,
        IReadOnlyDictionary<string, PerkDefinition> ById,
        IReadOnlySet<MongoId> FilteredBonusIds,
        IReadOnlyDictionary<string, byte[]> Icons);

    // Builtin·RetiredBonusIds 뒤에 둔다 (정적 초기화 순서)
    private static Tables tables = Build([], new Dictionary<string, byte[]>());

    /// <summary>내장 + 설치한 add-on 특전 (목록 순서).</summary>
    public static IReadOnlyList<PerkDefinition> All => tables.All;

    /// <summary>설치한 add-on 특전 (perks/ 폴더 이름 순).</summary>
    public static IReadOnlyList<PerkDefinition> Addons => tables.Addons;

    /// <summary>프로필 Bonuses 에서 걸러 낼 ID 전체 (내장 + 삭제한 특전 + 설치한 add-on).</summary>
    public static IReadOnlySet<MongoId> FilteredBonusIds => tables.FilteredBonusIds;

    public static IReadOnlyList<string> Ids => tables.Ids;

    /// <summary>state 의 catalog. 클라이언트는 이 순서로 목록을 그리고 이 값으로 수치를 보인다. add-on 특전만 kind·texts·icon 이 실린다.</summary>
    public static IReadOnlyList<PerkCatalogEntry> Entries => tables.Entries;

    /// <summary>설치한 add-on 아이콘 (특전 ID → PNG). /prestigeperks/icons 가 보낸다.</summary>
    public static IReadOnlyDictionary<string, byte[]> Icons => tables.Icons;

    /// <summary>
    /// add-on 특전을 내장 뒤에 설치한다 (앞서 설치한 것은 바꾼다, 빈 목록이면 내장만으로 되돌린다). 내장 ID, 앞선 특전의 ID·Bonus ID 와 겹치는 것은 빼고
    /// 그 경고를 돌려준다. 아이콘은 비운다 (<see cref="InstallIcons"/> 로 따로 넣는다).
    /// </summary>
    public static IReadOnlyList<string> Install(IReadOnlyList<PerkDefinition> addons)
    {
        var warnings = new List<string>();
        var ids = Builtin.Select(perk => perk.Id).ToHashSet(StringComparer.Ordinal);
        var bonusIds = Builtin.SelectMany(perk => perk.Bonuses).Select(bonus => bonus.Id).Concat(RetiredBonusIds).ToHashSet(StringComparer.Ordinal);
        var accepted = new List<PerkDefinition>();
        foreach (var perk in addons)
        {
            var source = perk.Addon?.Source ?? "?";
            if (ids.Contains(perk.Id))
            {
                warnings.Add($"perks/{source}: {perk.Id}: 내장 특전이나 앞선 add-on 과 ID 가 겹칩니다. 이 특전은 건너뜁니다");
                continue;
            }

            var clash = perk.Bonuses.FirstOrDefault(bonus => bonusIds.Contains(bonus.Id));
            if (clash is not null)
            {
                warnings.Add($"perks/{source}: {perk.Id}: Bonus ID {clash.Id} 가 다른 특전과 겹칩니다. 이 특전은 건너뜁니다");
                continue;
            }

            ids.Add(perk.Id);
            bonusIds.UnionWith(perk.Bonuses.Select(bonus => bonus.Id));
            accepted.Add(perk);
        }

        tables = Build(accepted, new Dictionary<string, byte[]>());
        return warnings;
    }

    /// <summary>add-on 아이콘을 넣는다. 설치한 add-on 특전의 것만 남긴다 (catalog 의 icon 도 따라 바뀐다).</summary>
    public static void InstallIcons(IReadOnlyDictionary<string, byte[]> icons) => tables = Build(tables.Addons, icons);

    private static Tables Build(IReadOnlyList<PerkDefinition> addons, IReadOnlyDictionary<string, byte[]> icons)
    {
        var all = Builtin.Concat(addons).ToList();
        var addonIds = addons.Select(perk => perk.Id).ToHashSet(StringComparer.Ordinal);
        var kept = icons.Where(pair => addonIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return new Tables(
            all,
            addons.ToList(),
            all.Select(perk => perk.Id).ToList(),
            all.Select(perk => Entry(perk, kept)).ToList(),
            all.ToDictionary(perk => perk.Id),
            all.SelectMany(perk => perk.Bonuses).Select(bonus => bonus.Id)
                .Concat(RetiredBonusIds)
                .Select(id => new MongoId(id))
                .ToHashSet(),
            kept);
    }

    private static PerkCatalogEntry Entry(PerkDefinition perk, IReadOnlyDictionary<string, byte[]> icons)
    {
        var texts = perk.Addon?.Texts
            .Where(pair => pair.Value.Name is not null || pair.Value.Effect is not null || pair.Value.Detail is not null)
            .ToDictionary(pair => pair.Key, pair => new PerkTextsEntry { Name = pair.Value.Name, Effect = pair.Value.Effect, Detail = pair.Value.Detail });
        return new PerkCatalogEntry
        {
            Id = perk.Id,
            Amount = perk.Amount,
            Unit = perk.Unit switch
            {
                PerkUnit.Points => PerkCatalogEntry.Points,
                PerkUnit.Flat => PerkCatalogEntry.Flat,
                _ => PerkCatalogEntry.Percent,
            },
            // 내장 특전은 지금 JSON 그대로 (새 필드 없음)
            Kind = perk.Addon is null ? null : perk.Kind == PerkKind.Patch ? PerkCatalogEntry.CodeKind : PerkCatalogEntry.BonusKind,
            Texts = texts is { Count: > 0 } ? texts : null,
            Icon = icons.ContainsKey(perk.Id),
        };
    }

    public static bool TryGet(int number, out PerkDefinition perk) => ByNumber.TryGetValue(number, out perk!);

    public static bool TryGet(string? id, out PerkDefinition perk)
    {
        perk = null!;
        return id is not null && tables.ById.TryGetValue(id, out perk!);
    }

    public static bool IsKnown(int number) => ByNumber.ContainsKey(number);

    public static bool IsKnown(string? id) => id is not null && tables.ById.ContainsKey(id);

    /// <summary>번호의 ID. 모르는 번호(빈 칸 0, 쓰지 않는 번호)면 null.</summary>
    public static string? IdOf(int number) => TryGet(number, out var perk) ? perk.Id : null;

    /// <summary>ID 의 번호. 빈 칸이거나 모르는 특전(add-on 을 뺀 경우)이면 null. add-on 특전은 0.</summary>
    public static int? NumberOf(string? id) => TryGet(id, out var perk) ? perk.Number : null;

    /// <summary>카탈로그에 있는 특전만 (빈 칸, add-on 을 뺀 특전은 빠진다). state 에는 클라이언트가 그릴 수 있는 특전만 보낸다.</summary>
    public static IReadOnlyList<string> KnownOnly(IEnumerable<string?> ids) => ids.Where(IsKnown).Select(id => id!).ToList();

    /// <summary>특전 → 단계. 모르는 특전은 빠진다 (도감 수에 넣지 않는다).</summary>
    public static IReadOnlyDictionary<string, int> KnownOnly(IReadOnlyDictionary<string, int> tiers) =>
        tiers.Where(pair => IsKnown(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>개발용 명령 입력: 번호("24") 또는 ID("mastery", "viper.medic", 대소문자 무시). 카탈로그에 없으면 null.</summary>
    public static string? Resolve(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return IdOf(number);
        }

        var id = token.Trim().ToLowerInvariant();
        return IsKnown(id) ? id : null;
    }

    /// <summary>"mastery(24)" 형식. 개발용 명령 출력에 쓴다. EFT 채팅은 "#숫자" 를 해시태그로 색칠하다 태그가 깨지므로 # 을 쓰지 않는다.</summary>
    public static string Describe(int number) => TryGet(number, out var perk) ? $"{perk.Id}({number})" : $"?({number})";

    /// <summary>"mastery(24)" 형식 (로그용). 빈 칸은 "empty", 모르는 특전과 add-on 특전(번호 없음)은 ID 그대로.</summary>
    public static string Describe(string? id) => TryGet(id, out var perk) && perk.Number != 0 ? $"{perk.Id}({perk.Number})" : id ?? "empty";

    // Bonus 특전의 화면 수치는 첫 Bonus 값이다 (4번처럼 여러 개면 모두 같은 값, 테스트가 확인)
    private static PerkDefinition Bonus(int number, string id, PerkUnit unit, params PerkBonus[] bonuses) =>
        new(number, id, PerkKind.Bonus, bonuses[0].Value, unit, bonuses);

    private static PerkDefinition Patch(int number, string id, double amount, PerkUnit unit) => new(number, id, PerkKind.Patch, amount, unit, []);
}
