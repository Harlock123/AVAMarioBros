namespace AVAMarioBros.Engine;

public sealed partial class GameEngine
{
    public const double WalkSpeed = 200, WalkAccel = 1100, AirAccel = 700, Friction = 1300, JumpSpeed = 680;
    public const double BumpReach = 40, KickChainWindow = 1.5, PipeSeconds = 1.2, EmergeEvery = 2.2;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 2.6, BonusSeconds = 20;
    public const int ExtraLifeEvery = 20000, KickPoints = 800, CoinPoints = 800, EmberPoints = 1000, BumpPoints = 10, PerfectBonus = 5000, StartJolts = 3;

    /// <summary>The phase: 1 for the first. Every third is a bonus phase of coins.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Mechanic Player { get; } = new();
    public List<Pest> Pests { get; } = [];
    public List<Coin> Coins { get; } = [];
    public List<Bump> Bumps { get; } = [];
    public Ember? Ember { get; private set; }
    public MechanicPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>Jolts left in the jolt block (it's gone when they're used up).</summary>
    public int Jolts { get; private set; }
    /// <summary>How long ago the jolt block was last hit, for the screen's shake.</summary>
    public double Jolted { get; private set; } = 99;
    /// <summary>On a bonus phase, the time left to gather the coins.</summary>
    public double BonusLeft { get; private set; }
    public bool IsBonus => Wave % 3 == 0;
    /// <summary>Pests still to come out of the pipes this phase.</summary>
    public int ToCome => _queue.Count;
    /// <summary>For tests: the pests hold still, none come out, and no fireball appears.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _chain, _coinsSet;
    private double _emergeIn, _emberIn, _lastKick = -99, _clock;
    private bool _leftPipe;
    private readonly Queue<PestKind> _queue = new();

