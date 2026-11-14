using System.Globalization;
using System.Text.Json;

namespace PrestigePerks.Perks;

/// <summary>config perkAmounts 를 읽은 내장 특전 수치. Changed 는 기본값과 다른 특전 (로그용 "agility [25, 37.5, 50]").</summary>
public sealed record PerkAmountResult(IReadOnlyList<PerkDefinition> Builtin, IReadOnlyList<PerkDefinition> BuiltinActive, IReadOnlyList<string> Changed);

/// <summary>
/// config perkAmounts (특전 ID → 단계 I·II·III 수치)를 내장 특전 정의에 넣는다. 서버 시작 때 한 번 (<see cref="PerkConfigLoader.ApplyPerkAmounts"/>).
/// 패시브는 [I, II, III] (화면 수치 그대로, 부호 포함), 액티브는 { "duration": [..], "radius": [..] } 처럼 기본 정의에 있는 이름만 받는다.
/// 잘못된 값은 그 특전(액티브는 그 이름)만 기본값으로 두고 경고한다. add-on 특전 수치는 add-on 의 perks.json 이 정한다.
/// </summary>
public static class PerkAmountConfig
{
    public const string CooldownName = "cooldown", DurationName = "duration";

    /// <summary>액티브 값 중 % 인 것 (클라이언트가 100 으로 나눠 쓴다). 100 을 넘으면 받지 않는다.</summary>
    public static IReadOnlySet<string> PercentValues { get; } = new HashSet<string>(StringComparer.Ordinal) { "reduction", "inertia", "head", "restore", "fill", "repair" };

    public static PerkAmountResult Resolve(IReadOnlyDictionary<string, JsonElement>? amounts, Action<string> warn)
    {
        amounts ??= new Dictionary<string, JsonElement>();
        var known = PerkCatalog.DefaultBuiltin.Concat(PerkCatalog.DefaultBuiltinActive).Select(perk => perk.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in amounts.Keys.Where(id => !known.Contains(id)))
        {
            warn(id.Contains('.')
                ? $"perkAmounts.{id}: add-on 특전 수치는 add-on 의 perks.json 에서 정합니다. 무시합니다"
                : $"perkAmounts.{id}: 모르는 특전 ID 라 무시합니다");
        }

        var changed = new List<string>();
        var builtin = new List<PerkDefinition>();
        foreach (var perk in PerkCatalog.DefaultBuiltin)
        {
            if (!amounts.TryGetValue(perk.Id, out var element))
            {
                builtin.Add(perk);
                continue;
            }

            var tiers = ReadTiers(element);
            var error = tiers is null ? "숫자 3개 [I, II, III] 여야 합니다" : PassiveError(perk, tiers);
            if (error is not null)
            {
                warn($"perkAmounts.{perk.Id}: {error}. 기본값 {Describe(perk.TierAmounts)} 을 씁니다");
                builtin.Add(perk);
                continue;
            }

            var configured = WithTiers(perk, tiers!);
            builtin.Add(configured);
            if (!configured.TierAmounts.SequenceEqual(perk.TierAmounts))
            {
                changed.Add($"{perk.Id} {Describe(tiers!)}");
            }
        }

        var active = new List<PerkDefinition>();
        foreach (var perk in PerkCatalog.DefaultBuiltinActive)
        {
            if (!amounts.TryGetValue(perk.Id, out var element))
            {
                active.Add(perk);
                continue;
            }

            active.Add(WithActiveValues(perk, element, warn, changed));
        }

        return new PerkAmountResult(builtin, active, changed);
    }

    /// <summary>
    /// 내장 패시브 수치 검사. 잘못됐으면 이유, 정상이면 null.
    /// 0 이 아니고 기본값과 부호가 같으며 (방향이 뒤집히지 않게) 단계가 오를수록 약해지지 않고, 감소는 −100 보다 커야 한다 (배수가 0 이하가 되지 않게).
    /// </summary>
    public static string? PassiveError(PerkDefinition perk, IReadOnlyList<double> tiers)
    {
        if (tiers.Count != PerkConfig.MaxTier || tiers.Any(value => !double.IsFinite(value) || value == 0))
        {
            return "0 이 아닌 숫자 3개 [I, II, III] 여야 합니다";
        }

        if (tiers.Any(value => Math.Sign(value) != Math.Sign(perk.Amount)))
        {
            return perk.Amount > 0 ? "이 특전은 양수여야 합니다 (음수면 효과 방향이 뒤집힙니다)" : "이 특전은 음수여야 합니다 (양수면 효과 방향이 뒤집힙니다)";
        }

        if (TierOrderError(tiers) is { } order)
        {
            return order;
        }

        return tiers.Any(value => value <= -100) ? "감소는 −100 보다 커야 합니다 (배수가 0 이하가 됩니다)" : null;
    }

