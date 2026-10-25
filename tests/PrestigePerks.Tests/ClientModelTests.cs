using PrestigePerks.Model;
using PrestigePerks.Perks;
using PrestigePerks.Saving;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace PrestigePerks.Tests;

/// <summary>클라이언트 모델 (client/PrestigePerks.Client/Model). 서버가 실제로 직렬화한 JSON 을 클라이언트가 읽는지까지 본다.</summary>
public class ClientModelTests
{
    private static PerkStateDto State(int cost, params (int Perk, bool Locked)[] slots) => new()
    {
        PrestigeLevel = slots.Length,
        Slots = slots.Select(slot => new PerkSlotDto { Perk = slot.Perk, Locked = slot.Locked }).ToList(),
        NextRerollCost = cost,
    };

    [Fact]
    public void Reads_the_json_the_server_sends()
    {
        var server = new PerkState
        {
            PrestigeLevel = 3,
            Slots = [new PerkSlot { Perk = 1, Locked = true }, new PerkSlot { Perk = 4 }, new PerkSlot { Perk = 8 }],
            ActivePerks = [1, 4, 8],
            FreeRerollsLeft = 0,
            PaidRerolls = 2,
            NextRerollCost = 180,
            CostFixed = true,
            NewPerks = [8],
            Bonuses = PerkBonusApplier.CreateBonuses([1, 4, 8]),
            BonusIds = PerkCatalog.FilteredBonusIds.Select(id => id.ToString()).ToList(),
        };
        var json = new JsonUtil([new SptJsonConverterRegistrator()]).Serialize(server)!;

        var state = PerkStateDto.Parse(json)!;

        Assert.Equal(3, state.PrestigeLevel);
        Assert.Equal([1, 4, 8], state.Slots.Select(slot => slot.Perk));
        Assert.Equal([true, false, false], state.Slots.Select(slot => slot.Locked));
        Assert.Equal(0, state.FreeRerollsLeft);
        Assert.Equal(2, state.PaidRerolls);
        Assert.Equal(180, state.NextRerollCost);
        Assert.True(state.CostFixed);
        Assert.Equal([8], state.NewPerks);
        Assert.Null(state.Error);
        Assert.Equal(6, state.BonusIds.Count);

        // 클라이언트 BonusDescriptor 로 바꿀 때 필요한 값: 이름 문자열 enum, ID, 음수 값
        Assert.Equal(4, state.Bonuses.Count);
        var experience = state.Bonuses.Single(bonus => bonus.Id == "8bcbeb65b78be907af6ee2b0");
        Assert.Equal("ExperienceRate", experience.Type);
        Assert.Equal(30, experience.Value);
        // 서버는 BonusSkillType 을 숫자로 보낸다. 클라이언트 ESkillClass 도 Physical, Combat, Special, Practical, Mental 순이라 Enum.TryParse 가 맞게 읽는다
        Assert.Equal([((int)BonusSkillType.Practical).ToString(), ((int)BonusSkillType.Mental).ToString()],
            state.Bonuses.Where(bonus => bonus.Type == "SkillGroupLevelingBoost").Select(bonus => bonus.SkillType));
        Assert.Equal(["Physical", "Combat", "Special", "Practical", "Mental"], Enum.GetNames<BonusSkillType>());
        var scav = state.Bonuses.Single(bonus => bonus.Type == "ScavCooldownTimer");
        Assert.Equal(-30, scav.Value);
        Assert.True(scav.Passive);
        Assert.False(scav.Production);
        Assert.True(scav.Visible);
    }

