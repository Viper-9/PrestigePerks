using PrestigePerks.Perks;

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
        Perks = [1, 24],
        CoinBalance = 120,
        Session = new PerkSession
        {
            Reason = PerkSessionReason.Prestige,
            Slots = [new PerkSlot { Perk = 1, Locked = true }, new PerkSlot { Perk = 24 }, new PerkSlot { Perk = 8 }],
            FreeRerollsLeft = 2,
            PaidRerolls = 1,
        },
    };

    [Fact]
    public void Missing_file_gives_new_data()
    {
        var result = PerkDataFile.Read(FilePath);
        Assert.Equal(PerkDataSource.New, result.Source);
        Assert.Empty(result.Data.Perks);
        Assert.Null(result.Data.Session);
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
        Assert.Equal([1, 24], data.Perks);
        Assert.Equal(120, data.CoinBalance);
        Assert.Equal(PerkSessionReason.Prestige, data.Session!.Reason);
        Assert.Equal([1, 24, 8], data.Session.Slots.Select(slot => slot.Perk!.Value));
        Assert.Equal([true, false, false], data.Session.Slots.Select(slot => slot.Locked));
        Assert.Equal(2, data.Session.FreeRerollsLeft);
        Assert.Equal(1, data.Session.PaidRerolls);
    }

    [Fact]
    public void Json_uses_the_documented_field_names()
    {
        var json = PerkDataFile.Serialize(Sample());
        foreach (var name in new[] { "schemaVersion", "perks", "coinBalance", "session", "reason", "slots", "perk", "locked", "freeRerollsLeft", "paidRerolls" })
        {
            Assert.Contains($"\"{name}\"", json);
        }

        Assert.DoesNotContain("LoadFailed", json);
    }

    [Fact]
    public void Second_write_keeps_the_previous_version_as_bak()
    {
        PerkDataFile.Write(FilePath, new PerkData { Perks = [1] });
        Assert.False(File.Exists(BackupPath));

        PerkDataFile.Write(FilePath, new PerkData { Perks = [1, 2] });

        Assert.Equal([1], PerkDataFile.Deserialize(File.ReadAllText(BackupPath)).Perks);
        Assert.Equal([1, 2], PerkDataFile.Deserialize(File.ReadAllText(FilePath)).Perks);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    public void Corrupt_file_is_restored_from_bak(string corrupt)
    {
        PerkDataFile.Write(FilePath, new PerkData { Perks = [8] });
        PerkDataFile.Write(FilePath, new PerkData { Perks = [8, 9] });
        File.WriteAllText(FilePath, corrupt);

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal(PerkDataSource.Backup, result.Source);
        Assert.Equal([8], result.Data.Perks);
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
        Assert.Empty(result.Data.Perks);
        Assert.True(File.Exists(result.CorruptCopyPath));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Unknown_and_duplicate_perks_are_dropped()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """
            { "schemaVersion": 1, "perks": [1, 5, 1, 99, 24], "coinBalance": -3,
              "session": { "reason": "prestige", "slots": [ { "perk": 13, "locked": true }, { "perk": 2 }, { "perk": 2 } ], "freeRerollsLeft": -1, "paidRerolls": 0 } }
            """);

        var result = PerkDataFile.Read(FilePath);

        Assert.Equal([1, 24], result.Data.Perks);
        Assert.Equal(0, result.Data.CoinBalance);
        Assert.Equal(new int?[] { null, 2, null }, result.Data.Session!.Slots.Select(slot => slot.Perk));
        Assert.False(result.Data.Session.Slots[0].Locked);
        Assert.Equal(0, result.Data.Session.FreeRerollsLeft);
        Assert.NotEmpty(result.Warnings);
    }
}
