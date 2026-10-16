namespace AVAMarioBros.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It knows the room by having tried it: for every
/// ledge, where walking off an end drops to, and where a jump (straight up, or steering left or right) lands, worked
/// out once with the mechanic's own movement. With that it can get from any ledge to any other.
///
/// Then: a pest on its back, it goes and kicks; a pest on its feet on a ledge above one it can stand on, it gets
/// under where the pest will be and jumps to bump it; a coin, it fetches. It keeps out of the way of pests on its own
/// ledge, jumps over the ones it can't get away from, and on a crowded floor uses the jolt block.
/// </summary>
public sealed class MechanicPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>A way from one ledge to another: walk (or stand) at <see cref="X"/>, then jump steering, or walk off.</summary>
    public sealed record Move(int From, int To, double X, int Steer, bool Jump);

    private static readonly List<Move> Moves = Learn();

    private int _tick;
    private Pest? _target;
    private Move? _move;

    private static int LedgeOf(double x, double y) => Array.FindIndex(Field.Ledges, l => Math.Abs(l.Y - y) <= 2 && l.Covers(x));

    /// <summary>Tries every way off every ledge, every eight pixels, to see where each one ends up.</summary>
    private static List<Move> Learn()
    {
        var moves = new List<Move>();
        for (int from = 0; from < Field.Ledges.Length; from++)
        {
            var l = Field.Ledges[from];
            for (double x = l.X0 + 6; x <= l.X1 - 6; x += 8)
            {
                foreach (int steer in new[] { -1, 0, 1 })
                {
                    var m = new Mechanic { X = x, Y = l.Y, Grounded = true };
                    bool first = true;
                    for (int t = 0; t < 120; t++)
                    {
                        GameEngine.Step(m, new Controls(steer, first), Field.Step, false);
                        first = false;
                        if (m.Grounded) break;
                    }
                    int to = m.Grounded ? LedgeOf(m.X, m.Y) : -1;
                    if (to >= 0 && to != from) moves.Add(new Move(from, to, x, steer, true));
                }
            }
            // Walking off either end.
            foreach (double edge in new[] { l.X0 - 4, l.X1 + 4 })
            {
                if (l.Covers(edge) || l.X0 <= 0 && edge < 0 || l.X1 >= Field.Width && edge > Field.Width) continue;
                var below = Field.Ledges.Where(o => o.Y > l.Y && o.Covers(edge)).OrderBy(o => o.Y).FirstOrDefault();
                if (below != null) moves.Add(new Move(from, Array.IndexOf(Field.Ledges, below), edge < l.X0 ? l.X0 + 2 : l.X1 - 2, edge < l.X0 ? -1 : 1, false));
            }
        }
        return moves;
    }

    /// <summary>The first step on the way from one ledge to another (null if it's already there or there's no way).</summary>
    private static int? NextLedge(int from, int to)
    {
        if (from == to) return null;
        var prev = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            if (at == to) break;
            foreach (var m in Moves.Where(m => m.From == at))
                if (prev.TryAdd(m.To, at)) queue.Enqueue(m.To);
        }
        if (!prev.ContainsKey(to)) return null;
        int step = to;
        while (prev[step] != from) step = prev[step];
        return step;
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; _move = null; return default; }
        // In the air on a planned move: steer it home.
        if (!p.Grounded) { Doing = "in the air"; return new Controls(_move?.Steer ?? 0); }
        int here = LedgeOf(p.X, p.Y);
        if (here < 0) return default;
        _move = null;
        if (Danger(g, p, here) is { } d) return d;
        // Something to do: kick, bump, fetch.
        var (goal, gx, bumpAt) = Choose(g, p, here);
        var target = _target;
        if (goal < 0)
        {
            // Only pests down on the floor: with jolts left, the block will do it.
            if (g.Jolts > 0 && g.Pests.Any(e => e.Upright && e.Grounded && e.Y >= Field.FloorY - 1))
            {
                if (here != 0)
                {
                    var down = Moves.Where(m => m.From == here && Field.Ledges[m.To].Y > Field.Ledges[here].Y).OrderBy(m => Math.Abs(Field.Dx(p.X, m.X))).FirstOrDefault();
                    _move = down;
                    Doing = "to the jolt block";
                    if (down == null) return default;
                    if (Math.Abs(Field.Dx(p.X, down.X)) > 5) return Toward(p, down.X);
                    return new Controls(down.Steer, down.Jump);
                }
                Doing = "jolting";
                if (Math.Abs(Field.Dx(p.X, Field.JoltX)) > 8) return Toward(p, Field.JoltX);
                return new Controls(0, true);
            }
            Doing = "waiting about";
            return Toward(p, Field.Width / 2 - 200);
        }
        if (goal != here)
        {
            int? next = NextLedge(here, goal);
            if (next is not { } n) { Doing = "stuck"; return default; }
            var m = Moves.Where(m => m.From == here && m.To == n).OrderBy(m => Math.Abs(Field.Dx(p.X, m.X))).First();
            _move = m;
            Doing = m.Jump ? "jumping up" : "dropping down";
            if (Math.Abs(Field.Dx(p.X, m.X)) > 5) return Toward(p, m.X);
            if (!m.Jump) return new Controls(m.Steer);
            if (Math.Abs(p.Vx) > 40) return default;
            // Not up into the gap while something above is about to come down through it.
            if (g.Pests.Any(e => !e.Gone && e.Upright && e.Y < p.Y - 4 && p.Y - e.Y < 170 && Math.Abs(Field.Dx(p.X, e.X)) < 110)) { Doing = "waiting to jump"; return default; }
            return new Controls(m.Steer, true);
        }
        if (bumpAt is { } bx)
        {
            // Under the pest's way, and jump as it comes over.
            Doing = "bumping";
            if (Math.Abs(Field.Dx(p.X, gx)) > 10) return Toward(p, gx);
            // A fly only while it's down on the ledge; anything else as it comes over.
            bool now = target is { Kind: PestKind.Hopfly } fly ? fly.Grounded && fly.Vy == 0 && Math.Abs(Field.Dx(p.X, fly.X)) < 26 : Math.Abs(Field.Dx(p.X, bx)) < 22;
            return new Controls(0, now);
        }
        Doing = "kicking";
        return Toward(p, gx);
    }

    private static Controls Toward(Mechanic p, double x)
    {
        double dx = Field.Dx(p.X, x);
        return new Controls(Math.Abs(dx) < 5 ? 0 : Math.Sign(dx));
    }

    /// <summary>
    /// What to go for, as (the ledge to be on, where on it, and for a bump, where the pest will be when the jump
    /// gets there): a pest on its back (the nearest), then a coin, then a pest on its feet that can be bumped from
    /// some ledge below it.
    /// </summary>
    private (int Ledge, double X, double? BumpAt) Choose(GameEngine g, Mechanic p, int here)
    {
        double best = double.MaxValue;
        (int, double, double?) choice = (-1, 0, null);
        _target = null;
        foreach (var e in g.Pests)
        {
            if (e.Gone || (e.Vy != 0 && e.Kind != PestKind.Hopfly) || e.State is not (PestState.Walking or PestState.Flipped)) continue;
            // A fly in mid-hop counts as on the ledge under it.
            int on = e.Vy != 0 ? Array.IndexOf(Field.Ledges, Field.Below(e.X, e.Y)) : LedgeOf(e.X, e.Y);
            if (on < 0) continue;
            if (e.State == PestState.Flipped)
            {
                double c = Cost(p, here, on, e.X) - 400;
                if (e.Timer < 1.2) c += 300;
                if (c < best) { best = c; choice = (on, e.X, null); }
                continue;
            }
            if (e.State != PestState.Walking || on == 0) continue;
            if (e.Kind == PestKind.Hopfly)
            {
                // A fly: under where it'll next come down (or where it is, if it's down and there's time).
                var ledge = Field.Ledges[on];
                double fs = g.SpeedOf(e), air = 2 * 430 / Field.Gravity, land;
                if (e.Vy != 0 || e.Hop > 0)
                {
                    double drop = ledge.Y - e.Y, t = (-e.Vy + Math.Sqrt(Math.Max(0, e.Vy * e.Vy + 2 * Field.Gravity * drop))) / Field.Gravity;
                    land = Field.Wrap(e.X + e.Dir * fs * t);
                }
                else land = e.Timer > 0.25 && Math.Abs(Field.Dx(p.X, e.X)) < 30 ? e.X : Field.Wrap(e.X + e.Dir * fs * air);
                int under = Array.FindIndex(Field.Ledges, l => l.Y > ledge.Y && l.Y - ledge.Y < 150 && l.Covers(land, -12));
                if (under < 0 || !ledge.Covers(land, -8)) continue;
                double fc = Cost(p, here, under, land) + (e.Last ? -150 : 0);
                if (fc < best) { best = fc; choice = (under, land, land); _target = e; }
                continue;
            }
            // Along its way, the first place it'll be over a ledge it can be bumped from, that there's time to get under;
            // failing that, the one it'd be least late for.
            double speed = g.SpeedOf(e), late = double.MaxValue;
            (int Below, double Ahead, double Cost)? spot = null;
            for (double tau = 0.3; tau < 4; tau += 0.1)
            {
                double ahead = Field.Wrap(e.X + e.Dir * speed * tau);
                if (!Field.Ledges[on].Covers(ahead, -8)) break;
                int below = Array.FindIndex(Field.Ledges, l => l.Y > Field.Ledges[on].Y && l.Y - Field.Ledges[on].Y < 150 && l.Covers(ahead, -12));
                if (below < 0) continue;
                double cost = Cost(p, here, below, ahead), by = cost / GameEngine.WalkSpeed - tau;
                if (by <= 0.6) { spot = (below, ahead, cost); break; }
                if (by < late) { late = by; spot = (below, ahead, cost + 500); }
            }
            if (spot is { } sp)
            {
                double bumpAt = Field.Wrap(e.X + e.Dir * speed * 0.14);
                double cost = sp.Cost + (e.Kind == PestKind.Clawcrab && !e.Angry ? 60 : 0) + (e.Last ? -150 : 0);
                if (cost < best) { best = cost; choice = (sp.Below, sp.Ahead, bumpAt); _target = e; }
            }
        }
        foreach (var k in g.Coins)
        {
            int on = LedgeOf(k.X, k.Y);
            if (on < 0) continue;
            double c = Cost(p, here, on, k.X) - (k.Still ? 200 : 100);
            if (c < best) { best = c; choice = (on, k.X, null); }
        }
        return choice;
    }

    /// <summary>A rough cost of getting somewhere: the distance across, and a charge for each ledge to change.</summary>
    private static double Cost(Mechanic p, int here, int ledge, double x)
    {
        int hops = 0;
        for (int at = here; at != ledge && hops < 8; hops++)
        {
            if (NextLedge(at, ledge) is not { } n) return double.MaxValue / 2;
            at = n;
        }
        return Math.Abs(Field.Dx(p.X, x)) + hops * 260;
    }

    /// <summary>
    /// A pest on its feet coming at it along its own ledge: away from it, or (if one is coming from each side, or
    /// it's close) over it; on a crowded floor, the jolt block; and keep out from under a falling pest.
    /// </summary>
    private static Controls? Danger(GameEngine g, Mechanic p, int here)
    {
        // The fireball: away from it if it's coming this way at this height.
        if (g.Ember is { } f && Math.Abs(f.Y - (p.Y - 20)) < 70 && Math.Abs(Field.Dx(p.X, f.X)) < 200 && Math.Sign(Field.Dx(f.X, p.X)) == Math.Sign(f.Vx))
            return new Controls(Math.Sign(f.Vx));
        var mine = g.Pests.Where(e => !e.Gone && e.Upright && (Math.Abs(e.Y - p.Y) < 4 && LedgeOf(e.X, e.Y) == here
            || e.Kind == PestKind.Hopfly && e.Y < p.Y && Array.IndexOf(Field.Ledges, Field.Below(e.X, e.Y)) == here)).ToList();
        var coming = mine.Where(e => Math.Abs(Field.Dx(p.X, e.X)) < 150 && Math.Sign(Field.Dx(e.X, p.X)) == e.Dir).ToList();
        if (here == 0 && g.Jolts > 0 && g.Pests.Count(e => e.Upright && e.Grounded) >= 3 && Math.Abs(Field.Dx(p.X, Field.JoltX)) < 14)
            return new Controls(0, true);
        if (coming.Count == 0)
        {
            // Something falling on it.
            if (g.Pests.Any(e => e.State == PestState.Falling && Math.Abs(Field.Dx(p.X, e.X)) < 40 && e.Y < p.Y && p.Y - e.Y < 160))
                return new Controls(p.Facing);
            return null;
        }
        var near = coming.OrderBy(e => Math.Abs(Field.Dx(p.X, e.X))).First();
        double gap = Math.Abs(Field.Dx(p.X, near.X));
        bool bothSides = coming.Any(e => e.Dir > 0) && coming.Any(e => e.Dir < 0);
        // Room to jump without bumping the ledge above?
        bool clear = !Field.Ledges.Any(l => l.Y < p.Y && p.Y - l.Y < 160 && l.Covers(p.X, 14));
        if ((gap < 80 || bothSides) && clear && near.Kind != PestKind.Hopfly) return new Controls(near.Dir, true);
        return new Controls(near.Dir);
    }
}
