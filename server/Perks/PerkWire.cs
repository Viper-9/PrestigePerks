using System.Globalization;

namespace PrestigePerks.Perks;

/// <summary>
/// 서버 안은 특전 문자열 ID 를 쓰고, 클라이언트와 주고받는 값(state 응답·요청)은 아직 번호다.
/// 통신을 ID 로 바꾸는 일은 서버가 카탈로그를 state 에 실어 보낼 때 한다 (dev-docs 98_확장성_특전_애드온.md 3절 3번).
/// </summary>
public static class PerkWire
{
    /// <summary>요청의 번호를 ID 로. 모르는 번호는 카탈로그에 없는 ID("999")가 되어 규칙이 UnknownPerk 로 거부한다.</summary>
    public static string ToId(int number) => PerkCatalog.IdOf(number) ?? number.ToString(CultureInfo.InvariantCulture);

    /// <summary>빈 칸과 모르는 특전(add-on 을 뺀 경우)은 0 (클라이언트의 빈 칸).</summary>
    public static int ToNumber(string? id) => PerkCatalog.NumberOf(id) ?? 0;

    /// <summary>모르는 특전은 빠진다.</summary>
    public static IReadOnlyList<int> ToNumbers(IEnumerable<string?> ids) => ids.Select(PerkCatalog.NumberOf).OfType<int>().ToList();

    /// <summary>특전 → 단계. 모르는 특전은 빠진다 (도감 수에 넣지 않는다).</summary>
    public static IReadOnlyDictionary<int, int> ToNumberKeys(IReadOnlyDictionary<string, int> tiers) =>
        tiers.Select(pair => (Number: PerkCatalog.NumberOf(pair.Key), Tier: pair.Value))
            .Where(pair => pair.Number is not null)
            .ToDictionary(pair => pair.Number!.Value, pair => pair.Tier);

    public static PerkSlotState ToState(PerkSlot slot) => new() { Perk = ToNumber(slot.Perk), Locked = slot.Locked, Fixed = slot.Fixed };
}
