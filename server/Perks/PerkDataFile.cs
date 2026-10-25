using System.Text.Json;

namespace PrestigePerks.Perks;

public enum PerkDataSource
{
    /// <summary>파일이 없어 새로 만든 데이터.</summary>
    New,
    Main,
    /// <summary>본 파일이 깨져 .bak 에서 복구.</summary>
    Backup,
    /// <summary>본 파일과 .bak 이 모두 깨져 빈 데이터로 시작.</summary>
    Reset,
}

public sealed record PerkDataReadResult(PerkData Data, PerkDataSource Source, string? CorruptCopyPath, IReadOnlyList<string> Warnings);

/// <summary>
/// 특전 데이터 파일 읽기·쓰기. 저장할 때 직전 버전을 .bak 으로 남기고, 읽기에 실패하면 .bak 으로 복구한다.
/// SaveLoadRouter.HandleLoad 가 동기라 모두 동기 입출력이다.
/// </summary>
public static class PerkDataFile
{
    public const string BackupSuffix = ".bak";
    private const string TempSuffix = ".tmp";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(PerkData data) => JsonSerializer.Serialize(data, Options);

    /// <summary>JSON 을 읽는다. 형식이 틀리거나 null 이면 JsonException.</summary>
    public static PerkData Deserialize(string json) =>
        JsonSerializer.Deserialize<PerkData>(json, Options) ?? throw new JsonException("file contains null");

    public static PerkDataReadResult Read(string path)
    {
        var backupPath = path + BackupSuffix;
        string? corruptCopy = null;
        var warnings = new List<string>();

        if (File.Exists(path))
        {
            if (TryParse(path, out var data, out var error))
            {
                return new PerkDataReadResult(Normalize(data, warnings), PerkDataSource.Main, null, warnings);
            }

            corruptCopy = MoveAside(path);
            warnings.Add($"{Path.GetFileName(path)} 을 읽지 못했습니다 ({error}). {Path.GetFileName(corruptCopy)} 로 옮겼습니다");
        }

        if (File.Exists(backupPath))
        {
            if (TryParse(backupPath, out var data, out var error))
            {
                File.Copy(backupPath, path, overwrite: true);
                return new PerkDataReadResult(Normalize(data, warnings), PerkDataSource.Backup, corruptCopy, warnings);
            }

            warnings.Add($"{Path.GetFileName(backupPath)} 도 읽지 못했습니다 ({error})");
            return new PerkDataReadResult(new PerkData(), PerkDataSource.Reset, corruptCopy, warnings);
        }

        return new PerkDataReadResult(new PerkData(), corruptCopy is null ? PerkDataSource.New : PerkDataSource.Reset, corruptCopy, warnings);
    }

