using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace FrenRider.Windows;

internal static partial class FrenRiderPresentation
{
    private static Vector4 Alpha(Vector4 color, float opacity) => new(color.X, color.Y, color.Z, color.W * opacity);
    private static uint Ink(Vector4 color, float opacity = 1) => MaterialCanvas.Color(Alpha(color, opacity));
    private static float SkinScale => MaterialTheme.Metrics.Scale;

    private static bool DrawGlassBackground(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding,
        Vector4 tint, bool windowBackground = false)
    {
        if (activeConfiguration is not { } config || !BackgroundBlurEnabled(config) || activeGlass == null)
            return false;
        tint.W = .42f * ImGui.GetStyle().Alpha;
        return activeGlass.DrawBackground(dl, min, max, rounding, tint, BackgroundFrost(config), windowBackground);
    }

    internal static void DrawBackdrop(float opacity = 1)
    {
        if (activeGlass != null)
            activeGlass.Opacity = activeConfiguration?.UiTransparencyEnabled == true
                ? ImGui.IsWindowAppearing() ? Math.Clamp(activeConfiguration.UiWindowOpacityPercent, 10, 100) / 100f : opacity
                : 1;
        if (!Decorated && (activeConfiguration is not { } config || !BackgroundBlurEnabled(config))) return;
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var c = MaterialTheme.Current.Colors;
        var glass = DrawGlassBackground(dl, min, max, ImGui.GetStyle().WindowRounding,
            IsLcars ? LcarsBackground : c.Background, windowBackground: true);
        activeGlassRoot = glass ? ImGuiP.GetCurrentWindowRead().RootWindow.ID : 0;
        if (glass && (IsLcars || Style != FrenRiderUiStyle.Glassmorphism)) return;
        if (!Decorated)
        {
            var bodyMin = min + new Vector2(0, ImGuiP.TitleBarHeight(ImGuiP.GetCurrentWindow()));
            dl.AddRectFilled(bodyMin, max, Ink(c.Background), ImGui.GetStyle().WindowRounding,
                ImDrawFlags.RoundCornersBottom);
            return;
        }
        dl.PushClipRect(min, max, true);
        try
        {
            if (!glass) dl.AddRectFilled(min, max, Ink(IsLcars ? LcarsBackground : c.Background));
            if (IsLcars) return;
            if (Style == FrenRiderUiStyle.Glassmorphism)
            {
                var size = max - min;
                if (size.X <= 0 || size.Y <= 0) return;
                var wave1 = Rgb(ActiveConfiguration?.UiGlassWave1Rgb ?? Configuration.DefaultGlassWave1Rgb);
                var wave2 = Rgb(ActiveConfiguration?.UiGlassWave2Rgb ?? Configuration.DefaultGlassWave2Rgb);
                var wave3 = Rgb(ActiveConfiguration?.UiGlassWave3Rgb ?? Configuration.DefaultGlassWave3Rgb);
                var time = ActiveConfiguration?.UiGlassAnimation != false
                    ? (float)ImGui.GetTime() * .65f * (ActiveConfiguration?.UiGlassAnimationSpeed ?? 1f) : 0;
                // Interpolated corner colours form soft ribbons without circle edges or translucent layer steps.
                // Cap this backdrop at 22,528 vertices, leaving room for the window's controls on a 16-bit draw list.
                var columns = Math.Clamp((int)MathF.Ceiling(size.X / (12 * SkinScale)), 48, 88);
                var rows = Math.Clamp((int)MathF.Ceiling(size.Y / (12 * SkinScale)), 32, 64);
                Span<uint> previousColors = stackalloc uint[89];
                Span<uint> currentColors = stackalloc uint[89];
                Span<float> crossingCenters = stackalloc float[89];
                for (var column = 0; column <= columns; column++)
                {
                    var u = column / (float)columns;
                    crossingCenters[column] = .80f - .68f * u + .14f * MathF.Sin(u * 5.3f - time * .83f + .7f);
                }
                for (var row = 0; row <= rows; row++)
                {
                    var v = row / (float)rows;
                    var primaryCenter = .22f + .38f * v + .11f * MathF.Sin(v * 5 - time * .9f);
                    var secondaryCenter = .84f - .38f * v + .13f * MathF.Sin(v * 5.2f + time * .72f + .8f);
                    for (var column = 0; column <= columns; column++)
                    {
                        var u = column / (float)columns;
                        var primaryDistance = u - primaryCenter;
                        var secondaryDistance = u - secondaryCenter;
                        var crossingDistance = v - crossingCenters[column];
                        var primaryBand = MathF.Exp(-primaryDistance * primaryDistance * 45);
                        var secondaryBand = MathF.Exp(-secondaryDistance * secondaryDistance * 55);
                        var crossingBand = MathF.Exp(-crossingDistance * crossingDistance * 65);
                        var color = Vector4.Lerp(c.Background, wave2, .14f + .07f * u + .04f * v);
                        color = Vector4.Lerp(color, wave1, primaryBand * .58f);
                        color = Vector4.Lerp(color, wave2, secondaryBand * .50f);
                        color = Vector4.Lerp(color, wave3, crossingBand * .50f);
                        var crest = MathF.Max(primaryBand, MathF.Max(secondaryBand, crossingBand));
                        var waveOpacity = glass ? crest * .58f : 1;
                        crest *= crest;
                        crest *= crest;
                        crest *= crest;
                        color = Vector4.Lerp(color, c.OnSurface, crest * .10f);
                        color.W = waveOpacity;
                        currentColors[column] = Ink(color);
                    }
                    if (row > 0)
                    {
                        var top = min.Y + size.Y * ((row - 1) / (float)rows);
                        var bottom = min.Y + size.Y * v;
                        for (var column = 0; column < columns; column++)
                        {
                            var left = min.X + size.X * (column / (float)columns);
                            var right = min.X + size.X * ((column + 1) / (float)columns);
                            dl.AddRectFilledMultiColor(new Vector2(left, top), new Vector2(right, bottom),
                                previousColors[column], previousColors[column + 1], currentColors[column + 1], currentColors[column]);
                        }
                    }
                    var swap = previousColors;
                    previousColors = currentColors;
                    currentColors = swap;
                }
            }
            else if (Style == FrenRiderUiStyle.Spatial)
            {
                dl.AddRectFilledMultiColor(min, max, Ink(c.SurfaceContainerLow), Ink(c.Background), Ink(c.SurfaceContainer), Ink(c.Background));
            }
        }
        finally { dl.PopClipRect(); }
    }

