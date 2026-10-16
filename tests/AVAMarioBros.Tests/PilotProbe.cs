using AVAMarioBros.Engine;

namespace AVAMarioBros.Tests;

/// <summary>How the computer player gets on, phase by phase (set MARIOBROS_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that just runs back and forth jumping).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("MARIOBROS_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "mariobros-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 12 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 400 < 200 ? 1 : -1, Jump: t % 50 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: phase {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   pests {g.Pests.Count} + {g.ToCome} lives {g.Lives}: " + string.Join("; ", g.Pests.Select(e => $"{e.Kind} {e.State} at ({e.X:0},{e.Y:0}) dir {e.Dir} vy {e.Vy:0} hop {e.Hop}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
