namespace AVAMarioBros.Engine;

/// <summary>The controls: walking left or right (-1, 0 or 1), and jump.</summary>
public readonly record struct Controls(int Dx = 0, bool Jump = false);

/// <summary>
/// The screen: one room of ledges, wrapping round at the sides, screen coordinates (y grows downwards), with a
/// pipe at each top corner where the pests come out and one at each bottom corner where they go back in.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Gravity = 1500;
    /// <summary>How thick a ledge is, from its top (where things stand) to its underside (which gets bumped).</summary>
    public const double Thick = 18;
    public const double FloorY = 704;

    /// <summary>The ledges: where each runs and the height of its top. The floor is the lowest.</summary>
    public static readonly Ledge[] Ledges =
    [
        new(0, Width, FloorY),
        new(0, 380, 564), new(644, Width, 564),
        new(0, 180, 424), new(300, 724, 424), new(844, Width, 424),
        new(0, 420, 284), new(604, Width, 284),
    ];

    /// <summary>The jolt block, between the floor and the first ledges, in the middle.</summary>
    public const double JoltX = Width / 2, JoltTop = 590, JoltSize = 44;

    /// <summary>Where the pests come out (on the top ledges, by the top pipes) and go back in (the floor's corners).</summary>
    public const double TopPipeY = 284, TopPipeLeftX = 70, TopPipeRightX = Width - 70, BottomPipeIn = 40;

    public static double Wrap(double x) => (x % Width + Width) % Width;

    /// <summary>The shortest way across from one x to another, round the wrap if that's shorter.</summary>
    public static double Dx(double from, double to)
    {
        double d = to - from;
        if (d > Width / 2) d -= Width;
        else if (d < -Width / 2) d += Width;
        return d;
    }

    /// <summary>The ledge whose top a point sits on (within a pixel or two), if any.</summary>
    public static Ledge? Under(double x, double y, double slack = 2) =>
        Ledges.FirstOrDefault(l => Math.Abs(l.Y - y) <= slack && l.Covers(x));

    /// <summary>The highest ledge below a point (the one something falling from there lands on).</summary>
    public static Ledge Below(double x, double y) => Ledges.Where(l => l.Y >= y - 0.5 && l.Covers(x)).OrderBy(l => l.Y).First();
}

/// <summary>A ledge: from <see cref="X0"/> to <see cref="X1"/>, its top at <see cref="Y"/>.</summary>
public sealed record Ledge(double X0, double X1, double Y)
{
    public bool Covers(double x, double margin = 0)
    {
        x = Field.Wrap(x);
        return x >= X0 - margin && x <= X1 + margin;
    }

    public bool IsFloor => Y >= Field.FloorY;
}

/// <summary>The player: a mechanic in a hard hat, who walks, jumps, and bumps the ledges from underneath.</summary>
public sealed class Mechanic
{
    public double X = Field.Width / 2, Y = Field.FloorY, Vx, Vy;
    public bool Grounded = true, Alive = true;
    public int Facing = 1;
    public double Safe, Walk;
    public const double Width = 26, Height = 40;
}

public enum PestKind
{
    /// <summary>A shell-backed beetle: one bump flips it.</summary>
    Shellback,
    /// <summary>A crab: the first bump only makes it angry (and quicker); the second flips it.</summary>
    Clawcrab,
    /// <summary>A fly that hops along: it can only be bumped while it's down on a ledge.</summary>
    Hopfly,
}

public enum PestState { Walking, Falling, Flipped, Kicked, InPipe }

public sealed class Pest
{
    public PestKind Kind;
    public PestState State = PestState.Walking;
    public double X, Y, Vx, Vy, T, Timer, Hop;
    public int Dir = 1;
    /// <summary>How many times it has come back from being flipped (each time quicker, and a new colour).</summary>
    public int Level;
    /// <summary>A crab bumped once, and cross about it.</summary>
    public bool Angry;
    /// <summary>The last one left in a phase: as quick as it gets.</summary>
    public bool Last;
    public bool Gone;
    public bool Upright => State is PestState.Walking or PestState.Falling;
    public bool Grounded => State == PestState.Walking && Hop <= 0;
    public const double Width = 30, Height = 26;
}

/// <summary>A coin: rolling along the ledges after a pest is kicked off, or set out to gather on a bonus phase.</summary>
public sealed class Coin
{
    public double X, Y, Vy, T;
    public int Dir = 1;
    /// <summary>Set out on a bonus phase: it stays where it is.</summary>
    public bool Still;
    public bool Falling;
}

/// <summary>A fireball, drifting across the room on a wavy line once a phase has gone on too long.</summary>
public sealed class Ember
{
    public double X, Y, BaseY, Vx, T;
    public const double Radius = 12;
}

/// <summary>Where a ledge was bumped from below, and how long ago (the bulge it makes).</summary>
public sealed class Bump
{
    public double X, Y, Age;
    public const double Life = 0.3;
}

public enum EffectKind { Score, Word, Spark }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
