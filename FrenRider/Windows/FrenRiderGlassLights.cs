using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace FrenRider.Windows;

internal static partial class FrenRiderPresentation
{
    private static void DrawGlassLights(ImDrawListPtr dl, Vector2 min, Vector2 max, bool glass)
    {
        var size = max - min;
        if (size.X <= 0 || size.Y <= 0) return;
        var config = ActiveConfiguration;
        var first = Rgb(config?.UiGlassLight1Rgb ?? Configuration.DefaultGlassWave1Rgb);
        var second = Rgb(config?.UiGlassLight2Rgb ?? Configuration.DefaultGlassWave2Rgb);
        var third = Rgb(config?.UiGlassLight3Rgb ?? Configuration.DefaultGlassWave3Rgb);
        var animated = config?.UiGlassAnimation != false;
        var time = animated ? (float)ImGui.GetTime() * .35f * (config?.UiGlassAnimationSpeed ?? 1) : 0;
        var pointer = animated ? (ImGui.GetMousePos() - min) / size : new Vector2(.5f);
        if (!float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y)) pointer = new(.5f);
        pointer = Vector2.Clamp(pointer, Vector2.Zero, Vector2.One);
        var effect = config?.UiGlassLights ?? FrenRiderGlassLights.AmbientGlows;
        var columns = Math.Clamp((int)MathF.Ceiling(size.X / (16 * SkinScale)), 32, 64);
        var rows = Math.Clamp((int)MathF.Ceiling(size.Y / (16 * SkinScale)), 24, 48);
        Span<uint> previous = stackalloc uint[65];
        Span<uint> current = stackalloc uint[65];
        var center1 = new Vector2(.28f + .17f * MathF.Sin(time * .61f), .33f + .20f * MathF.Cos(time * .47f));
        var center2 = new Vector2(.73f + .14f * MathF.Cos(time * .53f), .62f + .19f * MathF.Sin(time * .43f));
        var center3 = new Vector2(.50f + .22f * MathF.Sin(time * .37f + 1), .72f + .14f * MathF.Cos(time * .57f));
        for (var row = 0; row <= rows; row++)
        {
            var v = row / (float)rows;
            for (var column = 0; column <= columns; column++)
            {
                var point = new Vector2(column / (float)columns, v);
                float a, b, c;
                if (effect == FrenRiderGlassLights.SweepingBeams)
                {
                    a = Band(point.X - .32f * point.Y - .23f - .25f * MathF.Sin(time * .6f), 100);
                    b = Band(point.X + .41f * point.Y - .88f - .22f * MathF.Cos(time * .51f), 85);
                    c = Band(point.Y - .21f * point.X - .55f - .25f * MathF.Sin(time * .43f + 1), 110);
                }
                else if (effect == FrenRiderGlassLights.PointerGlow)
                {
                    a = Glow(point, pointer, 13);
                    b = Glow(point, pointer + new Vector2(.15f, -.10f), 18) * .65f;
                    c = Glow(point, pointer + new Vector2(-.12f, .13f), 18) * .55f;
                }
                else
                {
                    a = Glow(point, center1, 9);
                    b = Glow(point, center2, 8);
                    c = Glow(point, center3, 11);
                }
                var strength = MathF.Max(a, MathF.Max(b, c));
                var color = MaterialTheme.Current.Colors.Background;
                color = Vector4.Lerp(color, first, a * .68f);
                color = Vector4.Lerp(color, second, b * .60f);
                color = Vector4.Lerp(color, third, c * .56f);
                color.W = glass ? strength * .55f : 1;
                current[column] = Ink(color);
            }
            if (row > 0)
            {
                var top = min.Y + size.Y * ((row - 1) / (float)rows);
                var bottom = min.Y + size.Y * v;
                for (var column = 0; column < columns; column++)
                {
                    var left = min.X + size.X * (column / (float)columns);
                    var right = min.X + size.X * ((column + 1) / (float)columns);
                    dl.AddRectFilledMultiColor(new(left, top), new(right, bottom),
                        previous[column], previous[column + 1], current[column + 1], current[column]);
                }
            }
            var swap = previous;
            previous = current;
            current = swap;
        }
    }

    private static float Band(float distance, float falloff) => MathF.Exp(-distance * distance * falloff);
    private static float Glow(Vector2 point, Vector2 center, float falloff) => MathF.Exp(-Vector2.DistanceSquared(point, center) * falloff);
}