    [Fact]
    public void Reads_the_collection_and_tier_fields()
    {
        // SPT JsonUtil 은 정적 옵션을 고쳐 다른 테스트와 겹치므로 System.Text.Json 으로 직접 쓴다
        var config = new PerkConfig();
        var server = new PerkState
        {
            PrestigeLevel = 3,
            Slots = [new PerkSlot { Perk = 14, Fixed = true }, new PerkSlot(), new PerkSlot { Perk = 29, Locked = true }],
            ActiveTiers = new Dictionary<int, int> { [14] = 2, [29] = 1 },
            Owned = new Dictionary<int, int> { [14] = 3, [26] = 1 },
            TierCap = 2,
            TierOpensAt = [config.TierOpensAt(3, 2), config.TierOpensAt(3, 3)],
            TierPrices = config.TierPrices,
            TierEffectRatio = config.TierEffectRatio,
            NewTierCap = 2,
            BuyCost = 100,
        };
        var state = PerkStateDto.Parse(System.Text.Json.JsonSerializer.Serialize(server))!;

        Assert.Equal([true, false, false], state.Slots.Select(slot => slot.Fixed));
        Assert.True(state.Slots[1].IsEmpty);
        Assert.Equal(2, state.ActiveTiers[14]);
        Assert.Equal(3, state.Owned[14]);
        Assert.Equal(2, state.TierCap);
        Assert.Equal([null, 5], state.TierOpensAt);
        Assert.Equal([40, 100, 200], state.TierPrices);
        Assert.Equal([0.5, 0.75, 1.0], state.TierEffectRatio);
        Assert.Equal(2, state.NewTierCap);
        Assert.Equal(100, state.BuyCost);
    }

    [Fact]
    public void Reads_the_slot_flags()
    {
        var server = new PerkState { PrestigeLevel = 2, FreeRerolls = 99, SlotOpensAt = new PerkConfig().SlotOpensAt(2) };
        var state = PerkStateDto.Parse(System.Text.Json.JsonSerializer.Serialize(server))!;
        Assert.Equal(99, state.FreeRerolls);
        Assert.Null(PerkPageModel.OpensAt(state, 1));
        Assert.Equal(3, PerkPageModel.OpensAt(state, 2));
        Assert.Equal(6, PerkPageModel.OpensAt(state, 5));
        Assert.Null(PerkPageModel.OpensAt(state, 6));

        // 예전 서버 응답(단계 필드 없음)도 비어 있는 것으로 읽는다
        var old = PerkStateDto.Parse("""{ "prestigeLevel": 1, "owned": null }""")!;
        Assert.Empty(old.Owned);
        Assert.Equal(1, old.TierCap);
    }

