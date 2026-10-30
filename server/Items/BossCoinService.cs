using PrestigePerks.Logging;
using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace PrestigePerks.Items;

/// <summary>
/// 생성이 끝난 보스에게 코인을 넣는다 (<see cref="BossCoinDrop"/>). BossCoinPatch 가 BotGenerator.PrepareAndGenerateBot 후처리에서 부른다.
/// SPT 는 봇을 병렬로 만들므로 넘겨받은 봇만 건드리고 Random.Shared 를 쓴다.
/// 빈칸은 최종 인벤토리에서 직접 찾는다. SPT 루트 생성기의 배치 함수(BotGeneratorHelper.AddItemWithChildrenToEquipmentSlot)는
/// 생성 중에만 있는 칸 캐시를 쓰는데, 장비 생성이 끝나면 지워지고(BotInventoryGenerator.GenerateInventory → ClearCache)
/// ISB 프리셋이 장비를 바꾸면 실제 장비와 맞지도 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BossCoinService(
    ISptLogger<PerksFileLogger> logger,
    ItemHelper itemHelper,
    InventoryHelper inventoryHelper,
    PerkConfigLoader configLoader,
    PerkService perkService)
{
    /// <summary>Harmony 패치가 쓰는 인스턴스. 모드 로드 때 넣는다.</summary>
    internal static BossCoinService? Instance { get; set; }

    /// <summary>확인 명령이 봇을 수백 번 만들 때 개발자 로그를 끈다. 명령은 한 스레드에서 차례로 만든다.</summary>
    [ThreadStatic]
    private static bool quiet;

    private int noRoom;

    /// <summary>확률에 맞았지만 주머니·조끼·배낭에 자리가 없어 빠진 횟수 (서버 시작 뒤 누계).</summary>
    public int NoRoom => Volatile.Read(ref noRoom);

    public void LogSettings()
    {
        var config = configLoader.BossCoins;
        logger.Info(config.Enabled
            ? $"[PrestigePerks] boss coins: {config.Chance:P0} per boss, {config.MinStack}-{config.MaxStack} coins, {config.Roles?.Count ?? 0} roles"
            : "[PrestigePerks] boss coins off");
    }

    public void Add(MongoId sessionId, BotGenerationDetails? details, BotBase? bot)
    {
        var config = configLoader.BossCoins;
        if (!config.Enabled || bot?.Inventory is not { } inventory || !BossCoinDrop.IsTarget(config.Roles, details, bot))
        {
            return;
        }

        var role = (details?.Role ?? bot.Info?.Settings?.Role ?? "?").ToLowerInvariant();
        if (BossCoinDrop.Roll(config, Random.Shared) is not { } count)
        {
            Dev(sessionId, $"{role} no coin ({config.Chance:P0})");
            return;
        }

        var stack = BossCoinDrop.CreateStack(count);
        var slot = BossCoinDrop.Place(slot => TryAdd(inventory, slot, stack));
        if (slot is null)
        {
            Interlocked.Increment(ref noRoom);
            Dev(sessionId, $"{role} rolled {count} coins, no room in pockets, vest or backpack");
            return;
        }

        Dev(sessionId, $"{role} {count} coins ({slot})");
    }

    /// <summary>
    /// 장비 칸(주머니·조끼·배낭) 컨테이너의 그리드마다 빈 1×1 칸을 찾아 넣는다.
    /// 필터 검사는 SPT BotInventoryContainerService.ItemAllowedInContainer 와 같다 (첫 그리드 필터, 아이템의 부모 분류).
    /// </summary>
    private ItemAddedResult TryAdd(BotBaseInventory inventory, EquipmentSlots slot, Item stack)
    {
        var items = inventory.Items;
        var slotName = slot.ToString();
        // ParentId 는 문자열이고 루트 아이템은 null 이라 MongoId 로 바꾸지 않고 문자열로 비교한다
        var equipmentId = inventory.Equipment?.ToString();
        var container = items?.FirstOrDefault(item => item.SlotId == slotName && item.ParentId == equipmentId);
        var grids = container is null ? null : itemHelper.GetItem(container.Template).Value?.Properties?.Grids?.ToList();
        if (items is null || container is null || grids is not { Count: > 0 })
        {
            return ItemAddedResult.NO_CONTAINERS;
        }

        if (!Allowed(grids[0], itemHelper.GetItem(stack.Template).Value?.Parent))
        {
            return ItemAddedResult.INCOMPATIBLE_ITEM;
        }

        var containerId = container.Id.ToString();
        foreach (var grid in grids)
        {
            if (grid.Properties?.CellsH is not { } width || grid.Properties.CellsV is not { } height)
            {
                continue;
            }

            // GetContainerMap 은 부모로만 거르므로 다른 그리드에 바로 들어 있는 아이템은 뺀다 (그 안의 자식은 크기 계산에 필요해 둔다)
            var gridItems = items.Where(item => item.ParentId != containerId || item.SlotId == grid.Name);
            var found = inventoryHelper.GetContainerMap(width, height, gridItems, container.Id).FindSlotForItem(1, 1);
            if (found.Success != true)
            {
                continue;
            }

            stack.ParentId = container.Id;
            stack.SlotId = grid.Name;
            stack.Location = new ItemLocation { X = found.X, Y = found.Y, R = ItemRotation.Horizontal };
            items.Add(stack);
            return ItemAddedResult.SUCCESS;
        }

        return ItemAddedResult.NO_SPACE;
    }

    private static bool Allowed(Grid grid, MongoId? parent)
    {
        var filters = grid.Properties?.Filters?.FirstOrDefault();
        if (filters is null)
        {
            return true;
        }

        if (parent is { } excluded && filters.ExcludedFilter?.Contains(excluded) == true)
        {
            return false;
        }

        var filter = filters.Filter;
        if (filter is null || filter.Count == 0 || (filter.Count == 1 && filter.Contains(BaseClasses.ITEM)))
        {
            return true;
        }

        return parent is { } allowed && filter.Contains(allowed);
    }

    /// <summary>개발자 로그 없이 실행한다 (확인 명령).</summary>
    public T Quietly<T>(Func<T> action)
    {
        quiet = true;
        try
        {
            return action();
        }
        finally
        {
            quiet = false;
        }
    }

    private void Dev(MongoId sessionId, string message)
    {
        if (!quiet && perkService.IsDevMode(sessionId))
        {
            logger.Info($"[PrestigePerks] [dev] boss coin: {message}");
        }
    }
}
