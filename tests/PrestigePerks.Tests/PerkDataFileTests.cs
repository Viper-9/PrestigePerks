using PrestigePerks.Perks;
using static PrestigePerks.Tests.TestPerks;

namespace PrestigePerks.Tests;

public sealed class PerkDataFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PrestigePerksTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, PerkDataStore.FileName);
    private string BackupPath => FilePath + PerkDataFile.BackupSuffix;

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PerkData Sample() => new()
    {
        Slots = [new PerkSlot { Perk = Id(1), Locked = true }, new PerkSlot { Perk = Id(24) }, new PerkSlot { Perk = Id(8) }],
        Rerolls = new PerkRerolls { Level = 3, FreeLeft = 2, Paid = 1 },
        UnseenNewPerks = [Id(8)],
    };

    private static PerkData WithPerks(params int[] perks) => new() { Slots = perks.Select(perk => new PerkSlot { Perk = SlotPerk(perk) }).ToList() };

    private static IEnumerable<string?> Perks(PerkData data) => data.Slots.Select(slot => slot.Perk);

    [Fact]
    public void Missing_file_gives_new_data()
    {
        var result = PerkDataFile.Read(FilePath);
        Assert.Equal(PerkDataSource.New, result.Source);
        Assert.Empty(result.Data.Slots);
        Assert.Equal(0, result.Data.Rerolls.Level);
        Assert.Equal(PerkData.CurrentSchemaVersion, result.Data.SchemaVersion);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Round_trips_every_field()
    {
        PerkDataFile.Write(FilePath, Sample());
        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(PerkDataSource.Main, result.Source);
        Assert.Empty(result.Warnings);
        var data = result.Data;
        Assert.Equal(Ids(1, 24, 8), Perks(data));
        Assert.Equal([true, false, false], data.Slots.Select(slot => slot.Locked));
        Assert.Equal(3, data.Rerolls.Level);
        Assert.Equal(2, data.Rerolls.FreeLeft);
        Assert.Equal(1, data.Rerolls.Paid);
        Assert.Equal(Ids(8), data.UnseenNewPerks);
    }

    [Fact]
    public void Json_uses_the_documented_field_names()
    {
        var json = PerkDataFile.Serialize(Sample());
        foreach (var name in new[] { "schemaVersion", "slots", "perk", "locked", "fixed", "owned", "rerolls", "level", "freeLeft", "paid", "unseenNewPerks" })
        {
            Assert.Contains($"\"{name}\"", json);
        }

        Assert.DoesNotContain("LoadFailed", json);
        Assert.DoesNotContain("\"perks\"", json);
        Assert.Contains("\"schemaVersion\": 4", json);
        Assert.DoesNotContain("IsEmpty", json);
        Assert.DoesNotContain("Rollable", json);
    }

    [Fact]
    public void Schema_two_reads_as_nothing_owned_and_nothing_fixed()
    {
        var data = PerkDataFile.Normalize(PerkDataFile.Deserialize("""
            { "schemaVersion": 2, "slots": [ { "perk": 1, "locked": true }, { "perk": 24 } ], "rerolls": { "level": 2, "freeLeft": 1, "paid": 0 } }
            """), []);

        Assert.Equal(PerkData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Empty(data.Owned);
        Assert.Equal(SlotPerks(1, 24), data.Slots.Select(slot => slot.Perk));
        Assert.Equal([true, false], data.Slots.Select(slot => slot.Locked));
        Assert.All(data.Slots, slot => Assert.False(slot.Fixed));
    }

    [Fact]
    public void Owned_tiers_and_slot_states_are_cleaned_up()
    {
        var warnings = new List<string>();
        var data = PerkDataFile.Normalize(PerkDataFile.Deserialize("""
            {
              "schemaVersion": 3,
              "owned": { "1": 5, "24": 0, "999": 1, "8": 2 },
              "slots": [
                { "perk": 1, "locked": true, "fixed": true },
                { "perk": 0, "locked": true },
                { "perk": 24, "fixed": true },
                { "perk": 8, "fixed": true }
              ],
              "unseenTierCap": 7
            }
            """), warnings);

        Assert.Equal(new Dictionary<string, int> { [Id(1)] = 3, [Id(8)] = 2 }, data.Owned);
        Assert.Equal(SlotPerks(1, 0, 24, 8), data.Slots.Select(slot => slot.Perk));
        Assert.Equal([true, false, false, true], data.Slots.Select(slot => slot.Fixed));
        Assert.All(data.Slots, slot => Assert.False(slot.Locked));
        Assert.Null(data.UnseenTierCap);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void Second_write_keeps_the_previous_version_as_bak()
    {
        PerkDataFile.Write(FilePath, WithPerks(1));
        Assert.False(File.Exists(BackupPath));

        PerkDataFile.Write(FilePath, WithPerks(1, 2));

        Assert.Equal(SlotPerks(1), Perks(PerkDataFile.Deserialize(File.ReadAllText(BackupPath))));
        Assert.Equal(SlotPerks(1, 2), Perks(PerkDataFile.Deserialize(File.ReadAllText(FilePath))));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    public void Corrupt_file_is_restored_from_bak(string corrupt)
    {
        PerkDataFile.Write(FilePath, WithPerks(8));
        PerkDataFile.Write(FilePath, WithPerks(8, 9));
        File.WriteAllText(FilePath, corrupt);

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(PerkDataSource.Backup, result.Source);
        Assert.Equal(SlotPerks(8), Perks(result.Data));
        Assert.NotNull(result.CorruptCopyPath);
        Assert.True(File.Exists(result.CorruptCopyPath));
        Assert.Equal(corrupt, File.ReadAllText(result.CorruptCopyPath!));
        // 복구본이 본 파일로 다시 쓰여 다음 읽기는 정상
        Assert.Equal(PerkDataSource.Main, PerkDataFile.Read(FilePath).Source);
    }

    [Fact]
    public void Both_corrupt_resets_and_keeps_the_corrupt_copy()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{");
        File.WriteAllText(BackupPath, "[");

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(PerkDataSource.Reset, result.Source);
        Assert.Empty(result.Data.Slots);
        Assert.True(File.Exists(result.CorruptCopyPath));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Unknown_and_duplicate_perks_and_negative_counts_are_cleaned()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """
            { "schemaVersion": 2,
              "slots": [ { "perk": 1, "locked": true }, { "perk": 5 }, { "perk": 1 }, { "perk": 99 }, { "perk": 24 } ],
              "rerolls": { "level": 3, "freeLeft": -1, "paid": -2 },
              "unseenNewPerks": [24, 13] }
            """);

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(SlotPerks(1, 0, 0, 24), Perks(result.Data));
        Assert.True(result.Data.Slots[0].Locked);
        Assert.Equal(0, result.Data.Rerolls.FreeLeft);
        Assert.Equal(0, result.Data.Rerolls.Paid);
        Assert.Equal(3, result.Data.Rerolls.Level);
        Assert.Equal(Ids(24), result.Data.UnseenNewPerks);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Schema_one_keeps_confirmed_perks_and_drops_the_screen_and_balance()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """
            { "schemaVersion": 1, "perks": [27, 4, 1], "coinBalance": 120,
              "session": { "reason": "dev", "slots": [ { "perk": 9, "locked": true } ], "freeRerollsLeft": 2, "paidRerolls": 0 } }
            """);

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(SlotPerks(27, 4, 1), Perks(result.Data));
        Assert.All(result.Data.Slots, slot => Assert.False(slot.Locked));
        Assert.Equal(0, result.Data.Rerolls.Level);
        Assert.Equal(PerkData.CurrentSchemaVersion, result.Data.SchemaVersion);
        Assert.Null(result.Data.LegacyPerks);
        Assert.NotEmpty(result.Warnings);

        PerkDataFile.Write(FilePath, result.Data);
        Assert.DoesNotContain("coinBalance", File.ReadAllText(FilePath));
        Assert.Empty(PerkDataFile.Read(FilePath).Warnings);
    }

    [Fact]
    public void Schema_three_numbers_move_to_string_ids()
    {
        var warnings = new List<string>();
        var data = PerkDataFile.Normalize(PerkDataFile.Deserialize("""
            {
              "schemaVersion": 3,
              "slots": [ { "perk": 14, "fixed": true }, { "perk": 0 }, { "perk": 29, "locked": true } ],
              "owned": { "14": 2, "26": 1 },
              "rerolls": { "level": 3, "freeLeft": 2, "paid": 1 },
              "unseenNewPerks": [29]
            }
            """), warnings);

        Assert.Equal(4, data.SchemaVersion);
        Assert.Equal(["control", null, "reload"], data.Slots.Select(slot => slot.Perk));
        Assert.Equal([true, false, false], data.Slots.Select(slot => slot.Fixed));
        Assert.Equal([false, false, true], data.Slots.Select(slot => slot.Locked));
        Assert.Equal(new Dictionary<string, int> { ["control"] = 2, ["search"] = 1 }, data.Owned);
        Assert.Equal(["reload"], data.UnseenNewPerks);
        Assert.Contains(warnings, warning => warning.Contains("문자열 ID"));
    }

    [Fact]
    public void Schema_four_writes_string_ids_and_leaves_out_empty_perks()
    {
        PerkDataFile.Write(FilePath, new PerkData
        {
            Slots = [new PerkSlot { Perk = "control", Fixed = true }, new PerkSlot()],
            Owned = new Dictionary<string, int> { ["control"] = 2 },
        });
        var json = File.ReadAllText(FilePath);

        Assert.Contains("\"schemaVersion\": 4", json);
        Assert.Contains("\"perk\": \"control\"", json);
        Assert.Contains("\"control\": 2", json);
        Assert.DoesNotContain("\"perk\": null", json);
        Assert.DoesNotContain("\"perk\": 0", json);
        Assert.Empty(PerkDataFile.Read(FilePath).Warnings);
    }

    // 개발용 reset(PerkDataStore.Delete)은 빈 데이터를 덮어쓴다. 파일만 지우면 다음 읽기가 .bak 에서 복구해 reset 이 무효였다
    [Fact]
    public void Reset_by_writing_empty_data_is_not_undone_by_the_backup()
    {
        PerkDataFile.Write(FilePath, Sample());
        PerkDataFile.Write(FilePath, Sample());
        PerkDataFile.Write(FilePath, new PerkData());

        var result = PerkDataFile.Read(FilePath);
        Assert.Equal(PerkDataSource.Main, result.Source);
        Assert.Empty(result.Data.Slots);
        Assert.Equal(SlotPerks(1, 24, 8), Perks(PerkDataFile.Read(BackupPath).Data));
    }
}
