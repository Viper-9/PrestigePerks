using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace PrestigePerks;

/// <summary>
/// SPT가 리플렉션으로 찾는 모드 메타데이터.
/// </summary>
public record ModMetadata : IModMetadata
{
    /// <summary>Directory.Build.props 의 &lt;Version&gt; 이 어셈블리 InformationalVersion 으로 들어온다.</summary>
    public static string AssemblyVersion { get; } =
        typeof(ModMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>프레스티지 데이터를 교체하는 의존 모드. 이 모드가 바꾼 데이터 위에 덮어쓴다.</summary>
    public const string BackportPrestigesGuid = "wtf.archangel.contentbackportprestiges";

    /// <summary>백포트 모드의 필수 의존성. 같은 버전 범위로 우리도 요구한다.</summary>
    public const string WttCommonLibGuid = "com.wtt.commonlib";
    public const string WttContentBackportGuid = "com.wtt.contentbackport";

    public string ModGuid { get; init; } = "com.viper.prestigeperks";
    public string Name { get; init; } = "PrestigePerks";
    public string Author { get; init; } = "Viper-9";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(AssemblyVersion);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.2");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } =
        new()
        {
            { BackportPrestigesGuid, new SemanticVersioning.Range("^1.1.0") },
            { WttCommonLibGuid, new SemanticVersioning.Range("^3.0") },
            { WttContentBackportGuid, new SemanticVersioning.Range("~2.0") },
        };
    public string? Url { get; init; } = "https://github.com/Viper-9/PrestigePerks";
    public string License { get; init; } = "All Rights Reserved";
    public bool HasPrepatcher { get; init; } = false;
}
