using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMarioBros.Engine;
using Effect = AVAMarioBros.Engine.Effect;

namespace AVAMarioBros.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMarioBros/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);
    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private const uint Girder = 0xFF3A7AD8, GirderDark = 0xFF1A3A78, Brick = 0xFFB8582A, BrickDark = 0xFF6A2A10, PipeGreen = 0xFF3AB04A, PipeDark = 0xFF1A6A2A;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        // The jolt shakes the whole room for a moment.
        double shake = g.Jolted < 0.35 ? Math.Sin(g.Jolted * 90) * 6 * (1 - g.Jolted / 0.35) : 0;
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, 0, Field.Width, Field.Height));
        using (dc.PushTransform(Matrix.CreateTranslation(0, shake)))
        {
            DrawRoom(dc, g, 1);
            if (InGame)
            {
                foreach (var k in g.Coins) DrawCoin(dc, k.X, k.Y - 14, k.T, 1);
                foreach (var e in g.Pests) if (e.State != PestState.InPipe) DrawPest(dc, e.Kind, e.X, e.Y, e.Dir, e.T, e.State == PestState.Flipped || e.State == PestState.Kicked, e.Level, e.Angry, e.Last, e.Timer, 1);
                if (g.Ember is { } f) DrawEmber(dc, f.X, f.Y, f.T);
                DrawPlayer(dc);
            }
            DrawPipes(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 200), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the room ─────────────────────────────

    /// <summary>
    /// The ledges as riveted girders (the floor as brick), each bulging up where it's just been bumped from below,
    /// and the jolt block with the jolts left in it.
    /// </summary>
    private void DrawRoom(DrawingContext dc, GameEngine g, double alpha)
    {
        foreach (var l in Field.Ledges)
        {
            if (l.IsFloor)
            {
                dc.FillRectangle(Brush(Alpha(Brick, alpha)), new Rect(0, l.Y, Field.Width, Field.Height - l.Y));
                for (int row = 0; row < 4; row++)
                for (int col = -1; col < 34; col++)
                {
                    double x = col * 32 + (row % 2) * 16, y = l.Y + row * 16;
                    dc.DrawRectangle(null, Pen(Alpha(BrickDark, alpha), 2), new Rect(x, y, 32, 16));
                }
                continue;
            }
            // Drawn a short piece at a time, so the bumps can lift it.
            for (double x = l.X0; x < l.X1; x += 8)
            {
                double lift = 0;
                foreach (var b in g.Bumps.Where(b => Math.Abs(b.Y - l.Y) < 1))
                {
                    double d = Math.Abs(Field.Dx(b.X, x + 4));
                    if (d < 48) lift = Math.Max(lift, Math.Sin(Math.PI * b.Age / Bump.Life) * 12 * (1 - d / 48));
                }
                var piece = new Rect(x, l.Y - lift, Math.Min(8, l.X1 - x), Field.Thick);
                dc.FillRectangle(Brush(Alpha(Girder, alpha)), piece);
                dc.FillRectangle(Brush(Alpha(0xFF8AB8FF, alpha)), new Rect(piece.X, piece.Y, piece.Width, 3));
                dc.FillRectangle(Brush(Alpha(GirderDark, alpha)), new Rect(piece.X, piece.Bottom - 4, piece.Width, 4));
                if ((int)x % 32 == 0) dc.DrawEllipse(Brush(Alpha(GirderDark, alpha)), null, new Point(x + 4, l.Y - lift + 9), 2, 2);
            }
        }
        if (g.Jolts > 0)
        {
            var r = new Rect(Field.JoltX - Field.JoltSize / 2, Field.JoltTop, Field.JoltSize, Field.JoltSize);
            dc.DrawRectangle(Brush(Alpha(0xFFE03A2A, alpha)), Pen(Alpha(0xFFFFFFFF, alpha), 3), r, 4, 4);
            Text(dc, "JOLT", new Point(Field.JoltX, Field.JoltTop + 15), 18, Alpha(0xFFFFFFFF, alpha), center: true, shadow: false);
            for (int i = 0; i < g.Jolts; i++) dc.DrawEllipse(Brush(Alpha(Yellow, alpha)), null, new Point(Field.JoltX - 10 + i * 10, Field.JoltTop + 33), 3, 3);
        }
    }

    /// <summary>The four pipes: two at the top, where the pests come out, and two in the bottom corners where they go back in.</summary>
    private static void DrawPipes(DrawingContext dc)
    {
        void Pipe(double x, double y, double w, double h, bool mouthRight)
        {
            dc.DrawRectangle(Brush(PipeGreen), Pen(PipeDark, 2), new Rect(x, y, w, h));
            dc.FillRectangle(Brush(0x40FFFFFF), new Rect(x + 6, y + 4, 6, h - 8));
            double mx = mouthRight ? x + w - 8 : x - 8;
            dc.DrawRectangle(Brush(PipeGreen), Pen(PipeDark, 2), new Rect(mx, y - 6, 16, h + 12));
        }
        Pipe(0, Field.TopPipeY - 64, 60, 52, true);
        Pipe(Field.Width - 60, Field.TopPipeY - 64, 60, 52, false);
        Pipe(0, Field.FloorY - 52, 36, 50, true);
        Pipe(Field.Width - 36, Field.FloorY - 52, 36, 50, false);
    }

    // ───────────────────────────── the mechanic ─────────────────────────────

    private void DrawPlayer(DrawingContext dc)
    {
        var p = Engine.Player;
        if (!p.Alive)
        {
            // Knocked out: flat on his back, stars going round.
            DrawMechanic(dc, p.X, p.Y, p.Facing, 0, false, 1, knocked: true);
            return;
        }
        if (p.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        foreach (double x in new[] { p.X, p.X - Field.Width, p.X + Field.Width })
            if (x > -30 && x < Field.Width + 30) DrawMechanic(dc, x, p.Y, p.Facing, p.Walk, !p.Grounded, 1);
    }

    /// <summary>
    /// The mechanic, side on: boots, green overalls over a white shirt, a toolbelt, a round face and a yellow hard
    /// hat; arms swinging as he walks, one up when he jumps (that's the hand that bumps the ledge).
    /// </summary>
    private void DrawMechanic(DrawingContext dc, double x, double y, int facing, double walk, bool jumping, double k, bool knocked = false)
    {
        if (knocked)
        {
            dc.DrawRectangle(Brush(0xFF3A8A3A), null, new Rect(x - 18 * k, y - 12 * k, 30 * k, 10 * k), 4, 4);
            dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(x + 16 * k, y - 8 * k), 7 * k, 7 * k);
            dc.DrawEllipse(Brush(0xFFFFD020), null, new Point(x + 22 * k, y - 10 * k), 6 * k, 7 * k);
            for (int i = 0; i < 3; i++)
            {
                double a = _clock * 6 + i * 2.1;
                Text(dc, "*", new Point(x + 16 * k + Math.Cos(a) * 14, y - 26 * k + Math.Sin(a) * 5), 18, Yellow, center: true, shadow: false);
            }
            return;
        }
        double f = facing, stride = jumping ? 0 : Math.Sin(walk * 14) * 5 * k;
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x, y), 13 * k, 3 * k);
        // Legs and boots.
        foreach (int leg in new[] { -1, 1 })
        {
            double lx = x + leg * 4 * k + leg * stride * 0.6;
            dc.DrawLine(Pen(0xFF2A6A2A, 6 * k), new Point(x + leg * 4 * k, y - 16 * k), new Point(lx, y - (jumping && leg > 0 ? 8 : 4) * k));
            dc.DrawRectangle(Brush(0xFF5A3A1A), null, new Rect(lx - 4 * k + f * 1 * k, y - (jumping && leg > 0 ? 10 : 6) * k, 9 * k, 6 * k), 2, 2);
        }
        // The body: shirt, overalls with a bib, the toolbelt.
        dc.DrawRectangle(Brush(0xFFF0F0E8), null, new Rect(x - 8 * k, y - 30 * k, 16 * k, 10 * k), 3, 3);
        dc.DrawRectangle(Brush(0xFF3A8A3A), Pen(0xFF1A4A1A, 1), new Rect(x - 8 * k, y - 24 * k, 16 * k, 10 * k), 3, 3);
        dc.FillRectangle(Brush(0xFF3A8A3A), new Rect(x - 5 * k, y - 30 * k, 10 * k, 7 * k));
        dc.FillRectangle(Brush(0xFF7A4A1A), new Rect(x - 8 * k, y - 17 * k, 16 * k, 3 * k));
        dc.FillRectangle(Brush(0xFFC0C0C8), new Rect(x + f * 4 * k - 1.5 * k, y - 17 * k, 3 * k, 6 * k));
        // Arms: one up to bump when jumping, else swinging.
        double swing = jumping ? 0 : Math.Sin(walk * 14) * 4 * k;
        dc.DrawLine(Pen(0xFFF0F0E8, 4 * k), new Point(x - f * 6 * k, y - 28 * k), new Point(x - f * 9 * k - swing, y - 19 * k));
        if (jumping) dc.DrawLine(Pen(0xFFF0F0E8, 4 * k), new Point(x + f * 6 * k, y - 28 * k), new Point(x + f * 6 * k, y - 44 * k));
        else dc.DrawLine(Pen(0xFFF0F0E8, 4 * k), new Point(x + f * 6 * k, y - 28 * k), new Point(x + f * 9 * k + swing, y - 19 * k));
        dc.DrawEllipse(Brush(0xFFF0C090), null, jumping ? new Point(x + f * 6 * k, y - 45 * k) : new Point(x + f * 9 * k + swing, y - 18 * k), 2.5 * k, 2.5 * k);
        // The head and the hard hat.
        var head = new Point(x + f * 1 * k, y - 35 * k);
        dc.DrawEllipse(Brush(0xFFF0C090), Pen(0xFF7A5A3A, 1), head, 6.5 * k, 6.5 * k);
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + f * 3 * k, head.Y), 1.2 * k, 1.4 * k);
        dc.DrawEllipse(Brush(0xFFD08A6A), null, new Point(head.X + f * 6 * k, head.Y + 2 * k), 2 * k, 1.6 * k);
        Poly(dc, 0xFFFFD020, 0xFF8A6A10, 1, new Point(head.X - 7 * k, head.Y - 3 * k), new Point(head.X - 6 * k, head.Y - 8 * k), new Point(head.X, head.Y - 11 * k),
            new Point(head.X + 6 * k, head.Y - 8 * k), new Point(head.X + 7 * k, head.Y - 3 * k));
        dc.FillRectangle(Brush(0xFFFFD020), new Rect(head.X - 8 * k + f * 2 * k, head.Y - 4 * k, 16 * k, 2.5 * k));
    }

    // ───────────────────────────── the pests ─────────────────────────────

    /// <summary>Each pest's colour, by kind and by how many times it has got up again.</summary>
    private static uint PestColour(PestKind kind, int level, bool angry, bool last)
    {
        if (last) return 0xFF6AE0FF;
        uint[] shades = kind switch
        {
            PestKind.Shellback => [0xFF3AB04A, 0xFFE0A03A, 0xFF4A8AE0],
            PestKind.Clawcrab => [0xFFE04A3A, 0xFFE07AC0, 0xFF8A6AE0],
            _ => [0xFFB06AE0, 0xFFE0D04A, 0xFF4AE0B0],
        };
        uint c = shades[Math.Min(2, level)];
        return angry ? 0xFFFF2A1A : c;
    }

    /// <summary>
    /// A pest, side on: the shellback a domed shell on stubby legs; the crab a wide body with claws up; the fly a body
    /// with wings beating. On its back it lies upside down with its legs waving, shaking as it gets ready to get up.
    /// </summary>
    private void DrawPest(DrawingContext dc, PestKind kind, double x, double y, int dir, double t, bool flipped, int level, bool angry, bool last, double timer, double k)
    {
        uint col = PestColour(kind, level, angry, last);
        if (flipped && timer < 1.5 && (int)(t * 16) % 2 == 0) x += 2;
        // Points are given with dy upwards from its feet; on its back, the whole drawing turns over on the spot.
        Point Q(double dx, double dy) => flipped ? new(x + dx * k * dir, y - 4 * k - (26 - dy) * k) : new(x + dx * k * dir, y - dy * k);
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x, y), 14 * k, 3 * k);
        double step = Math.Sin(t * (flipped ? 20 : 12)) * 3 * k;
        switch (kind)
        {
            case PestKind.Shellback:
            {
                foreach (int leg in new[] { -1, 1 })
                    dc.DrawLine(Pen(0xFFE0C08A, 4 * k), Q(leg * 7, 8), Q(leg * 8 + leg * step / k * 0.5, 0));
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(Q(-14, 7), true);
                    c.ArcTo(Q(14, 7), new Size(14 * k, 16 * k), 0, false, flipped ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                    c.EndFigure(true);
                }
                dc.DrawGeometry(Brush(col), Pen(0xFF102010, 1.5), g);
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.4), 2), Q(-6, 17), Q(4, 20));
                dc.DrawEllipse(Brush(0xFFE0C08A), Pen(0xFF5A4A2A, 1), Q(15, 10), 5 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFF101010), null, Q(17, 11), 1.2 * k, 1.2 * k);
                break;
            }
            case PestKind.Clawcrab:
            {
                foreach (int leg in new[] { -1, 1 })
                foreach (int n in new[] { 0, 1 })
                    dc.DrawLine(Pen(col, 3 * k), Q(leg * (6 + n * 5), 6), Q(leg * (10 + n * 5) + leg * step / k * 0.4, 0));
                dc.DrawEllipse(Brush(col), Pen(0xFF3A0A0A, 1.5), Q(0, 11), 14 * k, 8 * k);
                foreach (int side in new[] { -1, 1 })
                {
                    dc.DrawLine(Pen(col, 3 * k), Q(side * 10, 14), Q(side * 16, 22));
                    dc.DrawEllipse(Brush(col), Pen(0xFF3A0A0A, 1), Q(side * 17, 25), 5 * k, 4 * k);
                    dc.DrawEllipse(Brush(0xFFFFFFFF), null, Q(side * 4, 18), 2.4 * k, 2.4 * k);
                    dc.DrawEllipse(Brush(0xFF101010), null, Q(side * 4, 18), 1 * k, 1 * k);
                }
                break;
            }
            default:
            {
                double flap = Math.Sin(t * 30) * 6 * k;
                foreach (int side in new[] { -1, 1 })
                    dc.DrawEllipse(Brush(Alpha(0xFFE8E8FF, 0.6)), Pen(Alpha(0xFF8A8AC0, 0.8), 1), Q(-4 + side * 3, 22 + flap / k * 0.3), 8 * k, 4 * k);
                dc.DrawEllipse(Brush(col), Pen(0xFF2A0A3A, 1.5), Q(0, 11), 11 * k, 8 * k);
                foreach (int stripe in new[] { -4, 2 }) dc.DrawLine(Pen(Alpha(0xFF101010, 0.5), 2), Q(stripe, 5), Q(stripe, 17));
                dc.DrawEllipse(Brush(0xFFE04A3A), null, Q(10, 14), 3.5 * k, 3.5 * k);
                dc.DrawLine(Pen(0xFF101010, 1.5), Q(-2, 3), Q(-4, 0));
                dc.DrawLine(Pen(0xFF101010, 1.5), Q(4, 3), Q(5, 0));
                break;
            }
        }
    }

    private static void DrawCoin(DrawingContext dc, double x, double y, double t, double k)
    {
        double w = Math.Abs(Math.Cos(t * 5)) * 10 * k + 2;
        dc.DrawEllipse(Brush(0xFFFFD040), Pen(0xFFA07010, 1.5), new Point(x, y), w, 12 * k);
        if (w > 6) dc.DrawLine(Pen(0xFFFFF0A0, 2), new Point(x, y - 6 * k), new Point(x, y + 6 * k));
    }

    private void DrawEmber(DrawingContext dc, double x, double y, double t)
    {
        for (int i = 3; i >= 1; i--)
        {
            double r = Ember.Radius * (0.6 + i * 0.25) + Math.Sin(t * 20 + i) * 2;
            dc.DrawEllipse(Brush(Alpha(i == 3 ? 0xFFFF4A1A : i == 2 ? 0xFFFF9A2A : 0xFFFFF08A, 0.5 + 0.15 * (3 - i))), null, new Point(x, y), r, r);
        }
        for (int i = 0; i < 4; i++)
        {
            double a = t * 6 + i * 1.6;
            dc.DrawEllipse(Brush(0xC0FFB04A), null, new Point(x - Math.Sign(1) * 6 + Math.Cos(a) * 14, y + Math.Sin(a) * 8 - 6), 2.5, 2.5);
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Spark:
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4, r = 6 + 34 * t;
                    dc.DrawLine(Pen(Alpha(e.Colour, 1 - t), 3), new Point(c.X + Math.Cos(a) * r * 0.5, c.Y + Math.Sin(a) * r * 0.5), new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 26), 24, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 44), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, g.IsBonus ? "BONUS PHASE" : $"PHASE {g.Wave}", new Point(512, 18), 26, Cyan, center: true);
        if (g.IsBonus && g.Phase == Phase.Play) Text(dc, $"{Math.Max(0, g.BonusLeft):0.0}", new Point(512, 50), 30, g.BonusLeft < 5 ? Red : White, center: true);
        else if (!g.IsBonus) for (int i = 0; i < g.ToCome + g.Pests.Count(p => !p.Gone && p.State != PestState.Kicked); i++)
            dc.DrawEllipse(Brush(0xFF3AB04A), null, new Point(512 - 40 + i * 12, 50), 4, 4);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawMechanic(dc, 200 + i * 26, 54, 1, 0, false, 0.6);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 170;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD0101030), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(Girder, 2), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD006060E), new Rect(0, 0, Field.Width, Field.Height));
        string title = "AVA MARIO BROS";
        uint[] hues = [0xFFFFD020, 0xFF3AB04A, 0xFFE04A3A, Cyan];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 100 + Math.Sin(_clock * 3 + i * 0.6) * 4), 104, hues[(i + (int)(_clock * 2)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 164), 28, Dim, center: true);
        // The mechanic jumping up to bump a ledge, a shellback over on its back above.
        double hop = Math.Abs(Math.Sin(_clock * 2.2));
        dc.FillRectangle(Brush(Girder), new Rect(cx - 140, 218, 280, Field.Thick));
        dc.FillRectangle(Brush(0xFF8AB8FF), new Rect(cx - 140, 218, 280, 3));
        DrawPest(dc, PestKind.Shellback, cx + 4, 218, 1, _clock, hop > 0.9, 0, false, false, 9, 1.4);
        DrawMechanic(dc, cx, 340 - hop * 40, 1, _clock, hop > 0.1, 1.5);
        dc.FillRectangle(Brush(Brick), new Rect(cx - 140, 340, 280, 12));
        Text(dc, "BUMP THE PESTS FROM UNDERNEATH TO KNOCK THEM OVER, THEN KICK THEM OFF", new Point(cx, 390), 22, White, center: true);
        (string Name, string Note, Action<double> Draw)[] legend =
        [
            ("SHELLBACK", "ONE BUMP", x => DrawPest(dc, PestKind.Shellback, x, 468, 1, _clock, false, 0, false, false, 9, 1.4)),
            ("CLAWCRAB", "TWO BUMPS", x => DrawPest(dc, PestKind.Clawcrab, x, 468, 1, _clock, false, 0, false, false, 9, 1.4)),
            ("HOPFLY", "WHEN IT LANDS", x => DrawPest(dc, PestKind.Hopfly, x, 468 - Math.Abs(Math.Sin(_clock * 3)) * 16, 1, _clock, false, 0, false, false, 9, 1.4)),
            ("FIREBALL", "1000", x => DrawEmber(dc, x, 452, _clock)),
            ("COIN", "800", x => DrawCoin(dc, x, 452, _clock, 1.3)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 360 + i * 180;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 500), 20, Dim, center: true);
            Text(dc, legend[i].Note, new Point(x, 524), 20, Yellow, center: true);
        }
        Text(dc, $"KICK 800 EACH (MORE IN A ROW)    THE JOLT BLOCK KNOCKS OVER EVERYTHING ON THE FLOOR", new Point(cx, 566), 20, Dim, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 624), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK    SPACE OR UP: JUMP    P: PAUSE    M: SOUND", new Point(cx, 684), 23, Dim, center: true);
        Text(dc, $"EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK AND A", new Point(cx, 714), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD006060E), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "MASTER MECHANICS", new Point(cx, 140), 68, 0xFFFFD020, center: true);
        Text(dc, "RANK NAME    SCORE   PHASE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD006060E), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