    internal static void DrawPanel(Vector2 min, Vector2 max, int accent = 0, bool inset = false, bool background = false)
    {
        if ((!Decorated && (activeConfiguration is not { } config || !BackgroundBlurEnabled(config)))
            || max.X <= min.X || max.Y <= min.Y) return;
        var dl = ImGui.GetWindowDrawList();
        var c = MaterialTheme.Current.Colors;
        var s = SkinScale;
        var rounding = IsLcars ? 9 : Style switch
        {
            FrenRiderUiStyle.Glassmorphism => 10,
            FrenRiderUiStyle.Spatial => inset ? 6 : 11,
            FrenRiderUiStyle.Cyberpunk => 3,
            _ => 6,
        };
        if (!IsLcars)
        {
            if (Style == FrenRiderUiStyle.Glassmorphism)
                dl.AddRectFilled(min + new Vector2(0, 3 * s), max + new Vector2(0, 3 * s), Ink(new Vector4(0, 0, 0, .15f)), 10 * s);
            else if (Style == FrenRiderUiStyle.Spatial && !inset)
                ContactShadow(dl, min, max, 11 * s);
            else if (Style == FrenRiderUiStyle.Cyberpunk && !inset)
                dl.AddRectFilled(min + new Vector2(0, 3 * s), max + new Vector2(0, 3 * s), Ink(new Vector4(0, 0, 0, .22f)), 3 * s);
        }
        var glass = background && DrawGlassBackground(dl, min, max, rounding * s,
            IsLcars ? LcarsBackground : c.Background);
        if (!Decorated)
        {
            if (!glass) dl.AddRectFilled(min, max, Ink(c.SurfaceContainerLow), 6 * s);
            dl.AddRect(min, max, Ink(c.OutlineVariant), 6 * s);
            return;
        }
        if (IsLcars)
        {
            var bar = LcarsBar(accent);
            if (!glass) dl.AddRectFilled(min, max, Ink(LcarsBackground), 9 * s);
            dl.AddRect(min, max, Ink(bar, .35f), 9 * s);
            if (inset) return;
            var band = Math.Min(8 * s, (max.X - min.X) * .08f);
            dl.AddRectFilled(min, new Vector2(min.X + band, max.Y), Ink(bar), 5 * s);
            if (!glass) dl.AddRectFilled(min + new Vector2(band, 0), new Vector2(min.X + band + 5 * s, max.Y), Ink(LcarsBackground));
            return;
        }
        if (Style == FrenRiderUiStyle.Glassmorphism)
        {
            GlassGlow(dl, min, max, 10 * s, inset ? .55f : 1);
            if (!glass) dl.AddRectFilled(min, max, Ink(inset ? c.SurfaceContainerLowest : c.SurfaceContainerLow, inset ? .68f : .42f), 10 * s);
            dl.AddRect(min, max, Ink(Vector4.Lerp(c.Primary, c.OnSurface, .35f), inset ? .5f : .8f), 10 * s);
            dl.AddLine(min + new Vector2(10, 1) * s, new Vector2(max.X - 10 * s, min.Y + s), Ink(c.OnSurface, .36f), s);
        }
        else if (Style == FrenRiderUiStyle.Spatial)
        {
            if (inset)
            {
                if (!glass) dl.AddRectFilled(min, max, Ink(c.SurfaceContainer), 6 * s);
                dl.AddRect(min, max, Ink(c.Outline, .25f), 6 * s);
                dl.AddLine(min + new Vector2(6, 1) * s, new Vector2(max.X - 6 * s, min.Y + s), Ink(c.Outline, .3f), 2 * s);
                dl.AddLine(new Vector2(min.X + 6 * s, max.Y - s), max - new Vector2(6, 1) * s, Ink(Vector4.One, .8f), s);
            }
            else
            {
                if (!glass) dl.AddRectFilled(min, max, Ink(c.SurfaceContainerLow), 11 * s);
                dl.AddRect(min, max, Ink(c.Outline, .22f), 11 * s);
                dl.AddLine(min + new Vector2(11, 1) * s, new Vector2(max.X - 11 * s, min.Y + s), Ink(Vector4.One, .95f), s);
            }
        }
        else if (Style == FrenRiderUiStyle.Cyberpunk)
        {
            if (!glass) dl.AddRectFilled(min, max, Ink(inset ? c.SurfaceContainerLowest : c.SurfaceContainerLow), 3 * s);
            dl.AddLine(min + new Vector2(3, 1) * s, new Vector2(max.X - 3 * s, min.Y + s), Ink(c.OnSurface, .06f), s);
        }
    }

