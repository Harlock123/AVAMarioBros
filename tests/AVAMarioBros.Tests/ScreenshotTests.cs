using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMarioBros.Engine;
using AVAMarioBros.Views;
using Controls = AVAMarioBros.Engine.Controls;

namespace AVAMarioBros.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MARIOBROS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamariobros-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MARIOBROS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMarioBros-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Jump = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 128400, 12);
        g.HighScores.Insert("AVA", 74200, 8);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A phase, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 120)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void A_bump() => Fight(1, "bump", g => g.Bumps.Any(b => b.Age > 0.08 && b.Age < 0.18) && g.Pests.Any(p => p.State == PestState.Flipped) && g.Pests.Count >= 3);

    [AvaloniaFact]
    public void A_kick() => Fight(2, "kick", g => g.Pests.Any(p => p.State == PestState.Kicked && p.Y < 600) && g.Coins.Count >= 1);

    [AvaloniaFact]
    public void Clawcrabs() => Fight(4, "crabs", g => g.Pests.Count(p => p.Kind == PestKind.Clawcrab && p.Upright) >= 3 && g.Pests.Any(p => p.Angry || p.State == PestState.Flipped), seconds: 200);

    [AvaloniaFact]
    public void Hopflies() => Fight(7, "flies", g => g.Pests.Count(p => p.Kind == PestKind.Hopfly && p.Vy != 0) >= 2 && g.Pests.Count >= 4, seconds: 200);

    [AvaloniaFact]
    public void The_fireball() => Fight(2, "fireball", g => g.Ember is { } f && f.X > 150 && f.X < 870 && g.Pests.Count >= 2, seconds: 200);

    [AvaloniaFact]
    public void A_bonus_phase() => Fight(3, "bonus", g => g.BonusLeft < 15 && g.Coins.Count is > 3 and < 9);

    [AvaloniaFact]
    public void The_jolt_block()
    {
        var (w, c, g) = Open(5);
        g.Go(1);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Jolted is > 0.05 and < 0.12 && g.Pests.Any(p => p.State == PestState.Flipped), 300));
        Shot(w, c, "jolt");
    }
}
