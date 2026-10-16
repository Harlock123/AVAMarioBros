using AVAMarioBros.Engine;

namespace AVAMarioBros.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamariobros-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A phase in play with nobody about and none to come but one pest put out of the way, so it isn't over; calm unless asked.</summary>
    private static GameEngine Game(int wave = 1, bool calm = true, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.NoMorePests();
        g.Pests.Clear();
        g.Coins.Clear();
        g.PutPest(PestKind.Shellback, 60, Field.TopPipeY).State = PestState.InPipe;
        g.Pests[0].Timer = 9999;
        g.PutPlayer(200, Field.FloorY);
        return g;
    }

    private static readonly Controls JumpButton = new(Jump: true);
    private const double L1 = 564, L2 = 424, L3 = 284;

    // ───────────────────────────── the room ─────────────────────────────

    [Fact]
    public void The_room_wraps_round_so_ledges_at_the_sides_join_up()
    {
        Assert.NotNull(Field.Under(1020, L1));
        Assert.NotNull(Field.Under(4, L1));
        Assert.Null(Field.Under(512, L1));
        Assert.Equal(L2, Field.Below(512, L1 - 200).Y);
        Assert.Equal(24, Field.Dx(1012, 12), 6);
        // A pest walking off the right of a side ledge carries on along it at the left.
        var g = Game(calm: false);
        g.PutPlayer(512, Field.FloorY);
        g.Player.Safe = 1000;
        var e = g.PutPest(PestKind.Shellback, 1010, L1, 1);
        g.Idle(30);
        Assert.Equal(L1, e.Y);
        Assert.InRange(e.X, 0, 60);
    }

    // ───────────────────────────── the mechanic ─────────────────────────────

    [Fact]
    public void He_walks_slides_to_a_stop_and_jumps_high_enough_to_reach_the_next_ledge_up()
    {
        var g = Game();
        g.PutPlayer(512, Field.FloorY);
        g.Run(60, new Controls(Dx: 1));
        Assert.Equal(GameEngine.WalkSpeed, g.Player.Vx, 1);
        double x = g.Player.X;
        g.Idle(30);
        Assert.Equal(0, g.Player.Vx);
        Assert.True(g.Player.X > x + 5);
        // Up through the gap in the middle, steering left onto the ledge.
        g.PutPlayer(400, Field.FloorY);
        g.Run(1, JumpButton);
        g.Run(60, new Controls(Dx: -1));
        Assert.Equal(L1, g.Player.Y);
        Assert.True(g.Player.Grounded);
    }

    [Fact]
    public void Jumping_under_a_ledge_bumps_it_and_knocks_over_a_shellback_on_it_just_there()
    {
        var g = Game();
        g.PutPlayer(200, Field.FloorY);
        var over = g.PutPest(PestKind.Shellback, 210, L1);
        var away = g.PutPest(PestKind.Shellback, 320, L1);
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.Equal(PestState.Flipped, over.State);
        Assert.Equal(PestState.Walking, away.State);
        Assert.Equal(GameEngine.BumpPoints, g.Score);
        Assert.True(g.Player.Y > L1 + Field.Thick);
    }

    [Fact]
    public void A_pest_on_its_back_gets_up_again_quicker_unless_kicked_and_a_second_bump_turns_it_back()
    {
        var g = Game(calm: false);
        g.Player.Safe = 1000;
        g.PutPlayer(200, Field.FloorY);
        var e = g.PutPest(PestKind.Shellback, 200, L1);
        double speed = g.SpeedOf(e);
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.Equal(PestState.Flipped, e.State);
        g.Idle((int)(g.FlipSeconds * 60) + 30);
        Assert.Equal(PestState.Walking, e.State);
        Assert.Equal(1, e.Level);
        Assert.True(g.SpeedOf(e) > speed);
        var h = Game();
        h.PutPlayer(200, Field.FloorY);
        var f = h.PutPest(PestKind.Shellback, 200, L1);
        h.Run(1, JumpButton);
        h.Idle(60);
        Assert.Equal(PestState.Flipped, f.State);
        h.Run(1, JumpButton);
        h.Idle(20);
        Assert.Equal(PestState.Walking, f.State);
    }

    [Fact]
    public void A_clawcrab_takes_two_bumps_the_first_only_making_it_angry()
    {
        var g = Game();
        g.PutPlayer(200, Field.FloorY);
        var e = g.PutPest(PestKind.Clawcrab, 200, L1);
        double speed = g.SpeedOf(e);
        g.Run(1, JumpButton);
        g.Idle(60);
        Assert.True(e.Angry);
        Assert.Equal(PestState.Walking, e.State);
        Assert.True(g.SpeedOf(e) > speed);
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.Equal(PestState.Flipped, e.State);
    }

    [Fact]
    public void A_hopfly_can_only_be_bumped_while_it_is_down_on_the_ledge()
    {
        var g = Game();
        g.PutPlayer(200, Field.FloorY);
        var e = g.PutPest(PestKind.Hopfly, 200, L1 - 40);
        e.Hop = 1;
        e.Vy = -10;
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.NotEqual(PestState.Flipped, e.State);
        e.Y = L1;
        e.Hop = 0;
        e.Vy = 0;
        g.Idle(40);
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.Equal(PestState.Flipped, e.State);
    }

    [Fact]
    public void Kicking_a_pest_off_scores_more_for_each_in_quick_succession_and_sends_out_a_coin()
    {
        var g = Game();
        g.PutPlayer(200, L1);
        foreach (double x in new[] { 240.0, 300 })
        {
            var e = g.PutPest(PestKind.Shellback, x, L1);
            e.State = PestState.Flipped;
            e.Timer = 99;
        }
        g.Run(40, new Controls(Dx: 1));
        Assert.Equal(GameEngine.KickPoints + 2 * GameEngine.KickPoints, g.Score);
        Assert.Equal(2, g.Pests.Count(p => p.State == PestState.Kicked));
        Assert.Equal(2, g.Coins.Count);
        Assert.All(g.Coins, k => Assert.Equal(Field.TopPipeY, k.Y));
    }

    [Fact]
    public void A_pest_on_its_feet_is_the_end_of_him_and_he_comes_back_down_from_the_top()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        g.PutPlayer(200, Field.FloorY);
        g.PutPest(PestKind.Shellback, 240, Field.FloorY, -1);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 300) g.Idle(1);
        Assert.Equal("shellback", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Player.Alive && g.Player.Safe > 0);
        Assert.True(g.Player.Y < 200);
    }

    [Fact]
    public void Pests_drop_off_the_ends_of_ledges_and_go_round_through_the_pipes()
    {
        var g = Game(calm: false);
        g.Player.Safe = 1000;
        g.PutPlayer(512, L2);
        var e = g.PutPest(PestKind.Shellback, 700, L2, 1);
        int t = 0;
        while (e.Y < L1 && t++ < 300) g.Idle(1);
        Assert.Equal(L1, e.Y);
        // Off the floor's corner into a pipe, and out at the top on the other side.
        var f = g.PutPest(PestKind.Shellback, 80, Field.FloorY, -1);
        t = 0;
        while (f.State != PestState.InPipe && t++ < 300) g.Idle(1);
        Assert.Equal(PestState.InPipe, f.State);
        g.Idle((int)(GameEngine.PipeSeconds * 60) + 2);
        Assert.Equal(PestState.Walking, f.State);
        Assert.Equal(Field.TopPipeY, f.Y);
        Assert.InRange(f.X, Field.TopPipeRightX - 5, Field.TopPipeRightX);
        Assert.Equal(-1, f.Dir);
    }

    [Fact]
    public void The_jolt_block_knocks_over_everything_on_the_ledges_three_times_then_it_is_gone()
    {
        var g = Game();
        var a = g.PutPest(PestKind.Shellback, 100, L3);
        var b = g.PutPest(PestKind.Shellback, 900, Field.FloorY);
        for (int i = 0; i < GameEngine.StartJolts; i++)
        {
            g.PutPlayer(Field.JoltX, Field.FloorY);
            a.State = b.State = PestState.Walking;
            a.Vy = b.Vy = 0;
            a.Y = L3;
            b.Y = Field.FloorY;
            g.Run(1, JumpButton);
            g.Idle(40);
            Assert.Equal(PestState.Flipped, a.State);
            Assert.Equal(PestState.Flipped, b.State);
        }
        Assert.Equal(0, g.Jolts);
        g.PutPlayer(Field.JoltX, Field.FloorY);
        g.Run(1, JumpButton);
        g.Idle(20);
        Assert.True(g.Player.Y < Field.JoltTop);
    }

    [Fact]
    public void Coins_are_picked_up_for_800_and_a_rolling_one_is_lost_down_a_bottom_pipe()
    {
        var g = Game(calm: false);
        g.Player.Safe = 1000;
        g.PutPlayer(500, L2);
        g.Coins.Add(new Coin { X = 540, Y = L2, Still = true });
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(GameEngine.CoinPoints, g.Score);
        g.Coins.Add(new Coin { X = 80, Y = Field.FloorY, Dir = -1 });
        g.Idle(60);
        Assert.Empty(g.Coins);
    }

    [Fact]
    public void A_fireball_comes_after_a_while_and_a_bump_under_it_puts_it_out_but_touching_it_kills()
    {
        var g = Game(calm: false);
        g.Player.Safe = 1000;
        g.PutPlayer(512, L2);
        int t = 0;
        while (g.Ember == null && t++ < 60 * 40) g.Idle(1);
        Assert.NotNull(g.Ember);
        Assert.InRange(t / 60.0, g.EmberAfter - 1, g.EmberAfter + 1);
        var h = Game();
        h.PutPlayer(200, Field.FloorY);
        h.SetEmber(200, L1 - 30, 0);
        h.Run(1, JumpButton);
        h.Idle(20);
        Assert.Null(h.Ember);
        Assert.Equal(GameEngine.EmberPoints, h.Score);
        var k = Game(calm: false);
        k.PutPlayer(400, Field.FloorY);
        k.SetEmber(440, Field.FloorY - 20, -100);
        t = 0;
        while (k.Phase == Phase.Play && t++ < 120) k.Idle(1);
        Assert.Equal("fireball", k.Cause);
    }

    [Fact]
    public void The_last_pest_left_goes_quicker_but_never_as_quick_as_the_mechanic()
    {
        var g = Game(calm: false);
        g.Pests.Clear();
        g.Player.Safe = 1000;
        var e = g.PutPest(PestKind.Clawcrab, 600, L2);
        e.Level = 2;
        e.Angry = true;
        double before = g.SpeedOf(e);
        g.Idle(2);
        Assert.True(e.Last);
        Assert.True(g.SpeedOf(e) >= before);
        Assert.True(g.SpeedOf(e) < GameEngine.WalkSpeed);
    }

    // ───────────────────────────── phases and the whole game ─────────────────────────────

    [Fact]
    public void A_phase_ends_when_every_pest_is_kicked_off_and_every_third_is_a_bonus_of_coins()
    {
        var g = Game(2);
        g.Pests.Clear();
        g.PutPlayer(200, L1);
        var e = g.PutPest(PestKind.Shellback, 240, L1);
        e.State = PestState.Flipped;
        e.Timer = 99;
        g.Run(30, new Controls(Dx: 1));
        g.Idle(120);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(3, g.Wave);
        Assert.True(g.IsBonus);
        Assert.Equal(10, g.Coins.Count(k => k.Still));
        Assert.Equal(0, g.ToCome);
        Assert.Equal(GameEngine.StartJolts, g.Jolts);
        // Gathering them all is worth the perfect bonus.
        int score = g.Score;
        g.Coins.Clear();
        g.Idle((int)(GameEngine.StartSeconds * 60) + 2);
        g.Idle(90);
        Assert.Equal(score + GameEngine.PerfectBonus, g.Score);
    }

    [Fact]
    public void Each_round_of_phases_brings_new_pests()
    {
        string Kinds(int wave)
        {
            var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(wave);
            g.Player.Safe = 1000;
            var seen = new HashSet<PestKind>();
            for (int i = 0; i < 60 * 30; i++) { g.Idle(1); foreach (var e in g.Pests) seen.Add(e.Kind); g.Player.Safe = 1000; }
            return string.Join(",", seen.OrderBy(k => k));
        }
        Assert.Equal("Shellback", Kinds(1));
        Assert.Contains("Clawcrab", Kinds(4));
        Assert.Contains("Hopfly", Kinds(7));
        Assert.Equal("Shellback,Clawcrab,Hopfly", Kinds(10));
    }

    [Fact]
    public void An_extra_life_every_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        g.PutPlayer(500, L2);
        g.Coins.Add(new Coin { X = 520, Y = L2, Still = true });
        g.Run(20, new Controls(Dx: 1));
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_life_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game(calm: false);
        g.SetLives(1);
        g.SetScore(99999);
        g.PutPlayer(200, Field.FloorY);
        g.PutPest(PestKind.Shellback, 220, Field.FloorY, -1);
        g.Idle(30);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "PIP") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("PIP", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            Assert.False(g.IsBonus);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, GameEngine.StartJolts), (g.Wave, g.Lives, g.Jolts));
    }

    [Fact]
    public void The_computer_player_clears_the_first_phase()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        for (int i = 0; i < 60 * 180 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on phase {g.Wave}, {g.Pests.Count} pests and {g.ToCome} to come");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(4);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Player.X:0.00} {g.Player.Y:0.00} {g.Pests.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Player.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Player.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
