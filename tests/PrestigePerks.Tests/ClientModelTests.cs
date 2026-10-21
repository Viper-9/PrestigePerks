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
    public void Reads_the_shake_response()
    {
        var server = new PerkState { PrestigeLevel = 2, Slots = [new PerkSlot { Perk = 3 }, new PerkSlot { Perk = 9 }], ShakeChance = 0.5, ShakeOutcome = nameof(ShakeOutcome.Next) };
        var state = PerkStateDto.Parse(new JsonUtil([new SptJsonConverterRegistrator()]).Serialize(server)!)!;
        Assert.Equal("Next", state.ShakeOutcome);
        Assert.Equal(0.5, state.ShakeChance);
        Assert.Null(PerkStateDto.Parse("""{ "prestigeLevel": 1 }""")!.ShakeOutcome);
    }

    [Fact]
    public void Reads_the_slot_and_shake_flags()
    {
        // SPT JsonUtil 은 정적 옵션을 고쳐 다른 테스트와 겹치므로 System.Text.Json 으로 직접 쓴다
        var server = new PerkState { PrestigeLevel = 2, FreeRerolls = 99, ShakeEnabled = false, SlotOpensAt = new PerkConfig().SlotOpensAt(2) };
        var state = PerkStateDto.Parse(System.Text.Json.JsonSerializer.Serialize(server))!;
        Assert.Equal(99, state.FreeRerolls);
        Assert.False(state.ShakeEnabled);
        Assert.Null(PerkPageModel.OpensAt(state, 1));
        Assert.Equal(3, PerkPageModel.OpensAt(state, 2));
        Assert.Equal(6, PerkPageModel.OpensAt(state, 5));
        Assert.Null(PerkPageModel.OpensAt(state, 6));

        // 값이 없으면 흔들기는 켜진 것으로 본다
        Assert.True(PerkStateDto.Parse("""{ "prestigeLevel": 1 }""")!.ShakeEnabled);
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

        Assert.Equal(RollBlock.AllLocked, PerkPageModel.CheckRoll(State(0, (1, true), (4, true)), 999, false));
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
        // 흔들기와 겹쳐 헷갈려서 일반 리롤의 가짜 멈춤·역회전은 껐다 (리버스는 흔들기 결과로만)
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