    /// <summary>임시 파일에 쓴 뒤 교체한다. 기존 파일은 .bak 이 된다.</summary>
    public static void Write(string path, PerkData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = path + TempSuffix;
        File.WriteAllText(tempPath, Serialize(data));
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, path + BackupSuffix);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    /// <summary>schema 1 을 옮기고, 모르는 특전 번호·중복·잘못된 값을 정리한다. schema 2 는 그대로 읽혀 "보유 없음, 확정 없음"이 된다. 정리한 내용은 warnings 에 남긴다.</summary>
    internal static PerkData Normalize(PerkData data, List<string> warnings)
    {
        if (data.SchemaVersion > PerkData.CurrentSchemaVersion)
        {
            warnings.Add($"schemaVersion {data.SchemaVersion} 은 이 버전보다 새 형식입니다. 아는 항목만 읽습니다");
        }

        data.Slots ??= [];
        data.Rerolls ??= new PerkRerolls();
        data.UnseenNewPerks ??= [];
        if (data.SchemaVersion < 2)
        {
            // schema 1 은 2026-10-18 개발 중에만 쓴 형식이다. 확정 특전만 슬롯으로 옮기고 화면 상태·잔액은 버린다
            data.Slots = (data.LegacyPerks ?? []).Select(perk => new PerkSlot { Perk = perk }).ToList();
            warnings.Add($"schemaVersion {data.SchemaVersion} 을 {PerkData.CurrentSchemaVersion} 으로 옮겼습니다. 특전 [{string.Join(", ", data.LegacyPerks ?? [])}] 만 남기고 화면 상태·코인 잔액은 버립니다");
        }

        data.LegacyPerks = null;
        data.SchemaVersion = PerkData.CurrentSchemaVersion;
#if !DEV_COMMANDS
        if (data.DevAllPerks)
        {
            // 개발용 플래그는 상용 빌드에서 효과가 없다 (테스터 파일을 그대로 가져온 경우)
            warnings.Add("개발용 devAllPerks 를 껐습니다");
            data.DevAllPerks = false;
        }
#endif

        data.Owned ??= [];
        var owned = data.Owned.Where(pair => PerkCatalog.IsKnown(pair.Key) && pair.Value >= 1)
            .ToDictionary(pair => pair.Key, pair => Math.Min(pair.Value, PerkConfig.MaxTier));
        if (owned.Count != data.Owned.Count || owned.Any(pair => data.Owned[pair.Key] != pair.Value))
        {
            warnings.Add($"도감의 모르는 특전·잘못된 단계를 정리했습니다: {Describe(data.Owned)} → {Describe(owned)}");
        }

        data.Owned = owned;

        // 빈 칸(0)은 남긴다. 모르는 번호·겹친 특전은 지금처럼 지워서 새 단계 진입(EnterLevel)이 무료로 다시 채운다
        var seen = new HashSet<int>();
        var before = DescribeSlots(data.Slots);
        var fixedUp = false;
        var slots = new List<PerkSlot>();
        foreach (var slot in data.Slots)
        {
            if (slot is null || (!slot.IsEmpty && (!PerkCatalog.IsKnown(slot.Perk) || !seen.Add(slot.Perk))))
            {
                fixedUp = true;
                continue;
            }

            // 확정 칸은 보유한 특전만, 잠금과 함께 켜지지 않는다. 빈 칸은 잠그거나 확정할 수 없다
            var shouldFix = slot.Fixed && !slot.IsEmpty && owned.ContainsKey(slot.Perk);
            var shouldLock = slot.Locked && !shouldFix && !slot.IsEmpty;
            if (shouldFix != slot.Fixed || shouldLock != slot.Locked)
            {
                slot.Fixed = shouldFix;
                slot.Locked = shouldLock;
                fixedUp = true;
            }

            slots.Add(slot);
        }

        if (fixedUp)
        {
            warnings.Add($"모르는 특전 번호·중복 슬롯을 지우고 잘못된 칸 상태를 정리했습니다: [{before}] → [{DescribeSlots(slots)}]");
        }

        data.Slots = slots;
        data.UnseenNewPerks = data.UnseenNewPerks.Where(seen.Contains).Distinct().ToList();
        if (data.UnseenTierCap is < 1 or > PerkConfig.MaxTier)
        {
            data.UnseenTierCap = null;
        }

        var rerolls = data.Rerolls;
        if (rerolls.Level < 0 || rerolls.FreeLeft < 0 || rerolls.Paid < 0 || rerolls.FreeGranted < 0)
        {
            warnings.Add($"음수 리롤 값을 0 으로 바꿨습니다 (level {rerolls.Level}, freeLeft {rerolls.FreeLeft}, freeGranted {rerolls.FreeGranted}, paid {rerolls.Paid})");
            rerolls.Level = Math.Max(rerolls.Level, 0);
            rerolls.FreeLeft = Math.Max(rerolls.FreeLeft, 0);
            rerolls.FreeGranted = rerolls.FreeGranted is int granted ? Math.Max(granted, 0) : null;
            rerolls.Paid = Math.Max(rerolls.Paid, 0);
        }

        return data;
    }

    private static string DescribeSlots(IEnumerable<PerkSlot?> slots) =>
        string.Join(", ", slots.Select(slot => slot is null ? "null" : $"{slot.Perk}{(slot.Locked ? " locked" : "")}{(slot.Fixed ? " fixed" : "")}"));

    private static string Describe(Dictionary<int, int> owned) => $"{{{string.Join(", ", owned.Select(pair => $"{pair.Key}:{pair.Value}"))}}}";

    private static bool TryParse(string path, out PerkData data, out string? error)
    {
        try
        {
            data = Deserialize(File.ReadAllText(path));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            data = null!;
            error = ex.Message;
            return false;
        }
    }

    private static string MoveAside(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var corruptPath = Path.Combine(directory, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(path, corruptPath, overwrite: true);
        return corruptPath;
    }
}