    internal static Vector4 BackgroundFill(Vector4 color)
    {
        if (activeGlassRoot == 0) return color;
        var window = ImGuiP.GetCurrentWindowRead();
        return !window.IsNull && window.RootWindow.ID == activeGlassRoot ? Vector4.Zero : color;
    }

    internal static MaterialStyleScope GlassBackgroundStyle()
    {
        var scope = new MaterialStyleScope();
        if (BackgroundFill(Vector4.One) != Vector4.Zero) return scope;
        scope.Color(ImGuiCol.ChildBg, Vector4.Zero);
        scope.Color(ImGuiCol.TableHeaderBg, Vector4.Zero);
        scope.Color(ImGuiCol.TableRowBg, Vector4.Zero);
        scope.Color(ImGuiCol.TableRowBgAlt, Vector4.Zero);
        return scope;
    }

    private static void GlassGlow(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius, float opacity)
    {
        var s = SkinScale;
        var accent = MaterialTheme.Current.Colors.Primary;
        for (var layer = 4; layer > 0; layer--)
        {
            var spread = new Vector2(layer * .75f * s);
            dl.AddRect(min - spread, max + spread, Ink(accent, opacity * (5 - layer) * .035f),
                radius + spread.X, ImDrawFlags.None, 2 * s);
        }
    }

    private static void ContactShadow(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius)
    {
        var s = SkinScale;
        for (var layer = 5; layer > 0; layer--)
        {
            var spread = new Vector2(layer * .7f * s, layer * .4f * s);
            dl.AddRectFilled(min - spread + new Vector2(0, 3 * s), max + spread + new Vector2(0, 3 * s), Ink(new Vector4(.09f, .16f, .23f, .035f)), radius + layer * s);
        }
    }

