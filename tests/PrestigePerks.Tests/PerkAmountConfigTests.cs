using System.Text.Json;
using PrestigePerks.Model;
using PrestigePerks.Perks;
using PrestigePerks.Saving;

namespace PrestigePerks.Tests;

/// <summary>config perkAmounts: 내장 특전 단계별 수치 (서버 카탈로그 → state catalog → 클라이언트 계수). 카탈로그를 바꾸므로 따로 돈다.</summary>
[Collection(PerkCatalogInstallCollection.Name)]
public sealed class PerkAmountConfigTests : IDisposable
{
    private readonly List<string> warnings = [];

    public void Dispose()
    {
        PerkCatalog.Configure(PerkCatalog.DefaultBuiltin, PerkCatalog.DefaultBuiltinActive);
        PerkCatalog.Install([]);
        PerkPatchMath.Configure(null!);
    }

    private PerkAmountResult Resolve(string json) =>
        PerkAmountConfig.Resolve(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), warnings.Add);

    private static PerkDefinition Passive(PerkAmountResult result, string id) => result.Builtin.Single(perk => perk.Id == id);

    private static ActivePerkSpec Active(PerkAmountResult result, string id) => result.BuiltinActive.Single(perk => perk.Id == id).Active!;

    [Fact]
    public void Shipped_config_lists_every_builtin_perk_with_the_default_amounts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var amounts = PerkConfigLoader.ParseModConfig(File.ReadAllText(path)).PerkAmounts!;

        var result = PerkAmountConfig.Resolve(amounts, warnings.Add);

        Assert.Empty(warnings);
        Assert.Empty(result.Changed);
        Assert.Equal(PerkCatalog.DefaultBuiltin.Concat(PerkCatalog.DefaultBuiltinActive).Select(perk => perk.Id), amounts.Keys);
        // 액티브는 config 에 쓸 수 있는 값을 모두 적어 둔다
        Assert.All(PerkCatalog.DefaultBuiltinActive, perk =>
            Assert.Equal(PerkAmountConfig.ActiveNames(perk.Active!).Order(), amounts[perk.Id].EnumerateObject().Select(value => value.Name).Order()));
    }

    [Fact]
    public void Defaults_are_half_and_three_quarters_of_tier_three()
    {
        var result = Resolve("{}");

        Assert.Empty(result.Changed);
        Assert.Equal([2.5, 3.75, 5], Passive(result, "agility").TierAmounts);
        Assert.Equal([-15, -22.5, -30], Passive(result, "scav").TierAmounts);
        Assert.Equal(0.75, Passive(result, "scav").Ratio(2));
    }

    [Fact]
    public void Passive_tiers_set_every_tier_and_the_bonus_value()
    {
        var result = Resolve("""{ "agility": [1, 4, 10], "experience": [10, 20, 40] }""");

        var agility = Passive(result, "agility");
        Assert.Equal((10.0, 0.1, 0.4, 1.0), (agility.Amount, agility.Ratio(1), agility.Ratio(2), agility.Ratio(3)));
        var experience = Passive(result, "experience");
        Assert.Equal(40, Assert.Single(experience.Bonuses).Value);
        Assert.Equal(["agility [1, 4, 10]", "experience [10, 20, 40]"], result.Changed.Order());
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("[5, 10]")]           // 3개가 아님
    [InlineData("[0, 10, 20]")]       // 0
    [InlineData("[-5, -10, -20]")]    // 부호가 뒤집힘 (늘어나는 특전)
    [InlineData("[10, 5, 20]")]       // 높은 단계가 약함
    [InlineData("\"20\"")]            // 배열이 아님
    [InlineData("[\"5\", 10, 20]")]   // 숫자가 아님
    public void Bad_passive_tiers_keep_the_default_with_a_warning(string tiers)
    {
        var result = Resolve($$"""{ "porter": {{tiers}}, "search": [10, 20, 40] }""");

        Assert.Equal([10, 15, 20], Passive(result, "porter").TierAmounts);
        Assert.Equal(40, Passive(result, "search").Amount); // 다른 특전은 그대로 적용
        Assert.Contains(warnings, warning => warning.StartsWith("perkAmounts.porter:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[-50, -80, -100]")]  // 배수 0
    [InlineData("[5, 10, 20]")]       // 감소 특전이 양수
    public void Decreases_stay_negative_and_above_minus_100(string tiers)
    {
        var result = Resolve($$"""{ "tough": {{tiers}} }""");

        Assert.Equal(-20, Passive(result, "tough").Amount);
        Assert.Single(warnings);
    }

    [Fact]
    public void Unknown_and_addon_ids_are_ignored_with_a_warning()
    {
        Resolve("""{ "sprint": [1, 2, 3], "viper.medic": [1, 2, 3] }""");

        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("perks.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Active_values_are_set_by_name()
    {
        var result = Resolve("""
            { "grit": { "reduction": [20, 25, 30], "duration": [10, 20, 30] },
              "secondwind": { "cooldown": [200, 150, 100] } }
            """);

        Assert.Equal([20, 25, 30], Active(result, "grit").Values["reduction"]);
        Assert.Equal([5, 6, 7.5], Active(result, "grit").Values["head"]); // 적지 않은 값은 기본값
        Assert.Equal([10, 20, 30], Active(result, "grit").Duration);
        Assert.Equal([200, 150, 100], Active(result, "secondwind").Cooldown); // 쿨타임은 줄어드는 게 맞다
        Assert.Equal(3, result.Changed.Count);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("firstaid", "cooldown", "[10, 10, 10]")]  // 레이드당 1회 특전은 쿨타임이 없다
    [InlineData("resupply", "duration", "[10, 10, 10]")]  // 즉시 효과는 지속 시간이 없다
    [InlineData("grit", "armor", "[1, 2, 3]")]             // 없는 이름
    [InlineData("grit", "reduction", "[50, 80, 120]")]    // % 는 100 이하
    [InlineData("sonarpulse", "radius", "[0, 65, 80]")]   // 0 보다 커야 한다
    [InlineData("sonarpulse", "radius", "[50, 65]")]
    public void Bad_active_values_keep_the_default_with_a_warning(string id, string name, string tiers)
    {
        var result = Resolve($$"""{ "{{id}}": { "{{name}}": {{tiers}} } }""");

        var defaults = PerkCatalog.DefaultBuiltinActive.Single(perk => perk.Id == id).Active!;
        Assert.Equal(defaults.Cooldown, Active(result, id).Cooldown);
        Assert.Equal(defaults.Duration, Active(result, id).Duration);
        Assert.Equal(defaults.Values.Keys, Active(result, id).Values.Keys);
        Assert.All(defaults.Values, pair => Assert.Equal(pair.Value, Active(result, id).Values[pair.Key]));
        Assert.Contains(warnings, warning => warning.StartsWith($"perkAmounts.{id}.{name}:", StringComparison.Ordinal));
    }

    [Fact]
    public void Active_perks_need_an_object()
    {
        var result = Resolve("""{ "focus": [1, 2, 3] }""");

        Assert.Equal([80, 140, 200], Active(result, "focus").Duration);
        Assert.Single(warnings);
    }

    [Fact]
    public void Configured_amounts_reach_the_catalog_state_bonuses_and_artisan()
    {
        var result = Resolve("""{ "artisan": [-10, -20, -40], "experience": [10, 20, 40], "focus": { "duration": [60, 90, 120] } }""");
        PerkCatalog.Configure(result.Builtin, result.BuiltinActive);
        PerkCatalog.Install([new PerkDefinition(0, "viper.sprint", PerkKind.Patch, 10, PerkUnit.Percent, [], new PerkAddonInfo("x", new Dictionary<string, PerkTexts>(), null))]);

        Assert.Equal([-10, -20, -40], PerkCatalog.Entries.Single(entry => entry.Id == "artisan").Tiers);
        Assert.Equal([5, 7.5, 10], PerkCatalog.Entries.Single(entry => entry.Id == "viper.sprint").Tiers); // add-on 숫자 하나는 기본 비율
        Assert.Equal([60, 90, 120], PerkCatalog.ActiveEntries.Single(entry => entry.Id == "focus").Active!.Duration);
        Assert.True(PerkCatalog.TryGet(17, out var artisan));
        Assert.Equal(-40, artisan.Amount);
        Assert.Equal(0.6, PerkArtisan.TimeFactor, 6);
        Assert.Equal(1 - 0.4 * 0.25, PerkArtisan.Factor(0.25), 6); // 단계 I: 10% ÷ 40% = 0.25

        var bonus = Assert.Single(PerkBonusApplier.CreateBonuses(new Dictionary<string, double> { ["experience"] = PerkCatalog.Builtin[0].Ratio(1) }));
        Assert.Equal(10, bonus.Value!.Value, 6);

        // add-on 을 다시 설치해도 config 수치는 남는다
        PerkCatalog.Install([]);
        Assert.Equal(-40, PerkCatalog.Entries.Single(entry => entry.Id == "artisan").Amount);
    }

    [Fact]
    public void Configure_keeps_ids_and_order()
    {
        Assert.Throws<ArgumentException>(() => PerkCatalog.Configure(PerkCatalog.DefaultBuiltin.Skip(1).ToList(), PerkCatalog.DefaultBuiltinActive));
        Assert.Throws<ArgumentException>(() => PerkCatalog.Configure(PerkCatalog.DefaultBuiltin, PerkCatalog.DefaultBuiltinActive.Reverse().ToList()));
    }

    [Fact]
    public void Client_patch_factors_and_scales_follow_the_server_catalog()
    {
        var result = Resolve("""{ "agility": [2, 4, 10], "control": [-2, -5, -20], "handling": [5, 10, 15], "maintenance": [-10, -25, -50] }""");
        PerkCatalog.Configure(result.Builtin, result.BuiltinActive);
        var catalog = PerkStateDto.Parse(JsonSerializer.Serialize(new PerkState { Catalog = PerkCatalog.Entries }))!.Catalog;

        PerkPatchMath.Configure(catalog);

        Assert.Equal(1.1f, PerkPatchMath.MoveSpeedFactor, 4);
        Assert.Equal(0.2f, PerkPatchMath.RecoilBuff, 4);
        Assert.Equal(15f, PerkPatchMath.ErgonomicsPoints, 4);
        Assert.Equal((0.5f, 0.5f), (PerkPatchMath.WeaponWearFactor, PerkPatchMath.ArmorWearFactor));
        Assert.Equal(0.8f, PerkPatchMath.ChanceFactor, 4); // 바꾸지 않은 특전은 기본값
        var scales = PerkPatchMath.BuildScales(new Dictionary<string, int> { ["agility"] = 1, ["control"] = 2 }, catalog, null!);
        Assert.Equal(0.2f, scales[PerkPatchSlots.Agility], 4);
        Assert.Equal(0.25f, scales[PerkPatchSlots.Control], 4);
        // 단계 I 이동량: 1 + 0.1 × 0.2 = 1.02 (config 의 +2%)
        Assert.Equal(1.02f, PerkPatchMath.MoveDistance(1f, scales[PerkPatchSlots.Agility]), 4);
        Assert.Equal("+2%", PerkAmounts.Format(catalog.Single(entry => entry.Id == "agility"), 1));
    }

    // 개발자 모드 [perk][CHECK]: config 수치에서 직접 계산한 기대값이 실제 패치 계산과 같아야 게임에서 거짓 FAIL 이 없다
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "porter": [40, 60, 80], "stamina": [30, 45, 60], "control": [-20, -30, -40], "tough": [-30, -40, -50], "agility": [2, 4, 10], "maintenance": [-10, -25, -50], "search": [20, 40, 60], "surgeon": [10, 30, 50], "handling": [5, 10, 15], "artisan": [-10, -20, -40] }""")]
    public void Patch_checks_agree_with_the_patch_math(string amounts)
    {
        var result = Resolve(amounts);
        PerkCatalog.Configure(result.Builtin, result.BuiltinActive);
        var catalog = PerkStateDto.Parse(JsonSerializer.Serialize(new PerkState { Catalog = PerkCatalog.Entries }))!.Catalog;
        PerkPatchMath.Configure(catalog);
        float[] befores = [0.05f, 0.3f, 0.75f, 1f, 2.5f, 40f];

        for (var tier = 1; tier <= 3; tier++)
        {
            foreach (var before in befores)
            {
                Check("porter", PerkCheckForm.Add, (b, s) => PerkPatchMath.CarryingWeight(b, s));
                Check("stamina", PerkCheckForm.AddBase, (b, s) => PerkPatchMath.StaminaCapacity(b, 100f, s), 100f);
                Check("control", PerkCheckForm.AddCapped, (b, s) => PerkPatchMath.RecoilSuppression(b, s));
                Check("handling", PerkCheckForm.Flat, (b, s) => PerkPatchMath.Ergonomics(b, s));
                Check("temperance", PerkCheckForm.Factor, (b, s) => PerkPatchMath.Consumption(b, s));
                Check("mastery", PerkCheckForm.Factor, (b, s) => PerkPatchMath.Mastering(b, s));
                Check("search", PerkCheckForm.Search, (b, s) => PerkPatchMath.AttentionLootSpeed(b, s));
                Check("tough", PerkCheckForm.Inverse, (b, s) => PerkPatchMath.ChanceCap(b, s));
                Check("reload", PerkCheckForm.Factor, (b, s) => PerkPatchMath.ReloadSpeed(b, s));
                Check("reliability", PerkCheckForm.Factor, (b, s) => PerkPatchMath.Malfunction(b, s));
                Check("reliability", PerkCheckForm.Factor, (b, s) => PerkPatchMath.ShotOverheat(b, s));
                Check("maintenance", PerkCheckForm.Treatment, (b, s) => PerkPatchMath.WeaponTreatmentFactor(b, s));
                Check("maintenance", PerkCheckForm.Factor, (b, s) => PerkPatchMath.ArmorDamage(b, s));
                Check("agility", PerkCheckForm.FactorSigned, (b, s) => PerkPatchMath.MoveDistance(b, s));
                Check("agility", PerkCheckForm.FactorSigned, (b, s) => PerkPatchMath.MoveDistance(-b, s), negate: true);
                Check("surgeon", PerkCheckForm.Surgery, (b, s) => PerkPatchMath.SurgeryTime(b, 0.4f, s), 0.4f);
                Check("loading", PerkCheckForm.Factor, (b, s) => PerkPatchMath.LoadOneAmmoSpeed(b, s));
                Check("artisan", PerkCheckForm.Factor, (b, s) => PerkPatchMath.CraftReduction(b, s));
                Check("artisan", PerkCheckForm.Inverse, (b, s) => PerkPatchMath.BitcoinSpeed(b, s));

                void Check(string id, PerkCheckForm form, Func<float, float, float> patch, float extra = 1f, bool negate = false)
                {
                    var entry = catalog.Single(item => item.Id == id);
                    var scale = (float)PerkAmounts.Ratio(entry, tier);
                    var value = entry.Amount * PerkAmounts.Ratio(entry, tier);
                    var input = negate ? -before : before;
                    var after = patch(before, scale);
                    var want = PerkPatchCheck.Expected(form, input, value, extra);
                    Assert.True(PerkPatchCheck.Matches(after, want), $"{id} T{tier} {form}: {input} -> {after}, want {want}");
                }
            }
        }

        // 다른 값이 나오면 FAIL 로 잡힌다
        Assert.False(PerkPatchCheck.Matches(1.2f, PerkPatchCheck.Expected(PerkCheckForm.Add, 1f, 80)));
        Assert.True(float.IsNaN(PerkPatchCheck.Expected(PerkCheckForm.None, 1f, 80)));
    }

    [Fact]
    public void Active_config_line_lists_the_tier_values()
    {
        var info = PerkStateDto.Parse(JsonSerializer.Serialize(new PerkState { ActivePerksEnabled = true, ActiveCatalog = PerkCatalog.ActiveEntries }))!
            .ActiveCatalog.Single(entry => entry.Id == "composure").Active;

        Assert.Equal("cooldown 300 s, duration 60 s, reduction 15, inertia 10", info.Describe(3));
        Assert.Equal("restore 30", PerkStateDto.Parse(JsonSerializer.Serialize(new PerkState { ActivePerksEnabled = true, ActiveCatalog = PerkCatalog.ActiveEntries }))!
            .ActiveCatalog.Single(entry => entry.Id == "emergencysurgery").Active.Describe(1));
    }

    [Fact]
    public void Client_defaults_match_the_server_defaults()
    {
        var catalog = PerkStateDto.Parse(JsonSerializer.Serialize(new PerkState { Catalog = PerkCatalog.Entries }))!.Catalog;

        PerkPatchMath.Configure(null!);
        var fallback = PerkPatchMath.Describe();
        PerkPatchMath.Configure(catalog);

        Assert.Equal(fallback, PerkPatchMath.Describe());
    }

    [Fact]
    public void Old_tier_effect_ratio_setting_only_warns()
    {
        var config = PerkConfigLoader.ParseModConfig("""{ "perks": { "tierEffectRatio": [0.3, 0.6, 1.0], "freeRerolls": 5 } }""");

        var validated = PerkConfigLoader.Validated(config, warnings.Add);

        Assert.Contains(warnings, warning => warning.Contains("tierEffectRatio", StringComparison.Ordinal));
        Assert.Equal(5, validated.Perks.FreeRerolls); // 다른 perks 설정은 그대로
    }
}
