using System;
using System.Collections.Generic;
using System.Linq;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;

namespace FrenRider.Windows;

internal sealed class FrenRiderFonts : IDisposable
{
    private readonly IFontAtlas atlas;
    private readonly Dictionary<UiFontRole, IFontHandle> roles = [];
    private readonly List<IFontHandle> handles = [];
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal FrenRiderFonts(IFontAtlas atlas, ushort[] ranges, string language, string assetRoot,
        IEnumerable<UiFontRole> selectedRoles)
    {
        this.atlas = atlas;
        atlas.BuildStepChange += MaterialFontAtlas.ReleaseRasterizedInputs;
        try
        {
            var glyphCount = 0;
            for (var index = 0; index + 1 < ranges.Length; index += 2)
                glyphCount += ranges[index + 1] - ranges[index] + 1;
            var pageSize = glyphCount > 1000 ? 2048 : 1024;
            var faces = new Dictionary<(MaterialFontWeight Weight, float Height), IFontHandle>();
            foreach (var role in selectedRoles)
            {
                var weight = FrenRiderPresentation.FontWeights[(int)role];
                var height = FrenRiderPresentation.AtlasHeight(role);
                if (!faces.TryGetValue((weight, height), out var handle))
                {
                    handle = MaterialFontAtlas.CreateFont(atlas, weight, height, ranges, language, assetRoot, pageSize, includeHostLanguage: true);
                    faces.Add((weight, height), handle);
                    handles.Add(handle);
                    handle.ImFontChanged += FontChanged;
                }
                roles.Add(role, handle);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IEnumerable<UiFontRole> Roles => roles.Keys;

    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        var characters = strings.SelectMany(text => MaterialText.NativeGlyphText(text)).Where(c => !char.IsControl(c)).Distinct().ToArray();
        foreach (var (role, handle) in roles)
        {
            using var font=handle.Lock();
            foreach(var character in characters)
                if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                    throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4")+" in "+role);
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=roles.GetValueOrDefault(role, roles[UiFontRole.Body]);
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("FrenRider fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose()
    {
        atlas.BuildStepChange -= MaterialFontAtlas.ReleaseRasterizedInputs;
        foreach (var handle in handles)
        {
            handle.ImFontChanged -= FontChanged;
            handle.Dispose();
        }
        atlas.Dispose();
    }
}