    [Fact]
    public void Error_responses_parse()
    {
        var state = PerkStateDto.Parse("""{ "prestigeLevel": 0, "slots": null, "error": "NoSlots" }""")!;
        Assert.Equal("NoSlots", state.Error);
        Assert.Empty(state.Slots);
        Assert.Empty(state.Bonuses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ broken")]
    [InlineData(null)]
    public void Bad_json_gives_null(string? json)
    {
        Assert.Null(PerkStateDto.Parse(json!));
    }

    [Fact]
    public void Roll_button_state()
    {
        var free = State(0, (1, false), (4, true));
        Assert.Equal(RollBlock.None, PerkPageModel.CheckRoll(free, inventoryCoins: 0, busy: false));
        Assert.Equal(RollBlock.Busy, PerkPageModel.CheckRoll(free, 0, busy: true));

        var paid = State(90, (1, false), (4, true));
        Assert.Equal(RollBlock.NotEnoughCoins, PerkPageModel.CheckRoll(paid, 89, false));
        Assert.Equal(RollBlock.None, PerkPageModel.CheckRoll(paid, 90, false));

        Assert.Equal(RollBlock.NothingToRoll, PerkPageModel.CheckRoll(State(0, (1, true), (4, true)), 999, false));
        Assert.Equal(RollBlock.NoSlots, PerkPageModel.CheckRoll(State(0), 999, false));
        Assert.Equal(RollBlock.NoSlots, PerkPageModel.CheckRoll(null!, 999, false));
    }

    [Fact]
    public void Spin_targets_are_the_unlocked_slots()
    {
        var state = State(0, (1, true), (4, false), (8, true), (9, false));
        Assert.Equal([1, 3], PerkPageModel.SpinTargets(state));
        Assert.Equal(2, PerkPageModel.LockedCount(state));
        Assert.Equal(2, PerkPageModel.SlotOf(state, 8));
        Assert.Equal(-1, PerkPageModel.SlotOf(state, 24));
        Assert.True(PerkPageModel.IsSlotOpen(state, 3));
        Assert.False(PerkPageModel.IsSlotOpen(state, 4));
    }

    private static PerkStateDto Collection()
    {
        // P3, 상한 II: [제어 II 확정] [빈 칸] [강인 잠김]. 수색 I 은 장착 안 됨
        var state = State(0, (14, false), (0, false), (11, true));
        state.Slots[0].Fixed = true;
        state.TierCap = 2;
        state.Owned = new Dictionary<int, int> { [14] = 3, [26] = 1 };
        state.ActiveTiers = new Dictionary<int, int> { [14] = 2, [11] = 1 };
        state.TierPrices = [40, 100, 200];
        state.TierEffectRatio = [0.5, 0.75, 1.0];
        state.TierOpensAt = [null, 5];
        return state;
    }

    [Fact]
    public void Slot_kinds_and_roll_targets_follow_fixed_locked_and_empty_slots()
    {
        var state = Collection();
        Assert.Equal([SlotKind.Fixed, SlotKind.Empty, SlotKind.Locked, SlotKind.Closed], Enumerable.Range(0, 4).Select(i => PerkPageModel.KindOf(state, i)));
        Assert.Equal([1], PerkPageModel.SpinTargets(state));
        Assert.Equal(1, PerkPageModel.FixedCount(state));
        Assert.Equal(1, PerkPageModel.LockedCount(state));
        Assert.Equal(2, PerkPageModel.OwnedCount(state));
        Assert.Equal(-1, PerkPageModel.SlotOf(state, 0));

        var pool = PerkPageModel.RollPool(state);
        Assert.DoesNotContain(14, pool);
        Assert.DoesNotContain(26, pool);
        Assert.DoesNotContain(11, pool);
        Assert.Equal(17, pool.Count);
    }

    [Fact]
    public void Auto_select_takes_the_first_slot_with_a_perk()
    {
        // [빈 칸] [강인 잠김] [제어]: 빈 칸을 건너뛰고 강인
        var state = State(0, (0, false), (11, true), (14, false));
        Assert.Equal(11, PerkPageModel.FirstSlotPerk(state));
        Assert.Equal(0, PerkPageModel.PerkInSlot(state, 0));
        Assert.Equal(14, PerkPageModel.PerkInSlot(state, 2));
        Assert.Equal(0, PerkPageModel.PerkInSlot(state, 3)); // 닫힌 칸
        Assert.Equal(0, PerkPageModel.PerkInSlot(state, -1));

        Assert.Equal(0, PerkPageModel.FirstSlotPerk(State(0, (0, false), (0, false))));
        Assert.Equal(0, PerkPageModel.FirstSlotPerk(State(0)));
        Assert.Equal(0, PerkPageModel.FirstSlotPerk(null!));
    }

    [Fact]
    public void Roll_is_blocked_when_the_collection_is_complete()
    {
        var state = State(0, (0, false));
        state.Owned = PerkPageModel.AllPerks.ToDictionary(perk => perk, _ => 1);
        Assert.Equal(RollBlock.CollectionComplete, PerkPageModel.CheckRoll(state, 999, false));
    }

    [Fact]
    public void Shown_tier_is_applied_tier_in_a_slot_owned_tier_up_to_the_cap_otherwise_one()
    {
        var state = Collection();
        Assert.Equal(2, PerkPageModel.ShownTier(state, 14)); // III 보유, 상한 II
        Assert.Equal(1, PerkPageModel.ShownTier(state, 11)); // 미보유, 잠긴 칸
        Assert.Equal(1, PerkPageModel.ShownTier(state, 26)); // I 보유, 장착 안 됨
        Assert.Equal(1, PerkPageModel.ShownTier(state, 24)); // 미보유
        Assert.Equal(0, PerkPageModel.AppliedTier(state, 26));
        Assert.Equal("−10%", PerkPageModel.EffectValue(state, 11, 1));
        Assert.Equal("−7.5%", PerkPageModel.EffectValue(state, 14, 2));
    }

    [Fact]
    public void Drag_rules_allow_only_unequipped_owned_rows_and_fixed_slots_onto_unlocked_slots()
    {
        var state = Collection();
        Assert.True(PerkPageModel.CanDragRow(state, 26));
        Assert.False(PerkPageModel.CanDragRow(state, 14)); // 이미 칸에 있음
        Assert.False(PerkPageModel.CanDragRow(state, 24)); // 미보유
        Assert.True(PerkPageModel.CanDragSlot(state, 0));
        Assert.False(PerkPageModel.CanDragSlot(state, 2));
        Assert.Equal([true, true, false, false], Enumerable.Range(0, 4).Select(i => PerkPageModel.CanDrop(state, i)));
    }

    [Fact]
    public void Selection_states_and_buy_buttons()
    {
        var state = Collection();
        state.Slots.Add(new PerkSlotDto { Perk = 29 });

        var roll = PerkPageModel.Select(state, 29, 500);
        Assert.Equal((SelectionKind.RollSlot, 1, 40, BuyBlock.None), (roll.Kind, roll.NextTier, roll.Price, roll.Block));

        var locked = PerkPageModel.Select(state, 11, 20);
        Assert.Equal((SelectionKind.LockedSlot, 1, BuyBlock.NotEnoughCoins), (locked.Kind, locked.NextTier, locked.Block));

        // 제어: III 보유라 살 단계 없음
        var fixedPerk = PerkPageModel.Select(state, 14, 500);
        Assert.Equal((SelectionKind.Fixed, 0, 2), (fixedPerk.Kind, fixedPerk.NextTier, fixedPerk.Tier));

        var owned = PerkPageModel.Select(state, 26, 500);
        Assert.Equal((SelectionKind.Owned, 2, 100, BuyBlock.None), (owned.Kind, owned.NextTier, owned.Price, owned.Block));

        state.Owned[26] = 2;
        var capped = PerkPageModel.Select(state, 26, 500);
        Assert.Equal((3, BuyBlock.Capped, (int?)5), (capped.NextTier, capped.Block, capped.OpensAt));

        var notShown = PerkPageModel.Select(state, 24, 500);
        Assert.Equal((SelectionKind.NotShown, 0), (notShown.Kind, notShown.NextTier));
        Assert.Equal(SelectionKind.None, PerkPageModel.Select(state, 0, 500).Kind);
        Assert.Equal(SelectionKind.None, PerkPageModel.Select(null!, 14, 500).Kind);
    }

    [Theory]
    [InlineData(14, 0.5, "−5%")]
    [InlineData(9, 0.75, "+15%")]
    [InlineData(30, 0.75, "+7.5")]
    [InlineData(11, 0.75, "−15%")]
    [InlineData(32, 0.75, "+3.8%")]
    [InlineData(10, 0.75, "+11.3%")]
    [InlineData(1, 1.0, "+30%")]
    public void Effect_values_scale_and_round_to_one_decimal(int perk, double ratio, string expected)
    {
        Assert.Equal(expected, PerkAmounts.Format(perk, ratio));
    }

    [Theory]
    [InlineData(1, 0.75, "+22.5%p")]
    [InlineData(8, 1.0, "−30%p")]
    [InlineData(14, 0.75, "−7.5%p")]
    [InlineData(27, 0.75, "+18.8%p")]
    [InlineData(11, 0.5, "−10%")] // 곱하는 특전은 그대로 %
    [InlineData(24, 1.0, "+30%")]
    [InlineData(30, 1.0, "+10")] // 고정값은 단위 없음
    public void Added_perks_take_the_point_unit_and_multiplied_perks_keep_percent(int perk, double ratio, string expected)
    {
        Assert.Equal(expected, PerkAmounts.Format(perk, ratio, "%p"));
    }

    [Fact]
    public void Tier_ratio_falls_back_to_defaults()
    {
        Assert.Equal(0.75, PerkAmounts.Ratio(null!, 2));
        Assert.Equal(0.4, PerkAmounts.Ratio([0.4, 0.6, 0.9], 1));
        Assert.Equal(0, PerkAmounts.Ratio([0.5, 0.75, 1.0], 4));
    }

    [Fact]
    public void Bonus_perk_amounts_match_the_server_catalog()
    {
        foreach (var perk in PerkCatalog.All.Where(perk => perk.Kind == PerkKind.Bonus))
        {
            Assert.All(perk.Bonuses, bonus => Assert.Equal(bonus.Value, PerkAmounts.Amount(perk.Number)));
        }

        Assert.All(PerkCatalog.Numbers, perk => Assert.True(PerkAmounts.IsKnown(perk), $"perk {perk}"));
    }

    [Theory]
    [InlineData("kr")]
    [InlineData("en")]
    public void Effect_texts_take_the_value_and_have_no_numbers(string culture)
    {
        var locale = new PerkLocale(LocaleFile(culture), null!);
        foreach (var perk in PerkCatalog.Numbers)
        {
            var text = LocaleFile(culture)[$"perk.{perk}.effect"];
            Assert.Contains("{0}", text);
            Assert.DoesNotMatch("[0-9]", text.Replace("{0}", ""));
            Assert.EndsWith("+15%", locale.PerkEffect(perk, "+15%"));
            Assert.DoesNotContain("{0}", locale.PerkSubject(perk));
        }
    }

    [Fact]
    public void Client_perk_list_matches_the_server_catalog()
    {
        Assert.Equal(PerkCatalog.Numbers, PerkPageModel.AllPerks);
    }

    private static Dictionary<string, string> LocaleFile(string culture) =>
        PerkLocale.ParseFile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClientLocales", $"{culture}.json")));

    [Theory]
    [InlineData("kr")]
    [InlineData("en")]
    public void Locale_files_name_every_perk(string culture)
    {
        var file = LocaleFile(culture);
        foreach (var perk in PerkCatalog.Numbers)
        {
            foreach (var field in new[] { "name", "effect", "detail" })
            {
                Assert.False(string.IsNullOrWhiteSpace(file.GetValueOrDefault($"perk.{perk}.{field}")), $"{culture}: perk.{perk}.{field}");
            }
        }
    }

    [Fact]
    public void Every_perk_has_an_icon()
    {
        foreach (var perk in PerkCatalog.Numbers)
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ClientIcons", $"perk_{perk}.png")), $"perk_{perk}.png");
        }
    }

    [Fact]
    public void Locale_files_have_the_same_keys()
    {
        Assert.Equal(LocaleFile("en").Keys.Order(), LocaleFile("kr").Keys.Order());
    }

    [Fact]
    public void Locale_falls_back_to_english_then_to_the_key()
    {
        var locale = new PerkLocale(new Dictionary<string, string> { ["a"] = "가 {0}" }, new Dictionary<string, string> { ["a"] = "A", ["b"] = "B {0}" });
        Assert.Equal("가 3", locale.Format("a", 3));
        Assert.Equal("B 7", locale.Format("b", 7));
        Assert.Equal("missing", locale.Get("missing"));
        Assert.Equal("{0} {1}", new PerkLocale(new() { ["x"] = "{0} {1}" }, null!).Format("x", 1));
    }

    [Fact]
    public void Locale_file_splits_game_texts_by_prefix()
    {
        var (texts, game) = PerkLocalePack.Split(new()
        {
            ["ui.title"] = "特典",
            ["game:Prestige/ItemsTransfer/Header"] = "コイン",
            ["game:"] = "ignored",
        });

        Assert.Equal(new Dictionary<string, string> { ["ui.title"] = "特典" }, texts);
        Assert.Equal(new Dictionary<string, string> { ["Prestige/ItemsTransfer/Header"] = "コイン" }, game);
    }

    [Fact]
    public void Locale_file_overrides_the_built_in_texts_and_missing_keys_fall_back_to_english()
    {
        var kr = LocaleFile("kr");
        var pack = new Dictionary<string, string> { ["ui.title"] = "팩 제목" };
        var locale = new PerkLocale(PerkLocalePack.Merge(kr, pack), LocaleFile("en"));

        Assert.Equal("팩 제목", locale.Get("ui.title"));
        Assert.Equal(kr["ui.list"], locale.Get("ui.list"));
        Assert.NotEqual("팩 제목", kr["ui.title"]);

        var partial = new PerkLocale(PerkLocalePack.Merge(null!, pack), LocaleFile("en"));
        Assert.Equal(LocaleFile("en")["ui.list"], partial.Get("ui.list"));
    }
}

public class ReelScheduleTests
{
    private static readonly int[] Pool = PrestigePerks.Model.PerkPageModel.AllPerks;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Strip_puts_the_result_where_the_reel_lands(bool fakeOut)
    {
        var effect = fakeOut ? PrestigePerks.Model.ReelEffect.FakeOut : PrestigePerks.Model.ReelEffect.Normal;
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var strip = new List<int>();
            PrestigePerks.Model.ReelSchedule.Fill(strip, 7, random, Pool, -1);

            var (result, stop) = PrestigePerks.Model.ReelSchedule.Extend(strip, 5, 24, effect, random, Pool);

            Assert.Equal(5 + PrestigePerks.Model.ReelSchedule.LandingItems, result);
            Assert.Equal(24, strip[result]);
            // 가짜 멈춤이면 결과 바로 다음 칸(미끼)에서 멈춘다. 결과 칸은 그 아래에 걸려 보인다
            Assert.Equal(fakeOut ? result + 1 : result, stop);
            Assert.NotEqual(24, strip[stop + (fakeOut ? 0 : 1)]);
            // 멈춘 칸 위로도 보일 칸이 있다
            Assert.True(strip.Count >= stop + 3);
            // 같은 칸이 연달아 나오지 않고, 새로 붙인 칸에는 결과가 한 번만 있다
            for (var i = 1; i < strip.Count; i++)
            {
                Assert.NotEqual(strip[i - 1], strip[i]);
            }

            Assert.Single(strip.Skip(7), perk => perk == 24);
        }
    }

