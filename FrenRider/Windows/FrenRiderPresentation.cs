using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace FrenRider.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action }

internal static class FrenRiderPresentation
{
    // Approved regular/compact Main and Mini envelopes; native Main version chrome is retained.
    internal const uint ReferenceAccent = 0x70C677;
    internal static readonly float[] FontSizes = [16, 16, 26, 20, 22, 18];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "seguisb.ttf", "seguisb.ttf", "seguisb.ttf"];
    internal static float HeaderHeight(bool compact) => compact ? 50 : 56;
    internal static float MiniWidth(bool compact) => compact ? 398 : 380;
    internal static float MiniTitleHeight(bool compact) => compact ? 50 : 54;
    internal static float MiniHeight(bool compact, bool eureka) => eureka ? compact ? 286 : 300 : compact ? 188 : 206;
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    internal static MaterialControlMetrics Controls(float height, float icon = 20)
    {
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(12 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = icon * s, Rounding = 4 * s, ItemSpacing = new(10 * s, 4 * s), CellPadding = new(12 * s, 8 * s) };
    }
    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var reference = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new Vector3(Rgb(ReferenceAccent).X, Rgb(ReferenceAccent).Y, Rgb(ReferenceAccent).Z)));
        var selected = Rgb(accent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var hueShift = seed.Y < .001f ? 0 : seed.Z - reference.Z;
        var chromaScale = seed.Y < .001f ? 0 : seed.Y / reference.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chromaScale, lch.Z + hueShift), 1);
        }
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var background = Relative(0x102024);
        var foreground = Relative(0xE8EEF2);
        var primary = Relative(ReferenceAccent);
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground, Surface = Relative(0x15292E), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x112226), SurfaceContainerLow = Relative(0x15292E),
            SurfaceContainer = Relative(0x1A3035), SurfaceContainerHigh = Relative(0x20383D), SurfaceContainerHighest = Relative(0x29434A),
            SurfaceVariant = Relative(0x365056), OnSurfaceVariant = Relative(0xB7C7CD),
            Outline = Relative(0x426067), OutlineVariant = Relative(0x304A51),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(0x396D40), OnPrimaryContainer = foreground,
            Secondary = Relative(0x85B5C0), OnSecondary = background, SecondaryContainer = Relative(0x20383D), OnSecondaryContainer = foreground,
            Tertiary = Relative(0xA8C3A9), OnTertiary = background, TertiaryContainer = Relative(0x2D4235), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x36643B),
        };
        return new(colors) { SurfaceOpacity = 1 };
    }

    internal static void People(Vector2 origin, float size, Vector4 color)
    {
        // Plugin-owned branding; no font icons or shared-library plugin assets.
        var dl = ImGui.GetWindowDrawList();
        var ink = MaterialCanvas.Color(color);
        dl.AddCircleFilled(origin + new Vector2(.36f, .24f) * size, .16f * size, ink, 24);
        dl.AddCircleFilled(origin + new Vector2(.78f, .34f) * size, .12f * size, ink, 24);
        dl.AddRectFilled(origin + new Vector2(.12f, .45f) * size, origin + new Vector2(.60f, .91f) * size, ink, size * .15f);
        dl.AddRectFilled(origin + new Vector2(.65f, .52f) * size, origin + new Vector2(.97f, .91f) * size, ink, size * .11f);
    }

    internal static void Person(Vector2 origin, float size, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = MaterialCanvas.Color(color);
        dl.AddCircleFilled(origin + new Vector2(.5f, .24f) * size, .2f * size, ink, 24);
        dl.AddRectFilled(origin + new Vector2(.12f, .48f) * size, origin + new Vector2(.88f, .94f) * size, ink, size * .2f);
    }

    internal static void Job(Vector2 origin, float size, uint classJobId)
    {
        if (classJobId == 0 || !Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(classJobId, out var job))
            return;

        // The game's job-icon family is indexed by the sheet row, independent of translated names.
        var texture = Plugin.TextureProvider.GetFromGameIcon(new(62000u + job.RowId)).GetWrapOrDefault();
        if (texture != null)
            ImGui.GetWindowDrawList().AddImage(texture.Handle, origin, origin + new Vector2(size));
    }

    internal static void Flag(Vector2 origin, float size, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = MaterialCanvas.Color(color);
        dl.AddLine(origin + new Vector2(.15f, .05f) * size, origin + new Vector2(.15f, .96f) * size, ink, size * .08f);
        dl.AddTriangleFilled(origin + new Vector2(.2f, .1f) * size, origin + new Vector2(.92f, .25f) * size, origin + new Vector2(.2f, .58f) * size, ink);
    }
}
