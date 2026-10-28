using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Perks;

/// <summary>
/// add-on 특전 파일(perks/&lt;add-on&gt;/perks.json) 해석·검사 (dev-docs 95 "add-on 파일 형식").
/// 잘못된 특전은 warn 으로 알리고 건너뛴다. 파일 입출력과 아이콘 파일 검사는 PerkAddonLoader 가 한다.
/// </summary>
public static class PerkAddonFile
{
    public const int MaxIdLength = 64, MaxBonuses = 4;
    public const double MaxValue = 100;

    private static readonly Regex IdPattern = new("^[a-z0-9_-]+\\.[a-z0-9_.-]+$", RegexOptions.CultureInvariant);

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// add-on 이 쓸 수 있는 BonusType. 서버·클라이언트가 pmcData.Bonuses 의 합을 읽는 곳을 디컴파일로 확인한 타입만 (dev-docs 95 "허용 BonusType").
    /// 창고 크기처럼 구조를 바꾸는 타입은 add-on 을 빼면 아이템이 사라질 수 있어 넣지 않는다.
    /// </summary>
    public static IReadOnlySet<BonusType> AllowedBonusTypes { get; } = new HashSet<BonusType>
    {
        BonusType.ExperienceRate,
        BonusType.SkillGroupLevelingBoost,
        BonusType.ScavCooldownTimer,
        BonusType.HealthRegeneration,
        BonusType.EnergyRegeneration,
        BonusType.HydrationRegeneration,
        BonusType.QuestMoneyReward,
        BonusType.RagfairCommission,
        BonusType.InsuranceReturnTime,
        BonusType.FuelConsumption,
        BonusType.RepairWeaponBonus,
        BonusType.RepairArmorBonus,
    };

    /// <summary>add-on 특전 ID: 점이 든 소문자 "author.perk" (내장 ID 는 점이 없어 겹치지 않는다), 64자 이하.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= MaxIdLength } && IdPattern.IsMatch(id) && !id.EndsWith('.');