    [Fact]
    public void Landing_starts_at_spin_speed_and_eases_to_a_stop()
    {
        // ease-out cubic 의 처음 기울기 3 × 거리 / 시간 = 빨리 돌 때 속도
        var distance = 12f;
        var duration = PrestigePerks.Model.ReelSchedule.LandingDuration(distance);
        var startSpeed = (PrestigePerks.Model.ReelSchedule.EaseOutCubic(0.001f) - 0) / 0.001f * distance / duration;
        Assert.Equal(PrestigePerks.Model.ReelSchedule.Speed, startSpeed, 0);
        Assert.Equal(0, PrestigePerks.Model.ReelSchedule.EaseOutCubic(0));
        Assert.Equal(1, PrestigePerks.Model.ReelSchedule.EaseOutCubic(1));
        Assert.Equal(1, PrestigePerks.Model.ReelSchedule.EaseOutCubic(2));
    }

    [Fact]
    public void Snap_back_overshoots_then_settles()
    {
        Assert.Equal(0, PrestigePerks.Model.ReelSchedule.EaseOutBack(0), 4);
        Assert.Equal(1, PrestigePerks.Model.ReelSchedule.EaseOutBack(1), 4);
        Assert.Contains(Enumerable.Range(1, 99), i => PrestigePerks.Model.ReelSchedule.EaseOutBack(i / 100f) > 1.05f);
    }
}

