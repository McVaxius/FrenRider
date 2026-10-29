using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace FrenRider.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action }

internal static partial class FrenRiderPresentation
{
    [ThreadStatic] private static Configuration? activeConfiguration;
    [ThreadStatic] private static FrenRiderGlass? activeGlass;
    internal static Configuration? ActiveConfiguration => activeConfiguration;
    internal static MaterialColorMode ColorMode(Configuration config) => config.UiStyle switch
    {
        FrenRiderUiStyle.Glassmorphism => MaterialColorMode.Glassmorphism,
        FrenRiderUiStyle.Spatial => MaterialColorMode.Spatial,
        FrenRiderUiStyle.Cyberpunk => MaterialColorMode.Cyberpunk,
        _ => config.UiColors?.Mode ?? MaterialColorMode.Auto,
    };
    internal static bool BackgroundBlurEnabled(Configuration config)
        => config.UiStylingEnabled && config.UiColors is { } settings && settings.HasBackgroundBlur(ColorMode(config));
    internal static float BackgroundFrost(Configuration config)
        => BackgroundBlurEnabled(config) ? config.UiColors.BackgroundBlurPercent / 100f : 0;
    internal static FrenRiderUiStyle Style => activeConfiguration?.UiStylingEnabled == false ? FrenRiderUiStyle.Classic : activeConfiguration?.UiStyle switch
    {
        FrenRiderUiStyle.Glassmorphism => FrenRiderUiStyle.Glassmorphism,
        FrenRiderUiStyle.Spatial => FrenRiderUiStyle.Spatial,
        FrenRiderUiStyle.Cyberpunk => FrenRiderUiStyle.Cyberpunk,
        _ => FrenRiderUiStyle.Classic,
    };
    internal static bool IsLcars => activeConfiguration is { } config
        ? config.UiStylingEnabled && Style == FrenRiderUiStyle.Classic && config.UiColors?.Mode == MaterialColorMode.Lcars
        : MaterialTheme.Current.IsLcars;
    internal static bool Decorated => activeConfiguration?.UiStylingEnabled != false && (Style != FrenRiderUiStyle.Classic || IsLcars);
    internal static Vector4 LcarsBackground => Rgb(activeConfiguration?.UiLcarsBackgroundRgb ?? Configuration.DefaultLcarsBackgroundRgb);
    internal static Vector4 LcarsText => ReadableInk(LcarsBackground);
    internal static Vector4 LcarsBar(int index) => Rgb(index switch
    {
        0 => activeConfiguration?.UiLcarsBar1Rgb ?? Configuration.DefaultLcarsBar1Rgb,
        1 => activeConfiguration?.UiLcarsBar2Rgb ?? Configuration.DefaultLcarsBar2Rgb,
        _ => activeConfiguration?.UiLcarsBar3Rgb ?? Configuration.DefaultLcarsBar3Rgb,
    });
    internal static Vector4 ReadableInk(Vector4 surface) =>
        MaterialColor.Contrast(surface, Vector4.One) >= MaterialColor.Contrast(surface, new Vector4(0, 0, 0, 1))
            ? Vector4.One : new(0, 0, 0, 1);
    internal static AppearanceScope Push(Configuration config, FrenRiderGlass? renderer = null) => new(config, renderer);
    internal readonly ref struct AppearanceScope
    {
        private readonly Configuration? previous;
        private readonly FrenRiderGlass? previousGlass;
        internal AppearanceScope(Configuration config, FrenRiderGlass? renderer)
        {
            previous = activeConfiguration;
            previousGlass = activeGlass;
            activeConfiguration = config;
            activeGlass = renderer;
        }
        public void Dispose()
        {
            activeConfiguration = previous;
            activeGlass = previousGlass;
        }
    }

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
    internal static MaterialTheme Theme(uint accent) => Theme(accent, new MaterialColorSettings());