    public double PestSpeed => Math.Min(130, 70 + 6 * (Wave - 1));
    public double FlipSeconds => Math.Max(4, 8 - 0.4 * (Wave - 1));
    public double EmberAfter => Math.Max(12, 26 - Wave);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        Jolts = StartJolts;
        _nextExtra = ExtraLifeEvery;
        Pests.Clear();
        Coins.Clear();
        Bumps.Clear();
        Ember = null;
        PlacePlayer();
        SetPhase(Phase.Start);
    }

    /// <summary>On the floor in the middle, under the jolt block's gap.</summary>
    private void PlacePlayer(bool fromTop = false)
    {
        var p = Player;
        p.X = Field.Width / 2 - 120;
        p.Y = fromTop ? 120 : Field.FloorY;
        p.Vx = p.Vy = 0;
        p.Grounded = !fromTop;
        p.Alive = true;
        p.Safe = SafeSeconds;
    }

    /// <summary>
    /// A phase: pests queued to come out of the top pipes (beetles to begin with, then crabs, then flies, then all
    /// of them), or on every third, a bonus phase of coins set out on the ledges against the clock.
    /// </summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Pests.Clear();
        Coins.Clear();
        Bumps.Clear();
        _queue.Clear();
        Ember = null;
        _emberIn = EmberAfter;
        _chain = 0;
        if (IsBonus)
        {
            // Ten coins round the room, and the jolt block filled up again.
            Jolts = StartJolts;
            (double X, double Y)[] spots = [(90, 284), (934, 284), (300, 284), (724, 284), (512, 424), (380, 424), (90, 424), (934, 424), (190, 564), (834, 564)];
            foreach (var (x, y) in spots) Coins.Add(new Coin { X = x, Y = y, Still = true });
            _coinsSet = spots.Length;
            BonusLeft = BonusSeconds;
        }
        else
        {
            int n = Math.Min(8, 4 + (wave - 1) / 3);
            int round = (wave - 1) / 3;
            for (int i = 0; i < n; i++)
            {
                var kind = (round % 4) switch
                {
                    0 => PestKind.Shellback,
                    1 => i % 3 == 2 ? PestKind.Shellback : PestKind.Clawcrab,
                    2 => i % 3 == 2 ? PestKind.Clawcrab : PestKind.Hopfly,
                    _ => (PestKind)(i % 3),
                };
                _queue.Enqueue(kind);
            }
        }
        _emergeIn = 0.5;
        PlacePlayer();
        SetPhase(Phase.Start);
        Say(IsBonus ? "BONUS PHASE" : $"PHASE {wave}", IsBonus ? "GATHER THE COINS" : Theme(wave), StartSeconds + 0.3);
        Play(Sfx.PhaseStart);
        Music(true);
    }

    private static string? Theme(int wave) => ((wave - 1) / 3 % 4) switch
    {
        0 => "SHELLBACKS",
        1 => "CLAWCRABS - TWO BUMPS EACH",
        2 => "HOPFLIES - BUMP THEM WHEN THEY LAND",
        _ => null,
    };

    private void Simulate(double dt, Controls c)
    {
        _clock += dt;
        foreach (var b in Bumps) b.Age += dt;
        Bumps.RemoveAll(b => b.Age >= Bump.Life);
        Jolted += dt;
        switch (Phase)
        {
            case Phase.Start:
                Move(dt, c);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Move(dt, c);
                Emerge(dt);
                MovePests(dt);
                MoveCoins(dt);
                MoveEmber(dt);
                Touch();
                if (Phase != Phase.Play) break;
                if (IsBonus)
                {
                    BonusLeft -= dt;
                    if (Coins.Count == 0 || BonusLeft <= 0) Clear();
                }
                else if (_queue.Count == 0 && Pests.All(p => p.Gone)) Clear();
                break;
            case Phase.Dying:
                MovePests(dt);
                MoveCoins(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    PlacePlayer(fromTop: true);
                    Ember = null;
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Move(dt, c);
                MovePests(dt);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        Pests.RemoveAll(p => p.Gone);
        PhaseTime += dt;
    }

    // ───────────────────────────── the mechanic ─────────────────────────────

    /// <summary>
    /// One step of the mechanic: walking (with a little slide when he stops), a jump with some steering in the
    /// air, landing on ledges, and his head bumping their undersides. Returns where he bumped, if he did.
    /// </summary>
    public static (double X, Ledge? Ledge, bool Jolt)? Step(Mechanic p, Controls c, double dt, bool joltThere)
    {
        double want = c.Dx * WalkSpeed, accel = p.Grounded ? (c.Dx != 0 ? WalkAccel : Friction) : AirAccel;
        if (c.Dx != 0 || p.Grounded) p.Vx += Math.Clamp(want - p.Vx, -accel * dt, accel * dt);
        if (c.Dx != 0) p.Facing = c.Dx;
        if (c.Jump && p.Grounded)
        {
            p.Vy = -JumpSpeed;
            p.Grounded = false;
        }
        (double, Ledge?, bool)? bumped = null;
        double oldY = p.Y;
        if (!p.Grounded) p.Vy += Field.Gravity * dt;
        p.X = Field.Wrap(p.X + p.Vx * dt);
        p.Y += p.Vy * dt;
        if (p.Grounded)
        {
            if (Field.Under(p.X, p.Y) == null) { p.Grounded = false; p.Vy = 0; }
            if (Math.Abs(p.Vx) > 20) p.Walk += dt;
        }
        else if (p.Vy < 0)
        {
            // Rising: does his head reach a ledge's underside (or the jolt block's)?
            double head = p.Y - Mechanic.Height, oldHead = oldY - Mechanic.Height;
            foreach (var l in Field.Ledges)
            {
                double under = l.Y + Field.Thick;
                if (oldHead >= under && head < under && l.Covers(p.X, -4))
                {
                    p.Y = under + Mechanic.Height;
                    p.Vy = 60;
                    bumped = (p.X, l, false);
                    break;
                }
            }
            double jb = Field.JoltTop + Field.JoltSize;
            if (bumped == null && joltThere && oldHead >= jb && head < jb && Math.Abs(Field.Dx(Field.JoltX, p.X)) < Field.JoltSize / 2 + Mechanic.Width / 2 - 4)
            {
                p.Y = jb + Mechanic.Height;
                p.Vy = 60;
                bumped = (p.X, null, true);
            }
        }
        else
        {
            // Falling: does he land on a ledge?
            foreach (var l in Field.Ledges)
                if (oldY <= l.Y && p.Y >= l.Y && l.Covers(p.X))
                {
                    p.Y = l.Y;
                    p.Vy = 0;
                    p.Grounded = true;
                    break;
                }
        }
        return bumped;
    }

    private void Move(double dt, Controls c)
    {
        var p = Player;
        if (!p.Alive) return;
        p.Safe = Math.Max(0, p.Safe - dt);
        bool jumped = c.Jump && p.Grounded;
        var hit = Step(p, c, dt, Jolts > 0);
        if (jumped) Play(Sfx.Jump);
        if (hit is not { } h) return;
        if (h.Jolt) Jolt();
        else BumpLedge(h.X, h.Ledge!);
    }

    /// <summary>A ledge bumped from below: whatever's stood on it just there is knocked over (or rights itself).</summary>
    private void BumpLedge(double x, Ledge l)
    {
        Bumps.Add(new Bump { X = x, Y = l.Y });
        Play(Sfx.Bump);
        foreach (var e in Pests.Where(e => !e.Gone && Math.Abs(e.Y - l.Y) < 3 && Math.Abs(Field.Dx(x, e.X)) < BumpReach).ToList()) Knock(e);
        foreach (var coin in Coins.Where(k => Math.Abs(k.Y - l.Y) < 3 && Math.Abs(Field.Dx(x, k.X)) < BumpReach).ToList()) Take(coin);
        if (Ember is { } f && Math.Abs(Field.Dx(x, f.X)) < BumpReach && f.Y < l.Y && f.Y > l.Y - 70) Douse();
    }

    /// <summary>The jolt block: every pest stood on a ledge anywhere is knocked over at once.</summary>
    private void Jolt()
    {
        Jolts--;
        Jolted = 0;
        Play(Sfx.Jolt);
        foreach (var e in Pests.Where(e => e.State is PestState.Walking or PestState.Flipped && e.Hop <= 0).ToList()) Knock(e);
        if (Ember != null) Douse();
    }

    /// <summary>
    /// A pest bumped from below: one on its feet goes over on its back (a crab only gets angry the first time); one
    /// already on its back is turned the right way up again. A fly in the air isn't touched.
    /// </summary>
    private void Knock(Pest e)
    {
        if (e.Hop > 0 || e.State is PestState.Kicked or PestState.InPipe or PestState.Falling) return;
        if (e.State == PestState.Flipped)
        {
            e.State = PestState.Walking;
            e.Vy = 0;
            Play(Sfx.Righted);
            return;
        }
        Score += BumpPoints;
        if (e.Kind == PestKind.Clawcrab && !e.Angry)
        {
            e.Angry = true;
            Play(Sfx.Angry);
            return;
        }
        e.State = PestState.Flipped;
        e.Timer = FlipSeconds;
        e.Vy = -220;
        Play(Sfx.Flip);
    }

    // ───────────────────────────── the pests ─────────────────────────────

    /// <summary>The phase's pests come out of the top pipes one after another, from alternate sides.</summary>
    private void Emerge(double dt)
    {
        if (Calm || _queue.Count == 0 || (_emergeIn -= dt) > 0) return;
        _emergeIn = EmergeEvery;
        _leftPipe = !_leftPipe;
        var e = new Pest { Kind = _queue.Dequeue() };
        Out(e, _leftPipe);
        Pests.Add(e);
    }

    /// <summary>A pest coming out of a top pipe, onto the top ledge, walking away from the pipe.</summary>
    private void Out(Pest e, bool left)
    {
        e.State = PestState.Walking;
        e.X = left ? Field.TopPipeLeftX : Field.TopPipeRightX;
        e.Y = Field.TopPipeY;
        e.Dir = left ? 1 : -1;
        e.Vy = 0;
        e.Hop = 0;
        Play(Sfx.Pipe);
    }

    /// <summary>How fast a pest goes: by its kind, quicker each time it has got up again, if it's cross, and if it's the last one left.</summary>
    public double SpeedOf(Pest e) => Math.Min(MaxPestSpeed, PestSpeed * (e.Kind switch { PestKind.Clawcrab => 1.1, PestKind.Hopfly => 1.2, _ => 1 })
        * Math.Pow(1.25, e.Level) * (e.Angry ? 1.3 : 1) * (e.Last ? 1.4 : 1));

    /// <summary>However riled, never quite as quick as the mechanic.</summary>
    public const double MaxPestSpeed = 0.82 * WalkSpeed;

    private void MovePests(double dt)
    {
        int left = Pests.Count(e => e.State != PestState.Kicked && !e.Gone) + _queue.Count;
        foreach (var e in Pests)
        {
            e.T += dt;
            if (left == 1 && e.State != PestState.Kicked && !e.Last && Phase == Phase.Play) e.Last = true;
            switch (e.State)
            {
                case PestState.Kicked:
                    e.Vy += Field.Gravity * dt;
                    e.X += e.Vx * dt;
                    e.Y += e.Vy * dt;
                    if (e.Y > Field.Height + 60) e.Gone = true;
                    continue;
                case PestState.InPipe:
                    if ((e.Timer -= dt) <= 0) Out(e, e.X < Field.Width / 2);
                    continue;
                case PestState.Flipped:
                    // On its back, legs waving, until it gets itself up again: quicker, and a new colour.
                    if (e.Vy != 0 || Field.Under(e.X, e.Y) == null) Fall(e, dt);
                    if (Calm) continue;
                    if ((e.Timer -= dt) <= 0)
                    {
                        e.State = PestState.Walking;
                        e.Level = Math.Min(2, e.Level + 1);
                        e.Angry = false;
                        Play(Sfx.Righted);
                    }
                    continue;
            }
            if (Calm) continue;
            double speed = SpeedOf(e);
            if (e.State == PestState.Falling) { e.X = Field.Wrap(e.X + e.Dir * speed * 0.5 * dt); Fall(e, dt); continue; }
            if (e.Kind == PestKind.Hopfly)
            {
                // Hops along: a short wait on the ledge, then up and over, moving only in the air.
                if (e.Hop > 0 || e.Vy != 0)
                {
                    e.X = Field.Wrap(e.X + e.Dir * speed * dt);
                    Fall(e, dt);
                    if (e.Vy == 0) e.Hop = 0;
                }
                else if ((e.Timer -= dt) <= 0)
                {
                    e.Timer = 0.35;
                    e.Hop = 1;
                    e.Vy = -430;
                }
            }
            else e.X = Field.Wrap(e.X + e.Dir * speed * dt);
            // Off the end of its ledge: it falls.
            if (e.Hop <= 0 && e.Vy == 0 && Field.Under(e.X, e.Y) == null) { e.State = PestState.Falling; e.Vy = 0; }
            // Into a bottom pipe, to come out at the top again on the other side.
            if (e.Y >= Field.FloorY - 1 && ((e.Dir < 0 && e.X < Field.BottomPipeIn) || (e.Dir > 0 && e.X > Field.Width - Field.BottomPipeIn)))
            {
                // (Its x marks the side it comes out of: in at the left, out at the top right.)
                e.State = PestState.InPipe;
                e.Timer = PipeSeconds;
                e.X = e.Dir < 0 ? Field.Width : 0;
                e.Hop = 0;
                e.Vy = 0;
            }
        }
    }

    /// <summary>Falling (or hopping) under gravity until it lands on a ledge.</summary>
    private static void Fall(Pest e, double dt)
    {
        double oldY = e.Y;
        e.Vy += Field.Gravity * dt;
        e.Y += e.Vy * dt;
        if (e.Vy <= 0) return;
        foreach (var l in Field.Ledges)
            if (oldY <= l.Y && e.Y >= l.Y && l.Covers(e.X))
            {
                e.Y = l.Y;
                e.Vy = 0;
                if (e.State == PestState.Falling) e.State = PestState.Walking;
                break;
            }
    }

    // ───────────────────────────── coins and the fireball ─────────────────────────────

    private void MoveCoins(double dt)
    {
        foreach (var k in Coins)
        {
            k.T += dt;
            if (k.Still) continue;
            k.X = Field.Wrap(k.X + k.Dir * 110 * dt);
            if (!k.Falling && Field.Under(k.X, k.Y) == null) k.Falling = true;
            if (k.Falling)
            {
                double oldY = k.Y;
                k.Vy += Field.Gravity * dt;
                k.Y += k.Vy * dt;
                foreach (var l in Field.Ledges)
                    if (oldY <= l.Y && k.Y >= l.Y && l.Covers(k.X)) { k.Y = l.Y; k.Vy = 0; k.Falling = false; break; }
            }
        }
        // A coin rolling into a bottom pipe is gone.
        Coins.RemoveAll(k => !k.Still && k.Y >= Field.FloorY - 1 && ((k.Dir < 0 && k.X < Field.BottomPipeIn) || (k.Dir > 0 && k.X > Field.Width - Field.BottomPipeIn)));
    }

    /// <summary>
    /// Once a phase has gone on long enough, a fireball comes drifting across the room on a wavy line; bump the
    /// ledge under it to put it out.
    /// </summary>
    private void MoveEmber(double dt)
    {
        if (Calm || IsBonus) return;
        if (Ember is not { } f)
        {
            if ((_emberIn -= dt) > 0) return;
            bool left = _rng.Next(2) == 0;
            double y = new[] { 520.0, 380, 240, 660 }[_rng.Next(4)];
            Ember = new Ember { X = left ? 0 : Field.Width, Y = y, BaseY = y, Vx = (left ? 1 : -1) * (90 + 6 * Wave) };
            Play(Sfx.Ember);
            return;
        }
        f.T += dt;
        f.X += f.Vx * dt;
        f.Y = f.BaseY + Math.Sin(f.T * 2.4) * 34;
        if (f.X < -30 || f.X > Field.Width + 30) { Ember = null; _emberIn = EmberAfter * 0.5; }
    }

    private void Douse()
    {
        var f = Ember!;
        Award(EmberPoints, f.X, f.Y);
        Effects.Add(new Effect { Kind = EffectKind.Spark, X = f.X, Y = f.Y, Life = 0.5, Colour = 0xFFFFB04A });
        Ember = null;
        _emberIn = EmberAfter;
        Play(Sfx.Ember);
    }

    // ───────────────────────────── touching ─────────────────────────────

    /// <summary>
    /// The mechanic touching things: a pest on its back is kicked off (each in quick succession worth more); a coin is
    /// his; a pest on its feet, or the fireball, is the end of him.
    /// </summary>
    private void Touch()
    {
        var p = Player;
        if (!p.Alive) return;
        double cy = p.Y - Mechanic.Height / 2;
        foreach (var k in Coins.Where(k => Math.Abs(Field.Dx(p.X, k.X)) < 24 && Math.Abs(k.Y - 12 - cy) < 30).ToList()) Take(k);
        foreach (var e in Pests)
        {
            if (e.Gone || e.State is PestState.Kicked or PestState.InPipe) continue;
            if (Math.Abs(Field.Dx(p.X, e.X)) > (Mechanic.Width + Pest.Width) / 2 - 4 || Math.Abs((e.Y - Pest.Height / 2) - cy) > (Mechanic.Height + Pest.Height) / 2 - 6) continue;
            if (e.State == PestState.Flipped) { Kick(e); continue; }
            if (p.Safe > 0) continue;
            Kill(e.Kind.ToString().ToLowerInvariant());
            return;
        }
        if (Ember is { } f && p.Safe <= 0 && Math.Abs(Field.Dx(p.X, f.X)) < Ember.Radius + Mechanic.Width / 2 - 2 && Math.Abs(f.Y - cy) < Ember.Radius + Mechanic.Height / 2 - 4)
            Kill("fireball");
    }

    /// <summary>A pest on its back kicked off the ledge: gone, and a coin comes out of a top pipe.</summary>
    private void Kick(Pest e)
    {
        _chain = _clock - _lastKick < KickChainWindow ? Math.Min(4, _chain + 1) : 1;
        _lastKick = _clock;
        e.State = PestState.Kicked;
        e.Vx = Math.Sign(Field.Dx(Player.X, e.X)) * 260;
        if (e.Vx == 0) e.Vx = 260;
        e.Vy = -380;
        Award(KickPoints * _chain, e.X, e.Y - 20);
        Play(Sfx.Kick);
        bool left = _rng.Next(2) == 0;
        Coins.Add(new Coin { X = left ? Field.TopPipeLeftX : Field.TopPipeRightX, Y = Field.TopPipeY, Dir = left ? 1 : -1 });
    }

    private void Take(Coin k)
    {
        Coins.Remove(k);
        Award(CoinPoints, k.X, k.Y - 20);
        Play(Sfx.Coin);
    }

    private void Kill(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lives--;
        Effects.Add(new Effect { Kind = EffectKind.Spark, X = p.X, Y = p.Y - 20, Life = 0.6, Colour = 0xFFFFFFFF });
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Clear()
    {
        Bonus = 0;
        string? sub = null;
        if (IsBonus)
        {
            int got = _coinsSet - Coins.Count;
            if (Coins.Count == 0) { Bonus = PerfectBonus; sub = $"PERFECT! BONUS {PerfectBonus}"; }
            else sub = $"{got} OF {_coinsSet} COINS";
        }
        Coins.RemoveAll(k => k.Still);
        Ember = null;
        Play(Sfx.PhaseClear);
        Say(IsBonus ? "TIME!" : $"PHASE {Wave} CLEARED", sub, ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 0.8, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a phase, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal Pest PutPest(PestKind kind, double x, double y, int dir = 1)
    {
        var e = new Pest { Kind = kind, X = x, Y = y, Dir = dir };
        Pests.Add(e);
        return e;
    }

    internal void PutPlayer(double x, double y)
    {
        var p = Player;
        p.X = x;
        p.Y = y;
        p.Vx = p.Vy = 0;
        p.Grounded = Field.Under(x, y) != null;
        p.Alive = true;
        p.Safe = 0;
    }

    internal void NoMorePests() => _queue.Clear();
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void SetJolts(int n) => Jolts = n;
    internal void SetEmber(double x, double y, double vx) => Ember = new Ember { X = x, Y = y, BaseY = y, Vx = vx };
}