    /// <summary>특전 ID·순번의 고정 Bonus ID. MD5 앞 12바이트 (24자리 hex). 바뀌면 메모리에 넣은 Bonus 를 못 거르므로 식을 바꾸지 않는다.</summary>
    public static string BonusId(string perkId, int index) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"PrestigePerks/{perkId}/{index.ToString(CultureInfo.InvariantCulture)}")))[..24];

    /// <summary>perks.json 내용을 읽는다. 파일이 깨졌으면 경고 하나와 빈 목록, 특전 하나가 잘못됐으면 경고하고 그 특전만 건너뛴다.</summary>
    public static IReadOnlyList<PerkDefinition> Parse(string json, string source, Action<string> warn)
    {
        var file = $"perks/{source}/perks.json";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException error)
        {
            warn($"{file}: JSON 오류 ({error.Message}). 이 add-on 은 건너뜁니다");
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("perks", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                warn($"{file}: \"perks\" 목록이 없습니다. 이 add-on 은 건너뜁니다");
                return [];
            }

            var result = new List<PerkDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                index++;
                var label = item.ValueKind == JsonValueKind.Object && String(item, "id") is { } named ? named : $"{index}번째 특전";
                var perk = ParsePerk(item, source, message => warn($"{file}: {label}: {message}"), out var reason);
                if (perk is null)
                {
                    warn($"{file}: {label}: {reason}. 이 특전은 건너뜁니다");
                }
                else if (!seen.Add(perk.Id))
                {
                    warn($"{file}: {label}: 같은 파일에 같은 ID 가 이미 있습니다. 이 특전은 건너뜁니다");
                }
                else
                {
                    result.Add(perk);
                }
            }

            return result;
        }
    }

    // 잘못됐으면 null 과 이유. 특전은 살리고 일부만 빼는 것(amount 무시, 효과 문구)은 note 로 알린다
    private static PerkDefinition? ParsePerk(JsonElement item, string source, Action<string> note, out string reason)
    {
        reason = "";
        if (item.ValueKind != JsonValueKind.Object)
        {
            reason = "특전이 객체가 아닙니다";
            return null;
        }

        var id = String(item, "id");
        if (!IsValidId(id))
        {
            reason = "id 는 점이 든 소문자 \"author.perk\" (64자 이하) 여야 합니다";
            return null;
        }

        var kind = String(item, "kind") switch
        {
            "bonus" => PerkKind.Bonus,
            "code" => PerkKind.Patch,
            _ => (PerkKind?)null,
        };
        if (kind is null)
        {
            reason = "모르는 kind (\"bonus\" 또는 \"code\")";
            return null;
        }

        var unit = String(item, "unit") switch
        {
            "percent" => PerkUnit.Percent,
            "points" => PerkUnit.Points,
            "flat" => PerkUnit.Flat,
            _ => (PerkUnit?)null,
        };
        if (unit is null)
        {
            reason = "모르는 unit (\"percent\", \"points\", \"flat\")";
            return null;
        }

        var hasBonuses = item.TryGetProperty("bonuses", out var bonusList);
        var hasAmount = item.TryGetProperty("amount", out var amountValue);
        IReadOnlyList<PerkBonus> bonuses;
        double amount;
        if (kind == PerkKind.Bonus)
        {
            var parsed = ParseBonuses(id!, hasBonuses ? bonusList : default, out reason);
            if (parsed is null)
            {
                return null;
            }

            if (hasAmount)
            {
                note("bonus 특전의 amount 는 쓰지 않습니다 (화면 수치는 첫 Bonus 값). 무시합니다");
            }

            bonuses = parsed;
            amount = parsed[0].Value;
        }
        else
        {
            if (hasBonuses)
            {
                reason = "code 특전에는 bonuses 를 쓸 수 없습니다";
                return null;
            }

            if (!hasAmount || !TryValue(amountValue, out amount))
            {
                reason = $"code 특전의 amount 는 0 이 아니고 절댓값 {MaxValue} 이하인 숫자여야 합니다";
                return null;
            }

            bonuses = [];
        }

        var icon = String(item, "icon");
        return new PerkDefinition(0, id!, kind.Value, amount, unit.Value, bonuses,
            new PerkAddonInfo(source, ParseTexts(item, note), string.IsNullOrWhiteSpace(icon) ? null : icon));
    }

    private static List<PerkBonus>? ParseBonuses(string id, JsonElement list, out string reason)
    {
        reason = "";
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxBonuses)
        {
            reason = $"bonus 특전의 bonuses 는 1~{MaxBonuses}개여야 합니다";
            return null;
        }

        var result = new List<PerkBonus>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !TryEnum<BonusType>(String(entry, "type"), out var type) || !AllowedBonusTypes.Contains(type))
            {
                reason = $"허용하지 않는 Bonus type ({String(entry, "type") ?? "없음"})";
                return null;
            }

            var skillName = String(entry, "skillType");
            BonusSkillType? skill = null;
            if (type == BonusType.SkillGroupLevelingBoost)
            {
                if (!TryEnum<BonusSkillType>(skillName, out var parsed))
                {
                    reason = $"SkillGroupLevelingBoost 에는 skillType 이 필요합니다 ({skillName ?? "없음"})";
                    return null;
                }

                skill = parsed;
            }
            else if (entry.TryGetProperty("skillType", out _))
            {
                reason = $"{type} 에는 skillType 을 쓸 수 없습니다";
                return null;
            }

            if (!entry.TryGetProperty("value", out var valueElement) || !TryValue(valueElement, out var value))
            {
                reason = $"Bonus value 는 0 이 아니고 절댓값 {MaxValue} 이하인 숫자여야 합니다";
                return null;
            }

            // 화면 수치는 첫 값 하나라 값이 다르면 보이는 것과 실제가 달라진다
            if (result.Count > 0 && value != result[0].Value)
            {
                reason = "Bonus value 가 서로 다릅니다 (모두 같아야 합니다)";
                return null;
            }

            result.Add(new PerkBonus(BonusId(id, result.Count), type, value, skill));
        }

        return result;
    }

    // 언어 코드 → 문구. 효과 문구에 수치 자리 {0} 이 없으면 그 줄만 뺀다
    private static Dictionary<string, PerkTexts> ParseTexts(JsonElement item, Action<string> note)
    {
        var result = new Dictionary<string, PerkTexts>(StringComparer.Ordinal);
        if (!item.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var language in texts.EnumerateObject())
        {
            if (language.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var effect = Text(language.Value, "effect");
            if (effect is not null && !effect.Contains("{0}", StringComparison.Ordinal))
            {
                note($"texts.{language.Name}.effect 에 수치 자리 {{0}} 이 없습니다. 그 문구는 뺍니다");
                effect = null;
            }

            result[language.Name] = new PerkTexts(Text(language.Value, "name"), effect, Text(language.Value, "detail"));
        }

        return result;
    }

    private static string? String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Text(JsonElement item, string name) => String(item, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool TryValue(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value)
            && double.IsFinite(value) && value != 0 && Math.Abs(value) <= MaxValue;
    }

    // 정해진 이름 그대로만 (대소문자 구분, 숫자·쉼표 조합은 받지 않는다)
    private static bool TryEnum<T>(string? name, out T value)
        where T : struct, Enum
    {
        value = default;
        return name is not null && Enum.GetNames<T>().Contains(name, StringComparer.Ordinal) && Enum.TryParse(name, false, out value);
    }
}