    /// <summary>단계가 오를수록 효과가 약해지면 이유 (|I| ≤ |II| ≤ |III|). 부호는 따로 본다. add-on 수치도 같은 규칙이다.</summary>
    public static string? TierOrderError(IReadOnlyList<double> tiers)
    {
        for (var index = 1; index < tiers.Count; index++)
        {
            if (Math.Abs(tiers[index]) < Math.Abs(tiers[index - 1]))
            {
                return "높은 단계가 낮은 단계보다 약할 수 없습니다 (|I| ≤ |II| ≤ |III|)";
            }
        }

        return null;
    }

    /// <summary>단계 수치를 넣은 정의. Amount 와 Bonus 값은 III 이다.</summary>
    public static PerkDefinition WithTiers(PerkDefinition perk, IReadOnlyList<double> tiers) =>
        perk with
        {
            Amount = tiers[^1],
            Tiers = tiers.ToList(),
            Bonuses = perk.Bonuses.Select(bonus => bonus with { Value = tiers[^1] }).ToList(),
        };

    /// <summary>숫자 3개 배열이면 그 값, 아니면 null.</summary>
    public static IReadOnlyList<double>? ReadTiers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != PerkConfig.MaxTier)
        {
            return null;
        }

        var tiers = new List<double>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var value))
            {
                return null;
            }

            tiers.Add(value);
        }

        return tiers;
    }

    /// <summary>"[25, 37.5, 50]" 형식 (로그).</summary>
    public static string Describe(IReadOnlyList<double> tiers) =>
        $"[{string.Join(", ", tiers.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))}]";

    // 액티브: 기본 정의에 있는 이름만, 이름마다 따로 검사한다
    private static PerkDefinition WithActiveValues(PerkDefinition perk, JsonElement element, Action<string> warn, List<string> changed)
    {
        var spec = perk.Active!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            warn($"perkAmounts.{perk.Id}: 액티브 특전은 {{ \"이름\": [I, II, III] }} 형식이어야 합니다. 기본값을 씁니다");
            return perk;
        }

        var cooldown = spec.Cooldown;
        var duration = spec.Duration;
        var values = spec.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var current = name switch
            {
                CooldownName when spec.Limit == ActiveLimit.Cooldown => spec.Cooldown,
                DurationName when spec.Duration.Any(value => value > 0) => spec.Duration,
                _ => spec.Values.GetValueOrDefault(name),
            };
            if (current is null)
            {
                var names = string.Join(", ", ActiveNames(spec));
                warn($"perkAmounts.{perk.Id}.{name}: 이 특전에 없는 값이라 무시합니다 (쓸 수 있는 값: {names})");
                continue;
            }

            var tiers = ReadTiers(property.Value);
            var error = tiers is null || tiers.Any(value => !double.IsFinite(value) || value <= 0)
                ? "0 보다 큰 숫자 3개 [I, II, III] 여야 합니다"
                : PercentValues.Contains(name) && tiers.Any(value => value > 100) ? "% 값이라 100 을 넘을 수 없습니다" : null;
            if (error is not null)
            {
                warn($"perkAmounts.{perk.Id}.{name}: {error}. 기본값 {Describe(current)} 을 씁니다");
                continue;
            }

            if (!tiers!.SequenceEqual(current))
            {
                changed.Add($"{perk.Id}.{name} {Describe(tiers!)}");
            }

            switch (name)
            {
                case CooldownName:
                    cooldown = tiers!;
                    break;
                case DurationName:
                    duration = tiers!;
                    break;
                default:
                    values[name] = tiers!;
                    break;
            }
        }

        return perk with { Active = spec with { Cooldown = cooldown, Duration = duration, Values = values } };
    }

    // config 에 쓸 수 있는 액티브 값 이름 (쿨타임 특전만 cooldown, 지속 효과만 duration)
    public static IEnumerable<string> ActiveNames(ActivePerkSpec spec)
    {
        if (spec.Limit == ActiveLimit.Cooldown)
        {
            yield return CooldownName;
        }

        if (spec.Duration.Any(value => value > 0))
        {
            yield return DurationName;
        }

        foreach (var name in spec.Values.Keys)
        {
            yield return name;
        }
    }
}