    private static void Chamfer(ImDrawListPtr dl, Vector2 min, Vector2 max, float cut, Vector4 color, bool fill, float thickness = 1)
    {
        cut = Math.Clamp(cut, 0, Math.Min(max.X - min.X, max.Y - min.Y) * .5f);
        dl.PathClear();
        dl.PathLineTo(min + new Vector2(cut, 0));
        dl.PathLineTo(new Vector2(max.X - cut, min.Y));
        dl.PathLineTo(new Vector2(max.X, min.Y + cut));
        dl.PathLineTo(max - new Vector2(0, cut));
        dl.PathLineTo(max - new Vector2(cut, 0));
        dl.PathLineTo(new Vector2(min.X + cut, max.Y));
        dl.PathLineTo(new Vector2(min.X, max.Y - cut));
        dl.PathLineTo(min + new Vector2(0, cut));
        if (fill) dl.PathFillConvex(Ink(color));
        else dl.PathStroke(Ink(color), ImDrawFlags.Closed, thickness);
    }

    internal static void LcarsHeader(string title)
    {
        if (!IsLcars) return;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var s = SkinScale;
        var height = Math.Max(MaterialText.Measure(title).Y + 8 * s, 29 * s);
        var dl = ImGui.GetWindowDrawList();
        var labelWidth = Math.Min(width * .62f, MaterialText.Measure(title).X + 28 * s);
        dl.AddRectFilled(min, min + new Vector2(Math.Max(1, labelWidth), height), Ink(LcarsBar(0)), 9 * s);
        dl.PushClipRect(min + new Vector2(9 * s, 0), min + new Vector2(Math.Max(10 * s, labelWidth - 7 * s), height), true);
        try { MaterialText.AddText(dl, min + new Vector2(12 * s, (height - MaterialText.Measure(title).Y) * .5f), Ink(ReadableInk(LcarsBar(0))), title); }
        finally { dl.PopClipRect(); }
        SegmentedRail(dl, min + new Vector2(labelWidth + 5 * s, height - 10 * s), min + new Vector2(width, height), 1);
        ImGui.Dummy(new Vector2(width, height + 4 * s));
    }

