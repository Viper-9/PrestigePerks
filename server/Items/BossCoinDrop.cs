using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace PrestigePerks.Items;

/// <summary>
/// 보스 코인 드랍의 순수 함수 (단위 테스트). <see cref="BossCoinService"/> 가 봇 생성이 끝난 직후(BotGenerator.PrepareAndGenerateBot 후처리) 부른다.
/// 생성된 봇에 직접 넣으므로 SPT 루트 뽑기 횟수, 다른 모드가 넣은 GP 코인·레가 메달, APBS·ISB 프리셋과 상관없이 설정한 확률이 된다.
/// 2026-10-26 까지는 주머니 목록에 넣어 화폐 뽑기로 나왔지만 (약 23%, GP 코인 모드와 나누면 12.5%), 확률을 정할 수 없고
/// ISB 프리셋처럼 장비를 통째로 바꾸는 모드에서는 빠져서 2026-10-30 바꿨다.
/// </summary>
public static class BossCoinDrop
{
    /// <summary>
    /// 기본 대상: SPT 보스 본인 13명 + ISB 보스 2명 + 블랙 디비전 보스. 추종자와 카반 저격 경호원(bossboarsniper, 이름만 boss)은 넣지 않는다.
    /// 해당 모드가 없으면 그 역할은 생성되지 않을 뿐이다.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultRoles =
    [
        "bossboar", "bossbully", "bossgluhar", "bosskilla", "bosskillaagro", "bossknight", "bosskojaniy",
        "bosskolontay", "bosspartisan", "bosssanitar", "bosstagilla", "bosstagillaagro", "bosszryachiy",
        "isbbosscommander", "isbbossfirefly", "bosswedge",
    ];

    /// <summary>코인을 넣어 볼 칸 순서. 보안 컨테이너는 플레이어가 루팅할 수 없어 넣지 않는다.</summary>
    public static readonly IReadOnlyList<EquipmentSlots> SlotOrder = [EquipmentSlots.Pockets, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack];

    /// <summary>바닐라 GP 코인. 확인 명령에서 GP 코인 모드와 따로 나오는지 센다.</summary>
    public const string GpCoinTemplateId = "5d235b4d86f7742e017bc88a";

    public static bool IsTarget(IReadOnlyList<string>? roles, string? role) =>
        role is not null && roles is not null && roles.Contains(role.ToLowerInvariant());

    /// <summary>요청한 역할이나 생성된 봇의 역할 중 하나가 대상이면 true.</summary>
    public static bool IsTarget(IReadOnlyList<string>? roles, BotGenerationDetails? details, BotBase? bot) =>
        IsTarget(roles, details?.Role) || IsTarget(roles, bot?.Info?.Settings?.Role);

    /// <summary>확률에 맞으면 코인 개수(minStack~maxStack), 아니면 null. <paramref name="random"/> 은 병렬 생성에서도 안전해야 한다 (Random.Shared).</summary>
    public static int? Roll(BossCoinConfig config, Random random) =>
        random.NextDouble() < config.Chance ? random.Next(config.MinStack, config.MaxStack + 1) : null;

    public static Item CreateStack(int count) =>
        new() { Id = new MongoId(), Template = new MongoId(PrestigeCoinItem.TemplateId), Upd = new Upd { StackObjectsCount = count } };

    /// <summary><see cref="SlotOrder"/> 순서로 넣어 보고 처음 들어간 칸을 돌려준다. 모두 자리가 없으면 null.</summary>
    public static EquipmentSlots? Place(Func<EquipmentSlots, ItemAddedResult> tryAdd)
    {
        foreach (var slot in SlotOrder)
        {
            if (tryAdd(slot) == ItemAddedResult.SUCCESS)
            {
                return slot;
            }
        }

        return null;
    }
}
