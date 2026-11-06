using System.Collections.Generic;
using PrestigePerks.Model;

namespace PrestigePerks;

/// <summary>
/// ConfigurationManager(F12)가 설정 태그에서 찾는 표시 값. 이름으로 찾으므로 클래스 이름과 필드 이름을 바꾸면 안 된다.
/// null 인 필드는 쓰지 않는다 (기본 표시).
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    public string Category;
    public string DispName;
    public string Description;
    public int? Order;
}

/// <summary>
/// F12 설정 창에 보이는 분류·이름·설명을 게임 언어로 (config.* 문구). .cfg 의 분류·키는 영어 그대로 둔다 (바꾸면 저장한 값을 잃는다).
/// ConfigurationManager 는 창을 열 때마다 태그를 다시 읽으므로, 메인 메뉴에서 언어를 알게 된 뒤 <see cref="Apply"/> 로 채우면 된다.
/// </summary>
internal static class ConfigText
{
    private static readonly List<(ConfigurationManagerAttributes Tag, string Section, string Key)> Entries = new List<(ConfigurationManagerAttributes, string, string)>();

    /// <summary>설정 하나의 태그. section, key 는 문구 키 (config.section.{section}, config.{key}.name / .desc). 같은 분류 안에서 order 가 큰 것이 위.</summary>
    public static ConfigurationManagerAttributes Tag(string section, string key, int order)
    {
        var tag = new ConfigurationManagerAttributes { Order = order };
        Entries.Add((tag, section, key));
        return tag;
    }

    public static void Apply(PerkLocale locale)
    {
        foreach (var (tag, section, key) in Entries)
        {
            tag.Category = locale.Get($"config.section.{section}");
            tag.DispName = locale.Get($"config.{key}.name");
            tag.Description = locale.Get($"config.{key}.desc");
        }
    }
}
