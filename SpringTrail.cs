using System.Drawing.Drawing2D;
using System.Numerics;

namespace CursorBlur;

// Adapted from Springy Cursor's four-corner runtime. See SpringyCursor-LICENSE.txt.
internal sealed class SpringTrail
{
    private readonly record struct Preset(float Duration, float[] CornerFactors, float LeadingFactor);

    private static readonly Preset[] Presets =
    {
        new(0.125f, new[] { 1f, 0.9f, 0.5f, 0.3f }, 0.16f),
        new(0.080f, new[] { 0.85f, 0.75f, 0.5f, 0.35f }, 0.19f),
        new(0.100f, new[] { 0.7f, 0.65f, 0.55f, 0.45f }, 0f),
    };

    private static readonly Vector2[] Corners =
    {
        new(-1, -1), new(1, -1), new(1, 1), new(-1, 1),
    };

    private struct Corner
    {
        public Vector2 Offset;
        public Vector2 Velocity;
        public float Duration;

        public void Step(float dt)
        {
            // Closed-form solution for a critically damped spring.
            float frequency = 4f / Duration;
            Vector2 coefficient = Velocity + frequency * Offset;
            float decay = MathF.Exp(-frequency * dt);
            Vector2 displacement = Offset + coefficient * dt;
            Offset = displacement * decay;
            Velocity = (coefficient - frequency * displacement) * decay;
        }
    }

    private readonly Corner[] springs = new Corner[4];
    private readonly PointF[] vertices = new PointF[4];

    internal static readonly Color Gold = Color.FromArgb(255, 183, 39);

    public void Reset() => Array.Clear(springs);

    public bool Advance(Rectangle previous, Rectangle current, float seconds, int presetIndex)
    {
        Vector2 center = Center(current);
        Vector2 movement = center - Center(previous);
        Preset preset = Presets[presetIndex];

        if (movement != Vector2.Zero)
        {
            // Give single-character moves a shorter animation, as in the editor extension.
            bool shortMove = previous.Y == current.Y &&
                MathF.Abs(movement.X) <= Math.Max(current.Width * 8, current.Height);
            float duration = shortMove ? preset.Duration * 0.4f : preset.Duration;
            Vector2 direction = Vector2.Normalize(movement / new Vector2(current.Width, current.Height));
            Span<float> alignment = stackalloc float[4];
            Span<int> order = stackalloc int[4] { 0, 1, 2, 3 };
            for (int i = 0; i < Corners.Length; i++)
                alignment[i] = Vector2.Dot(direction, Corners[i]) * 0.70710678f;
            for (int i = 1; i < order.Length; i++)
            {
                int corner = order[i];
                int j = i;
                while (j > 0 && alignment[corner] < alignment[order[j - 1]])
                {
                    order[j] = order[j - 1];
                    j--;
                }
                order[j] = corner;
            }

            for (int rank = 0; rank < order.Length; rank++)
            {
                int i = order[rank];
                ref Corner spring = ref springs[i];
                bool snap = preset.LeadingFactor > 0 && alignment[i] > 0.5f;
                spring.Duration = duration * (snap ? preset.LeadingFactor : preset.CornerFactors[rank]);
                spring.Offset -= movement;
                const float maxLag = 280f;
                if (spring.Offset.LengthSquared() > maxLag * maxLag)
                    spring.Offset = Vector2.Normalize(spring.Offset) * maxLag;
            }
        }

        float lag = 0;
        Vector2 halfSize = new(current.Width / 2f, current.Height / 2f);
        for (int i = 0; i < springs.Length; i++)
        {
            ref Corner spring = ref springs[i];
            if (spring.Duration > 0)
                spring.Step(Math.Clamp(seconds, 0.001f, 0.05f));
            lag = Math.Max(lag, spring.Offset.Length());
            Vector2 corner = center + Corners[i] * halfSize + spring.Offset;
            vertices[i] = new PointF(corner.X, corner.Y);
        }

        if (lag > 0.65f)
        {
            Opacity = Math.Clamp(lag / 5f, 0f, 1f);
            return true;
        }

        Reset();
        return false;
    }

    private float Opacity { get; set; }

    private static Vector2 Center(Rectangle bounds) => new(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

    public Rectangle Bounds()
    {
        const int glowMargin = 10;
        return Rectangle.FromLTRB(
            (int)MathF.Floor(vertices.Min(p => p.X)) - glowMargin,
            (int)MathF.Floor(vertices.Min(p => p.Y)) - glowMargin,
            (int)MathF.Ceiling(vertices.Max(p => p.X)) + glowMargin,
            (int)MathF.Ceiling(vertices.Max(p => p.Y)) + glowMargin);
    }

    public void Draw(Graphics graphics)
    {
        float alpha = Opacity;
        using var outer = new Pen(Color.FromArgb((int)(26 * alpha), Gold), 14) { LineJoin = LineJoin.Round };
        using var inner = new Pen(Color.FromArgb((int)(76 * alpha), Gold), 7) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color.FromArgb((int)(200 * alpha), Gold));
        graphics.DrawPolygon(outer, vertices);
        graphics.DrawPolygon(inner, vertices);
        graphics.FillPolygon(fill, vertices);
    }
}
