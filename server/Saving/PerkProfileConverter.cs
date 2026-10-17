using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils.Json;

namespace PrestigePerks.Saving;

/// <summary>
/// 프로필을 파일로 저장할 때 특전 Bonus 를 뺀다. SptProfile 루트를 직렬화하는 곳은 SaveServer.SaveProfileAsync 뿐이다.
/// 원본 대신 with 로 만든 얕은 복사본을 직렬화하므로 메모리 프로필은 바뀌지 않는다.
/// </summary>
public sealed class PerkProfileConverter : JsonConverter<SptProfile>
{
    /// <summary>이 변환기를 뺀 옵션 복사본. 그대로 넘기면 자기 자신을 다시 불러 무한 재귀가 된다.</summary>
    private readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> innerOptions = new();

    public override SptProfile? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<SptProfile>(ref reader, GetInnerOptions(options));

    public override void Write(Utf8JsonWriter writer, SptProfile value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, StripPerkBonuses(value), GetInnerOptions(options));

    /// <summary>PMC Bonuses 에 특전 ID 가 있으면 그것만 뺀 복사본을, 없으면 원본을 돌려준다.</summary>
    internal static SptProfile StripPerkBonuses(SptProfile profile)
    {
        var characters = profile.CharacterData;
        var pmc = characters?.PmcData;
        if (pmc?.Bonuses is null || !pmc.Bonuses.Any(PerkBonusApplier.IsPerkBonus))
        {
            return profile;
        }

        return profile with
        {
            CharacterData = characters! with
            {
                PmcData = pmc with { Bonuses = PerkBonusApplier.Strip(pmc.Bonuses) },
            },
        };
    }

    private JsonSerializerOptions GetInnerOptions(JsonSerializerOptions options) =>
        innerOptions.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i] is PerkProfileConverter)
                {
                    copy.Converters.RemoveAt(i);
                }
            }

            return copy;
        });
}

/// <summary>SPT 의 JsonUtil 이 만들어질 때 변환기를 등록한다.</summary>
[Injectable]
public class PerkJsonConverterRegistrator : IJsonConverterRegistrator
{
    public IEnumerable<JsonConverter> GetJsonConverters() => [new PerkProfileConverter()];
}