    internal static MaterialColorSettings Palette(Configuration config) => config.UiStyle switch
    {
        FrenRiderUiStyle.Glassmorphism => config.UiGlassColors ?? Configuration.DefaultPalette(FrenRiderUiStyle.Glassmorphism),
        FrenRiderUiStyle.Spatial => config.UiSpatialColors ?? Configuration.DefaultPalette(FrenRiderUiStyle.Spatial),
        FrenRiderUiStyle.Cyberpunk => config.UiCyberpunkColors ?? Configuration.DefaultPalette(FrenRiderUiStyle.Cyberpunk),
        _ => config.UiColors ?? new(),
    };

    internal static MaterialTheme Theme(Configuration config)
    {
        var style = config.UiStyle;
        var lcars = style == FrenRiderUiStyle.Classic && config.UiColors?.Mode == MaterialColorMode.Lcars;
        if (style is not (FrenRiderUiStyle.Glassmorphism or FrenRiderUiStyle.Spatial or FrenRiderUiStyle.Cyberpunk) && !lcars)
            return Theme(config.UiAccentRgb, config.UiColors ?? new());
        var selected = Palette(config);
        var background = Rgb(lcars ? config.UiLcarsBackgroundRgb : selected.BackgroundRgb);
        var primary = Rgb(lcars ? config.UiLcarsBar1Rgb : selected.ControlsRgb);
        var text = lcars ? ReadableInk(background) : Rgb(selected.TextRgb);
        Vector4 Mix(Vector4 a, Vector4 b, float amount) => Vector4.Lerp(a, b, amount) with { W = 1 };
        Vector4 Rotate(float angle, float chroma = 1)
        {
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(primary.X, primary.Y, primary.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chroma, lch.Z + angle), 1);
        }
        var light = ReadableInk(background).X == 0;
        var surface = lcars ? background : Mix(background, Vector4.One, light ? .55f : .045f);
        if (style == FrenRiderUiStyle.Cyberpunk) surface = Mix(surface, primary, .045f);
        var well = lcars ? background : Mix(background, new(0, 0, 0, 1), light ? .045f : .18f);
        var secondary = lcars ? Rgb(config.UiLcarsBar2Rgb)
            : style == FrenRiderUiStyle.Cyberpunk ? Rotate(2.35f, 1.5f) : Rotate(1.2f);
        var tertiary = lcars ? Rgb(config.UiLcarsBar3Rgb)
            : Rotate(style == FrenRiderUiStyle.Cyberpunk ? 1.2f : 2.35f);
        var palette = new OklchPaletteGenerator().Generate(new(primary.X, primary.Y, primary.Z));
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = text, Surface = surface, OnSurface = text,
            SurfaceContainerLowest = well, SurfaceContainerLow = surface,
            SurfaceContainer = lcars ? background : Mix(surface, text, .025f),
            SurfaceContainerHigh = Mix(surface, text, .055f), SurfaceContainerHighest = Mix(surface, text, .1f),
            SurfaceVariant = Mix(surface, text, .16f), OnSurfaceVariant = Mix(text, background, .23f),
            Outline = Mix(background, primary, .5f), OutlineVariant = Mix(background, primary, .25f),
            Primary = primary, OnPrimary = ReadableInk(primary), PrimaryContainer = lcars ? background : Mix(surface, primary, .1f),
            OnPrimaryContainer = text, Secondary = secondary, OnSecondary = ReadableInk(secondary),
            SecondaryContainer = Mix(surface, primary, .18f), OnSecondaryContainer = text,
            Tertiary = tertiary, OnTertiary = ReadableInk(tertiary), TertiaryContainer = Mix(surface, tertiary, .12f),
            OnTertiaryContainer = text, InverseSurface = text, InverseOnSurface = background, InversePrimary = primary,
        };
        return new(colors) { SurfaceOpacity = 1 };
    }

    internal static MaterialTheme Theme(uint accent, MaterialColorSettings colorSettings)
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
        return new(colorSettings.Apply(colors)) { SurfaceOpacity = 1 };
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
