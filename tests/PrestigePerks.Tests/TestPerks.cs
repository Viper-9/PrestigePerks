using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

/// <summary>테스트는 읽기 쉽게 특전 번호로 쓰고 서버 문자열 ID 로 바꾼다. 카탈로그에 없는 번호는 모르는 ID("999")가 된다.</summary>
internal static class TestPerks
{
    public static string Id(int number) => PerkWire.ToId(number);

    public static string[] Ids(params int[] numbers) => numbers.Select(Id).ToArray();

    /// <summary>슬롯 칸의 특전. 0 은 빈 칸(null).</summary>
    public static string? SlotPerk(int number) => number == 0 ? null : Id(number);

    public static string?[] SlotPerks(params int[] numbers) => numbers.Select(SlotPerk).ToArray();
}
