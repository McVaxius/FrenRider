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
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal FrenRiderFonts(IFontAtlas atlas, ushort[] ranges, string language, string assetRoot)
    {
        handles = FrenRiderPresentation.FontSizes.Select((_, index) => MaterialFontAtlas.CreateFont(atlas,
            FrenRiderPresentation.FontWeights[index], FrenRiderPresentation.AtlasHeight((UiFontRole)index),
            ranges, language, assetRoot)).ToArray();
        foreach(var handle in handles) handle.ImFontChanged+=FontChanged;
    }
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        var characters = strings.SelectMany(text => MaterialText.NativeGlyphText(text)).Where(c => !char.IsControl(c)).Distinct().ToArray();
        for (var role = 0; role < handles.Length; role++)
        {
            var handle = handles[role];
            using var font=handle.Lock();
            foreach(var character in characters)
                if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                    throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4")+" in "+(UiFontRole)role);
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=handles[(int)role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("FrenRider fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose() { foreach(var handle in handles) { handle.ImFontChanged-=FontChanged;handle.Dispose(); } }
}
