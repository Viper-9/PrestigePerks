namespace PrestigePerks;

/// <summary>
/// ConfigurationManager(F12)가 설정 태그에서 찾는 표시 값. 이름으로 찾으므로 클래스 이름과 필드 이름을 바꾸면 안 된다.
/// F12 의 분류·이름·설명은 Config.Bind 의 영어 그대로 보이고, 여기서는 순서만 정한다. 같은 분류 안에서 Order 가 큰 것이 위.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    public int? Order;
}
