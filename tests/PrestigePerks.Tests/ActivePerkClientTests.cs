using PrestigePerks.Model;
using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

/// <summary>클라이언트 액티브 특전 모델 (dev-docs 93 Task 6). 서버가 직렬화한 state 를 클라이언트가 읽은 것으로 본다.</summary>
public class ActivePerkClientTests
{
    private static PerkStateDto State(bool enabled = true, string? activePerk = "grit", bool locked = false, bool fixedSlot = false, int level = 3)
    {
        var server = new PerkState
        {
            PrestigeLevel = level,
            Slots = [new PerkSlotState { Perk = "porter" }, new PerkSlotState { Perk = "control", Locked = true }, new PerkSlotState()],
            ActivePerksEnabled = enabled,
            ActivePerkSlot = new PerkSlotState { Perk = activePerk, Locked = locked, Fixed = fixedSlot },
            ActivePerkTier = activePerk == null ? 0 : 1,
            Owned = new Dictionary<string, int> { ["porter"] = 1, ["grit"] = 2, ["firstaid"] = 1 },
            NewPerks = ["grit"],
            TierCap = 2,
        };
        return PerkStateDto.Parse(System.Text.Json.JsonSerializer.Serialize(server))!;
    }

    [Fact]
    public void Reads_the_active_fields()
    {
        var state = State();
        Assert.True(state.ActivePerksEnabled);
        Assert.Equal("grit", state.ActivePerkSlot.Perk);
        Assert.Equal(10, state.ActiveCatalog.Count);
        Assert.Equal([80, 200, 350], state.ActivePerkPrices);
        var info = state.ActiveInfo("secondwind")!.Active;
        Assert.False(info.IsOncePerRaid);
        Assert.True(state.ActiveInfo("grit")!.Active.IsOncePerRaid);
        Assert.Equal(240, info.CooldownAt(2));
        Assert.Equal(0, info.DurationAt(2));
        Assert.Equal(75, state.ActiveInfo("sonarpulse")!.Active.Value("radius", 2));
        Assert.Equal(0, state.ActiveInfo("sonarpulse")!.Active.Value("missing", 2));
        Assert.Null(state.Info("grit"));
        Assert.NotNull(state.AnyInfo("grit"));
        Assert.NotNull(state.AnyInfo("porter"));
        Assert.Equal(2, state.Owned["grit"]);
        Assert.Equal(["grit"], state.NewPerks);
    }

    [Fact]
    public void Turned_off_active_perks_disappear_from_the_state()
    {
        var state = State(enabled: false);
        Assert.Empty(state.ActiveCatalog);
        Assert.Null(state.ActivePerkSlot);
        Assert.False(state.Owned.ContainsKey("grit"));
        Assert.Empty(state.NewPerks);
        Assert.Equal(1, PerkPageModel.OwnedCount(state));
        Assert.DoesNotContain(PerkPageModel.ActiveSlot, PerkPageModel.SpinTargets(state));
        Assert.Equal(-1, PerkPageModel.SlotOf(state, "grit"));
    }

    [Fact]
    public void Slot_six_is_the_active_slot()
    {
        var state = State();
        Assert.Equal(6, PerkPageModel.ActiveSlot);
        Assert.True(PerkPageModel.IsSlotOpen(state, PerkPageModel.ActiveSlot));
        Assert.False(PerkPageModel.IsSlotOpen(state, 5));
        Assert.Equal(SlotKind.Roll, PerkPageModel.KindOf(state, PerkPageModel.ActiveSlot));
        Assert.Equal("grit", PerkPageModel.PerkInSlot(state, PerkPageModel.ActiveSlot));
        Assert.Equal(PerkPageModel.ActiveSlot, PerkPageModel.SlotOf(state, "grit"));
        Assert.Equal([0, 2, PerkPageModel.ActiveSlot], PerkPageModel.SpinTargets(state));
        Assert.Equal(1, PerkPageModel.LockedCount(state));
        Assert.Equal(2, PerkPageModel.LockedCount(State(locked: true)));
        Assert.Equal(1, PerkPageModel.FixedCount(State(fixedSlot: true)));
        Assert.True(PerkPageModel.CanDragSlot(State(fixedSlot: true), PerkPageModel.ActiveSlot));
        Assert.Equal(SlotKind.Empty, PerkPageModel.KindOf(State(activePerk: null), PerkPageModel.ActiveSlot));
    }

