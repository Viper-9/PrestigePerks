using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>
/// config/config.json 의 "activePerks". 끄면 클라이언트가 액티브 UI(칸, ACTIVE 탭, 목록, 알림, 레이드 키·HUD)를 없앤다.
/// 서버는 값을 state 로 넘기기만 한다 (리롤은 숨은 액티브 칸도 돌린다. 산 액티브와 코인 기록은 지우지 않는다). dev-docs 94 1절.
/// </summary>
public sealed record ActivePerkConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}