public class ReelFakeOutTests
{
    [Fact]
    public void Tug_pulls_toward_the_result_twice_and_lets_go()
    {
        var tugs = PrestigePerks.Model.ReelSchedule.Tugs;
        Assert.Equal(2, tugs.Length);
        // 두 번째가 더 세다
        Assert.True(PrestigePerks.Model.ReelSchedule.Tug(tugs[1].At) > PrestigePerks.Model.ReelSchedule.Tug(tugs[0].At));
        // 움찔 사이와 끝은 거의 제자리 (한 칸 넘게 끌려가지 않는다)
        Assert.True(PrestigePerks.Model.ReelSchedule.Tug(0) < 0.01f);
        Assert.True(PrestigePerks.Model.ReelSchedule.Tug(1) < 0.01f);
        Assert.True(PrestigePerks.Model.ReelSchedule.Tug((tugs[0].At + tugs[1].At) / 2) < tugs[0].Size);
        Assert.All(Enumerable.Range(0, 101), i => Assert.InRange(PrestigePerks.Model.ReelSchedule.Tug(i / 100f), 0f, 0.5f));
    }

    [Fact]
    public void Fake_landing_crawls_longer_at_the_end()
    {
        // 끝 20% 시간 동안 움직이는 거리: 차수가 클수록 작다 = 마지막에 더 천천히 기어 들어온다
        var normal = 1 - PrestigePerks.Model.ReelSchedule.EaseOut(0.8f, 3);
        var fake = 1 - PrestigePerks.Model.ReelSchedule.EaseOut(0.8f, PrestigePerks.Model.ReelSchedule.FakeEasePower);
        Assert.True(fake < normal);
        Assert.Equal(1, PrestigePerks.Model.ReelSchedule.EaseOut(1, PrestigePerks.Model.ReelSchedule.FakeEasePower));
    }
}