    internal static void LcarsHeading(string title, int accent = 0)
    {
        if (Style == FrenRiderUiStyle.Cyberpunk && !IsLcars)
        {
            CyberpunkHeading(title, accent);
            return;
        }
        if (!IsLcars)
        {
            MaterialText.Text(title);
            ImGui.Separator();
            return;
        }
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var s = SkinScale;
        var textWidth = Math.Max(1, width - 22 * s);
        var size = MaterialText.Measure(title, wrapWidth: textWidth);
        var height = Math.Max(24 * s, size.Y + 6 * s);
        var bar = LcarsBar(accent);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(width, height), Ink(bar), 7 * s);
        dl.PushClipRect(origin, origin + new Vector2(width, height), true);
        try { MaterialText.AddText(dl, origin + new Vector2(11 * s, (height - size.Y) * .5f), Ink(ReadableInk(bar)), title, wrapWidth: textWidth); }
        finally { dl.PopClipRect(); }
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void CyberpunkHeading(string title, int accent)
    {
        title = UiText.Current.Culture.TextInfo.ToUpper(title);
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var s = SkinScale;
        var c = MaterialTheme.Current.Colors;
        var left = Math.Min((accent == 0 ? 18 : 52) * s, width * .25f);
        var right = Math.Min(24 * s, width * .15f);
        var tabWidth = Math.Min(width, Math.Max(256 * s, MaterialText.Measure(title).X + left + right));
        var textWidth = Math.Max(1, tabWidth - left - right);
        var size = MaterialText.Measure(title, wrapWidth: textWidth);
        var height = Math.Max(28 * s, size.Y + 8 * s);
        var cut = Math.Min(right, height * .7f);
        var lead = accent == 0 ? 0 : Math.Min(32 * s, left * .65f);
        var tabStart = accent == 0 ? 0 : Math.Min(8 * s, left * .15f);
        var gap = accent == 0 ? 0 : Math.Min(6 * s, left * .15f);
        var tab = accent == 0 ? c.Primary : Vector4.Lerp(c.OnSurface, c.Primary, .10f);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, origin + new Vector2(width, height), true);
        try
        {
            dl.AddRectFilled(origin, origin + new Vector2(width, height), Ink(c.SurfaceContainerHigh), 2 * s);
            if (accent != 0)
                dl.AddQuadFilled(origin, origin + new Vector2(lead + tabStart, 0),
                    origin + new Vector2(tabStart, height), origin + new Vector2(0, height), Ink(c.Secondary));
            dl.AddQuadFilled(origin + new Vector2(lead + tabStart + gap, 0), origin + new Vector2(tabWidth - cut, 0),
                origin + new Vector2(tabWidth, height), origin + new Vector2(tabStart + gap, height), Ink(tab));
            if (accent == 0 && width - tabWidth >= 104 * s)
            {
                for (var index = 0; index < 4; index++)
                {
                    var slash = origin + new Vector2(width - (62 - index * 14) * s, height * .34f);
                    dl.AddQuadFilled(slash + new Vector2(6 * s, 0), slash + new Vector2(14 * s, 0),
                        slash + new Vector2(8 * s, height * .42f), slash + new Vector2(0, height * .42f), Ink(c.Primary));
                }
            }
            MaterialText.AddText(dl, origin + new Vector2(left, (height - size.Y) * .5f), Ink(ReadableInk(tab)), title, wrapWidth: textWidth);
        }
        finally { dl.PopClipRect(); }
        ImGui.Dummy(new Vector2(width, height));
    }

    internal static void LcarsFooter(string title)
    {
        if (!IsLcars) return;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = MaterialText.Measure(title);
        var height = Math.Max(12 * SkinScale, size.Y);
        var right = min + new Vector2(width, height);
        var textWidth = Math.Min(size.X, width * .46f);
        var dl = ImGui.GetWindowDrawList();
        SegmentedRail(dl, min + new Vector2(0, (height - 9 * SkinScale) * .5f), new Vector2(Math.Max(min.X, right.X - textWidth - 9 * SkinScale), min.Y + (height + 9 * SkinScale) * .5f), 1);
        dl.PushClipRect(new Vector2(right.X - textWidth, min.Y), right, true);
        try { MaterialText.AddText(dl, new Vector2(right.X - textWidth, min.Y), Ink(LcarsText), title); }
        finally { dl.PopClipRect(); }
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void SegmentedRail(ImDrawListPtr dl, Vector2 min, Vector2 max, int first)
    {
        if (max.X <= min.X || max.Y <= min.Y) return;
        var gap = Math.Min(4 * SkinScale, (max.X - min.X) / 12);
        var available = max.X - min.X - 3 * gap;
        var x = min.X;
        for (var index = 0; index < 4; index++)
        {
            var segment = available * (index == 1 ? .4f : .2f);
            dl.AddRectFilled(new Vector2(x, min.Y), new Vector2(x + segment, max.Y), Ink(LcarsBar((first + index) % 3)), 2 * SkinScale);
            x += segment + gap;
        }
    }

    internal static MaterialStyleScope NativeButtonStyle(Vector4? accent = null)
    {
        var scope = new MaterialStyleScope();
        if (!Decorated) return scope;
        var c = MaterialTheme.Current.Colors;
        scope.Style(ImGuiStyleVar.FrameRounding, IsLcars ? 7 * SkinScale : Style == FrenRiderUiStyle.Cyberpunk ? 3 * SkinScale : 6 * SkinScale);
        scope.Style(ImGuiStyleVar.FrameBorderSize, 0);
        scope.Color(ImGuiCol.Border, accent ?? (IsLcars ? LcarsBar(1) : c.Outline));
        if (IsLcars) scope.Color(ImGuiCol.Text, ReadableInk(ImGui.GetStyle().Colors[(int)ImGuiCol.Button]));
        // Caller-owned semantic Button colours remain intact and are used by PaintButton.
        return scope;
    }

    internal static MaterialStyleScope TransparentButtonFace()
    {
        var scope = new MaterialStyleScope();
        if (!Decorated) return scope;
        scope.Color(ImGuiCol.Button, Vector4.Zero);
        scope.Color(ImGuiCol.ButtonHovered, Vector4.Zero);
        scope.Color(ImGuiCol.ButtonActive, Vector4.Zero);
        scope.Color(ImGuiCol.Border, Vector4.Zero);
        return scope;
    }

    internal static void PaintButton(Vector2 min, Vector2 max)
    {
        if (!Decorated) return;
        var dl = ImGui.GetWindowDrawList();
        var c = MaterialTheme.Current.Colors;
        var s = SkinScale;
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var fill = ImGui.GetStyle().Colors[(int)(active ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button)];
        var border = ImGui.GetStyle().Colors[(int)ImGuiCol.Border];
        if (IsLcars)
        {
            dl.AddRectFilled(min, max, Ink(fill), 7 * s);
            dl.AddRect(min, max, Ink(border, hovered || active ? 1 : .7f), 7 * s, ImDrawFlags.None, (active ? 2 : 1) * s);
        }
        else if (Style == FrenRiderUiStyle.Glassmorphism)
        {
            GlassGlow(dl, min, max, 6 * s, active ? 1 : hovered ? .9f : .7f);
            dl.AddRectFilled(min, max, Ink(fill, .68f), 6 * s);
            dl.AddRect(min, max, Ink(Vector4.Lerp(c.Primary, c.OnSurface, .35f), active ? .95f : hovered ? .85f : .65f), 6 * s);
            dl.AddLine(min + new Vector2(6, 1) * s, new Vector2(max.X - 6 * s, min.Y + s), Ink(c.OnSurface, .25f), s);
        }
        else if (Style == FrenRiderUiStyle.Spatial)
        {
            if (!active) ContactShadow(dl, min, max - new Vector2(0, 2 * s), 6 * s);
            dl.AddRectFilled(min, max, Ink(fill), 6 * s);
            dl.AddRect(min, max, Ink(c.Outline, hovered ? .6f : .3f), 6 * s);
            dl.AddLine(min + new Vector2(6, 1) * s, new Vector2(max.X - 6 * s, min.Y + s), Ink(active ? c.Outline : Vector4.One, active ? .18f : .85f), s);
        }
        else if (Style == FrenRiderUiStyle.Cyberpunk)
        {
            if (!active)
                dl.AddRectFilled(min + new Vector2(0, 2 * s), max + new Vector2(0, 2 * s), Ink(new Vector4(0, 0, 0, .24f)), 3 * s);
            dl.AddRectFilled(min, max, Ink(fill), 3 * s);
            dl.AddRect(min, max, Ink(c.OnSurface, active ? .32f : hovered ? .26f : .16f), 3 * s);
            dl.PathClear();
            dl.PathLineTo(new Vector2(min.X + s, max.Y - 4 * s));
            dl.PathLineTo(new Vector2(min.X + 4 * s, max.Y - s));
            dl.PathLineTo(max - new Vector2(4 * s, s));
            dl.PathLineTo(max - new Vector2(s, 4 * s));
            dl.PathStroke(Ink(c.Primary), ImDrawFlags.None, 2 * s);
        }
        if (ImGui.IsItemFocused() && ImGui.GetIO().NavVisible)
        {
            if (Style == FrenRiderUiStyle.Cyberpunk && !IsLcars)
                dl.AddRect(min + new Vector2(2 * s), max - new Vector2(2 * s), Ink(c.Primary), 2 * s, ImDrawFlags.None, 2 * s);
            else dl.AddRect(min + new Vector2(s), max - new Vector2(s), Ink(IsLcars ? LcarsBar(2) : c.Primary), 5 * s, ImDrawFlags.None, 2 * s);
        }
    }

    internal static MaterialStyleScope NativeFieldStyle()
    {
        var scope = new MaterialStyleScope();
        if (!Decorated || IsLcars) return scope;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight());
        DrawPanel(min, max, inset: true, background: false);
        scope.Color(ImGuiCol.FrameBg, Vector4.Zero);
        scope.Color(ImGuiCol.FrameBgHovered, Alpha(MaterialTheme.Current.Colors.OnSurface, .06f));
        scope.Color(ImGuiCol.FrameBgActive, Alpha(MaterialTheme.Current.Colors.OnSurface, .10f));
        scope.Style(ImGuiStyleVar.FrameRounding, Style == FrenRiderUiStyle.Cyberpunk ? 0 : 5 * SkinScale);
        return scope;
    }

    internal static Vector4 ButtonForeground()
    {
        var style = ImGui.GetStyle();
        if (!Decorated) return style.Colors[(int)ImGuiCol.Text];
        return ReadableInk(style.Colors[(int)(ImGui.IsItemActive() ? ImGuiCol.ButtonActive : ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Button)]);
    }

    internal static void PaintToggle(Vector2 min, Vector2 size, bool value, Vector4 fill)
    {
        var dl = ImGui.GetWindowDrawList();
        var c = MaterialTheme.Current.Colors;
        var s = SkinScale;
        var max = min + size;
        var radius = size.Y * .5f;
        var thumb = min + new Vector2(value ? size.X - radius : radius, radius);
        if (Style == FrenRiderUiStyle.Cyberpunk && !IsLcars)
        {
            dl.AddRectFilled(min + new Vector2(0, 2 * s), max + new Vector2(0, 2 * s), Ink(new Vector4(0, 0, 0, .20f)), radius);
            dl.AddRectFilled(min, max, Ink(fill), radius);
            dl.AddRect(min, max, Ink(value ? c.Primary : c.Outline, .6f), radius);
            dl.AddCircleFilled(thumb + new Vector2(0, s), Math.Max(s, radius - 2 * s), Ink(new Vector4(0, 0, 0, .22f)), 32);
            dl.AddCircleFilled(thumb, Math.Max(s, radius - 3 * s), Ink(c.OnSurface), 32);
            return;
        }
        if (Style == FrenRiderUiStyle.Spatial && !IsLcars)
            ContactShadow(dl, min, max, radius);
        dl.AddRectFilled(min, max, Ink(fill), radius);
        dl.AddRect(min, max, Ink(IsLcars ? LcarsBar(2) : c.Outline, .5f), radius);
        var thumbInk = IsLcars ? ReadableInk(fill) : Style == FrenRiderUiStyle.Spatial ? Vector4.One : c.OnSurface;
        dl.AddCircleFilled(thumb + new Vector2(0, s), Math.Max(s, radius - 2 * s), Ink(new Vector4(0, 0, 0, .16f)), 32);
        dl.AddCircleFilled(thumb, Math.Max(s, radius - 3 * s), Ink(thumbInk), 32);
        if (Style == FrenRiderUiStyle.Glassmorphism && !IsLcars)
            dl.AddLine(min + new Vector2(radius, s), max - new Vector2(radius, size.Y - s), Ink(c.OnSurface, .3f), s);
    }

    internal static void PaintChoice(Vector2 min, float size, bool selected, bool radio)
    {
        if (!Decorated) return;
        var c = MaterialTheme.Current.Colors;
        var dl = ImGui.GetWindowDrawList();
        var s = SkinScale;
        var max = min + new Vector2(size);
        var accent = IsLcars ? LcarsBar(0) : c.Primary;
        var fill = selected ? accent : IsLcars ? LcarsBackground : c.SurfaceContainer;
        if (ImGui.IsItemHovered()) fill = MaterialColor.Layer(fill, IsLcars ? LcarsText : c.OnSurface, .12f);
        if (ImGui.IsItemActive()) fill = MaterialColor.Layer(fill, IsLcars ? LcarsText : c.OnSurface, .16f);
        if (Style == FrenRiderUiStyle.Spatial && !IsLcars)
            ContactShadow(dl, min, max, radio ? size * .5f : 4 * s);
        if (radio)
        {
            var center = (min + max) * .5f;
            dl.AddCircleFilled(center, size * .43f, Ink(IsLcars ? LcarsBackground : c.SurfaceContainerLow), 32);
            dl.AddCircle(center, size * .4f, Ink(selected ? accent : c.Outline), 32, 1.5f * s);
            if (selected) dl.AddCircleFilled(center, size * .25f, Ink(accent), 24);
        }
        else if (Style == FrenRiderUiStyle.Cyberpunk && !IsLcars)
        {
            Chamfer(dl, min, max, 3 * s, fill, true);
            Chamfer(dl, min, max, 3 * s, selected ? accent : c.Outline, false, s);
        }
        else
        {
            dl.AddRectFilled(min, max, Ink(fill), 4 * s);
            dl.AddRect(min, max, Ink(selected ? accent : c.Outline), 4 * s);
        }
        if (!radio && selected)
        {
            var ink = ReadableInk(accent);
            dl.AddLine(min + new Vector2(.23f, .52f) * size, min + new Vector2(.43f, .72f) * size, Ink(ink), 2 * s);
            dl.AddLine(min + new Vector2(.43f, .72f) * size, min + new Vector2(.78f, .28f) * size, Ink(ink), 2 * s);
        }
        if (ImGui.IsItemFocused() && ImGui.GetIO().NavVisible)
            dl.AddRect(min - new Vector2(2 * s), max + new Vector2(2 * s), Ink(accent), 4 * s, ImDrawFlags.None, 2 * s);
    }
}