    [Fact]
    public void Closed_active_slot_opens_at_three()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new PerkState
        {
            PrestigeLevel = 2,
            Slots = [new PerkSlotState { Perk = "porter" }],
            ActivePerksEnabled = true,
            ActivePerkOpensAt = 3,
        });
        var state = PerkStateDto.Parse(json)!;
        Assert.Equal(SlotKind.Closed, PerkPageModel.KindOf(state, PerkPageModel.ActiveSlot));
        Assert.Equal(3, PerkPageModel.OpensAt(state, PerkPageModel.ActiveSlot));
        Assert.Equal([0], PerkPageModel.SpinTargets(state));
    }

    [Fact]
    public void Collection_counts_are_split_by_kind()
    {
        var state = State();
        Assert.Equal(1, PerkPageModel.OwnedCount(state));
        Assert.Equal(2, PerkPageModel.ActiveOwnedCount(state));
        Assert.Equal(PerkCatalog.ActiveIds, PerkPageModel.ActivePerks(state));
        Assert.DoesNotContain("grit", PerkPageModel.Perks(state));
        Assert.True(PerkPageModel.IsActivePerk(state, "grit"));
        Assert.False(PerkPageModel.IsActivePerk(state, "porter"));
    }

    [Fact]
    public void Pools_and_roll_check_follow_both_kinds()
    {
        var state = State();
        Assert.All(PerkPageModel.RollPool(state), perk => Assert.Null(state.ActiveInfo(perk)));
        Assert.DoesNotContain("grit", PerkPageModel.ActiveRollPool(state));
        Assert.DoesNotContain("firstaid", PerkPageModel.ActiveRollPool(state));
        Assert.Contains("secondwind", PerkPageModel.ActiveRollPool(state));

        var onlyActive = State();
        foreach (var slot in onlyActive.Slots)
        {
            slot.Locked = true;
        }

        Assert.Equal(RollBlock.None, PerkPageModel.CheckRoll(onlyActive, 999, false));
        onlyActive.ActivePerkSlot.Locked = true;
        Assert.Equal(RollBlock.NothingToRoll, PerkPageModel.CheckRoll(onlyActive, 999, false));
    }

    [Fact]
    public void Drops_need_the_matching_kind()
    {
        var state = State();
        Assert.True(PerkPageModel.CanDrop(state, PerkPageModel.ActiveSlot, "firstaid"));
        Assert.False(PerkPageModel.CanDrop(state, PerkPageModel.ActiveSlot, "porter"));
        Assert.False(PerkPageModel.CanDrop(state, 2, "firstaid"));
        Assert.True(PerkPageModel.CanDrop(state, 2, "porter"));
        Assert.False(PerkPageModel.CanDrop(State(locked: true), PerkPageModel.ActiveSlot, "firstaid"));
    }

    [Fact]
    public void Active_selection_uses_active_prices_and_the_applied_tier()
    {
        var state = State(fixedSlot: true);
        state.ActivePerkTier = 2;
        var selection = PerkPageModel.Select(state, "grit", 999);
        Assert.True(selection.Active);
        Assert.Equal(SelectionKind.Fixed, selection.Kind);
        Assert.Equal(PerkPageModel.ActiveSlot, selection.Slot);
        Assert.Equal(2, selection.Tier);
        Assert.Equal(3, selection.NextTier);
        Assert.Equal(350, selection.Price);
        Assert.Equal(BuyBlock.Capped, selection.Block);

        var roll = PerkPageModel.Select(State(activePerk: "secondwind"), "secondwind", 999);
        Assert.Equal(SelectionKind.RollSlot, roll.Kind);
        Assert.Equal(80, roll.Price);
        Assert.Equal(BuyBlock.NotEnoughCoins, PerkPageModel.Select(State(activePerk: "secondwind"), "secondwind", 79).Block);
        Assert.False(PerkPageModel.Select(state, "porter", 999).Active);
        Assert.Equal(100, PerkPageModel.Select(state, "porter", 999).Price);
    }

    [Theory]
    [InlineData("Stop bleeding, regen {regen}/s · {duration} s", 2, "Stop bleeding, regen 4/s · 45 s")]
    [InlineData("Cooldown {cooldown} s", 3, "Cooldown 0 s")]
    [InlineData("{missing} stays", 1, "{missing} stays")]
    [InlineData("", 1, "")]
    public void Text_fills_named_values_for_the_tier(string template, int tier, string expected)
    {
        Assert.Equal(expected, ActivePerkText.Fill(template, State().ActiveInfo("firstaid")!.Active, tier));
    }

    [Fact]
    public void Numbers_drop_zero_decimals()
    {
        Assert.Equal("22.5", ActivePerkText.Number(22.5));
        Assert.Equal("300", ActivePerkText.Number(300));
        Assert.Equal("−30%", ActivePerkText.Fill("−{reduction}%", State().ActiveInfo("composure")!.Active, 1));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("kr")]
    public void Locale_files_describe_every_active_perk(string culture)
    {
        var file = PerkLocale.ParseFile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClientLocales", $"{culture}.json")));
        foreach (var perk in PerkCatalog.ActiveIds)
        {
            foreach (var field in new[] { "name", "effect", "detail", "value", "running" })
            {
                Assert.False(string.IsNullOrWhiteSpace(file.GetValueOrDefault($"perk.{perk}.{field}")), $"{culture}: perk.{perk}.{field}");
            }
        }

        foreach (var key in new[] { "ui.levelActive", "ui.typePassive", "ui.typeActive", "ui.oncePerRaid", "ui.cooldownSeconds", "ui.activeSlot", "ui.newActivePerk", "hud.ready", "hud.cooldown", "hud.used", "hud.noTarget", "hud.running" })
        {
            Assert.False(string.IsNullOrWhiteSpace(file.GetValueOrDefault(key)), $"{culture}: {key}");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("kr")]
    public void Locale_files_name_every_f12_setting(string culture)
    {
        var file = PerkLocale.ParseFile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClientLocales", $"{culture}.json")));
        var plugin = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClientSource", "Plugin.cs.txt"));
        var tags = System.Text.RegularExpressions.Regex.Matches(plugin, "ConfigText\\.Tag\\(\"(\\w+)\", \"(\\w+)\"");
        Assert.Equal(11, tags.Count);
        foreach (System.Text.RegularExpressions.Match tag in tags)
        {
            foreach (var key in new[] { $"config.section.{tag.Groups[1].Value}", $"config.{tag.Groups[2].Value}.name", $"config.{tag.Groups[2].Value}.desc" })
            {
                Assert.False(string.IsNullOrWhiteSpace(file.GetValueOrDefault(key)), $"{culture}: {key}");
            }
        }
    }

    [Fact]
    public void Every_active_perk_has_an_icon()
    {
        foreach (var perk in PerkCatalog.ActiveIds)
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ClientIcons", $"perk_{perk}.png")), $"perk_{perk}.png");
        }
    }

    [Fact]
    public void Client_active_ids_match_the_server_catalog()
    {
        Assert.Equal(PerkCatalog.ActiveIds, ActivePerkIds.All);
    }

    [Fact]
    public void Active_list_is_two_columns_of_five()
    {
        Assert.Equal(5, PerkListLayout.ActiveRowsPerColumn(10));
        Assert.Equal(1, PerkListLayout.ActiveRowsPerColumn(1));
    }

    // 확인 받을 것 1: 쿨타임은 효과가 끝난 뒤부터
    [Fact]
    public void Cooldown_perk_runs_then_cools_down_then_is_ready()
    {
        var clock = new ActivePerkClock(oncePerRaid: false, duration: 10, cooldown: 60);
        Assert.Equal(PressResult.Started, clock.Press(0, true));
        Assert.Equal(ActivePhase.Running, clock.Phase);
        Assert.Equal(PressResult.AlreadyRunning, clock.Press(5, true));
        Assert.Equal(0.5f, clock.Fraction(5), 3);
        Assert.False(clock.Tick(9.9f));
        Assert.True(clock.Tick(10));
        Assert.Equal(ActivePhase.Cooldown, clock.Phase);
        Assert.Equal(60, clock.Remaining(10), 3);
        Assert.Equal(PressResult.Cooling, clock.Press(30, true));
        Assert.False(clock.Tick(70));
        Assert.Equal(ActivePhase.Ready, clock.Phase);
    }

    [Fact]
    public void Instant_once_per_raid_perk_is_spent_at_once()
    {
        var clock = new ActivePerkClock(oncePerRaid: true, duration: 0, cooldown: 0);
        Assert.Equal(PressResult.Started, clock.Press(0, true));
        Assert.Equal(ActivePhase.Used, clock.Phase);
        Assert.Equal(PressResult.Spent, clock.Press(1, true));
    }

    [Fact]
    public void Instant_cooldown_perk_cools_down_from_the_press()
    {
        var clock = new ActivePerkClock(oncePerRaid: false, duration: 0, cooldown: 300);
        clock.Press(0, true);
        Assert.Equal(ActivePhase.Cooldown, clock.Phase);
        Assert.Equal(300, clock.Remaining(0), 3);
    }

    [Fact]
    public void No_target_does_not_spend_the_use()
    {
        var clock = new ActivePerkClock(oncePerRaid: true, duration: 30, cooldown: 0);
        Assert.Equal(PressResult.NoTarget, clock.Press(0, false));
        Assert.Equal(ActivePhase.Ready, clock.Phase);
    }

    [Fact]
    public void Force_end_and_reset()
    {
        var clock = new ActivePerkClock(oncePerRaid: true, duration: 30, cooldown: 0);
        clock.Press(0, true);
        clock.ForceEnd(3);
        Assert.Equal(ActivePhase.Used, clock.Phase);
        clock.Reset();
        Assert.Equal(ActivePhase.Ready, clock.Phase);
    }

    [Theory]
    [InlineData(ActivePhase.Ready, true, HudLook.Ready)]
    [InlineData(ActivePhase.Ready, false, HudLook.NoTarget)]
    [InlineData(ActivePhase.Running, false, HudLook.Running)]
    [InlineData(ActivePhase.Cooldown, true, HudLook.Cooldown)]
    [InlineData(ActivePhase.Used, true, HudLook.Used)]
    public void Hud_look_follows_the_phase_and_target(ActivePhase phase, bool hasTarget, HudLook expected)
    {
        Assert.Equal(expected, ActivePerkHudLook.Of(phase, hasTarget));
    }

    [Theory]
    [InlineData(135f, "2:15")]
    [InlineData(59.2f, "1:00")]
    [InlineData(0.4f, "0:01")]
    public void Cooldown_text_is_minutes_and_seconds_rounded_up(float seconds, string expected)
    {
        Assert.Equal(expected, ActivePerkHudLook.Clock(seconds));
    }
}