public class ReelReverseTests
{
    private static readonly int[] Pool = PrestigePerks.Model.PerkPageModel.AllPerks;

    [Fact]
    public void Reverse_stops_on_a_decoy_and_plants_the_result_behind()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var strip = new List<int>();
            PrestigePerks.Model.ReelSchedule.Fill(strip, 7, random, Pool, -1);

            var (result, stop) = PrestigePerks.Model.ReelSchedule.Extend(strip, 5, 24, PrestigePerks.Model.ReelEffect.Reverse, random, Pool);

            Assert.Equal(5 + PrestigePerks.Model.ReelSchedule.LandingItems, stop);
            Assert.NotEqual(24, strip[stop]);
            // 결과는 이미 지나간 칸, 멈춘 자리에서 보이지 않을 만큼 뒤에 있다
            Assert.Equal(stop - PrestigePerks.Model.ReelSchedule.ReverseItems, result);
            Assert.True(stop - result > 2);
            Assert.Equal(24, strip[result]);
            Assert.NotEqual(24, strip[result - 1]);
            Assert.NotEqual(24, strip[result + 1]);
        }
    }

    [Fact]
    public void Random_effects_are_off_on_normal_rolls()
    {
        // 일반 리롤의 가짜 멈춤·역회전은 껐다 (2026-10-18, 코드는 남겨 둠)
        var random = new Random(7);
        Assert.All(Enumerable.Range(0, 1000), _ => Assert.Equal(PrestigePerks.Model.ReelEffect.Normal, PrestigePerks.Model.ReelSchedule.PickEffect(random)));
    }

    [Fact]
    public void Next_cell_after_the_stop_is_a_perk_no_slot_has()
    {
        var held = new HashSet<int> { 1, 4, 8 };
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var strip = new List<int>();
            PrestigePerks.Model.ReelSchedule.Fill(strip, 7, random, Pool, -1);
            var (_, stop) = PrestigePerks.Model.ReelSchedule.Extend(strip, 5, 8, PrestigePerks.Model.ReelEffect.Normal, random, Pool, held);
            Assert.Equal(8, strip[stop]);
            Assert.DoesNotContain(strip[stop + 1], held);
        }
    }
}
