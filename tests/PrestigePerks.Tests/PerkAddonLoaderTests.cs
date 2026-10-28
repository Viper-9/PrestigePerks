using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

public sealed class PerkAddonLoaderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private readonly string root = Path.Combine(Path.GetTempPath(), "PrestigePerksAddonTests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> warnings = [];

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private void AddOn(string name, string json, params (string Path, byte[] Bytes)[] files)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "perks.json"), json);
        foreach (var (path, bytes) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, path))!);
            File.WriteAllBytes(Path.Combine(folder, path), bytes);
        }
    }

    private static string Perk(string id, string? icon = null) =>
        $$"""{ "perks": [ { "id": "{{id}}", "kind": "code", "unit": "flat", "amount": 1{{(icon is null ? "" : $", \"icon\": \"{icon}\"")}} } ] }""";

    // 게임 확인용 예제 (deploy-dev.ps1 -Example). 잘못된 항목은 noauthor 하나뿐이다
    [Fact]
    public void Example_addon_passes_the_checks_except_its_one_bad_entry()
    {
        var report = PerkAddonLoader.Load(Path.Combine(AppContext.BaseDirectory, "ExampleAddon"), warnings.Add);

        Assert.Equal(["example"], report.Sources);
        Assert.Equal(10, report.Perks.Count);
        Assert.Equal(["example.porter"], report.Perks.Where(perk => perk.Kind == PerkKind.Patch).Select(perk => perk.Id));
        Assert.Equal(["example.fuel", "example.health", "example.porter"], report.Icons.Keys.Order());
        Assert.Contains("noauthor", Assert.Single(warnings));
    }

    [Fact]
    public void Missing_folder_is_no_addons_and_no_warning()
    {
        var report = PerkAddonLoader.Load(root, warnings.Add);
        Assert.Empty(report.Perks);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Addons_are_read_in_folder_name_order_and_later_duplicates_are_skipped()
    {
        AddOn("b-second", Perk("b.one"));
        AddOn("a-first", Perk("a.one"));
        AddOn("c-dup", Perk("a.one"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));

        var report = PerkAddonLoader.Load(root, warnings.Add);

        Assert.Equal(["a.one", "b.one"], report.Perks.Select(perk => perk.Id));
        Assert.Equal(["a-first", "b-second", "c-dup"], report.Sources);
        Assert.Equal(2, warnings.Count); // c-dup 의 겹친 ID, empty 의 perks.json 없음
    }

    [Fact]
    public void Valid_icons_are_read_and_bad_icons_only_drop_the_icon()
    {
        AddOn("ok", Perk("v.ok", "icons/v.ok.png"), ("icons/v.ok.png", Png));
        AddOn("missing", Perk("v.missing", "icons/none.png"));
        AddOn("notpng", Perk("v.notpng", "icon.png"), ("icon.png", [1, 2, 3, 4]));
        AddOn("big", Perk("v.big", "big.png"), ("big.png", [.. Png, .. new byte[PerkAddonLoader.MaxIconBytes]]));
        AddOn("escape", Perk("v.escape", "../ok/icons/v.ok.png"));

        var report = PerkAddonLoader.Load(root, warnings.Add);

        Assert.Equal(["v.big", "v.escape", "v.missing", "v.notpng", "v.ok"], report.Perks.Select(perk => perk.Id).Order());
        Assert.Equal(["v.ok"], report.Icons.Keys);
        Assert.Equal(Png, report.Icons["v.ok"]);
        Assert.Equal(4, warnings.Count);
    }
}
