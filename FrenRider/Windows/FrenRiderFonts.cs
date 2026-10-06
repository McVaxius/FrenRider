using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;

namespace FrenRider.Windows;

internal sealed class FrenRiderFonts : IDisposable
{
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal FrenRiderFonts(IFontAtlas atlas, ushort[] ranges,string language)
    {
        handles=FrenRiderPresentation.FontSizes.Select((size,index)=>atlas.NewDelegateFontHandle(toolkit=>toolkit.OnPreBuild(build=>
        {
            size=FrenRiderPresentation.AtlasHeight((UiFontRole)index);
            var config=new SafeFontConfig { SizePx=size, GlyphRanges=ranges };
            build.Font=build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),FrenRiderPresentation.FontFiles[index]),config);
            build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisym.ttf"),
                new SafeFontConfig { SizePx = size, MergeFont = build.Font, GlyphRanges = ranges });
            // The language selector always displays all native language names. Host-managed merges cover these too.
            foreach(var locale in UiText.CjkLanguages(language))
                build.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular,new SafeFontConfig
                {
                    SizePx=size, MergeFont=build.Font, GlyphRanges=ranges,
                    // Verified bundled TTC order: Japanese, Korean,
                    // Simplified Chinese, Traditional Chinese. Keep the selected locale first.
                    FontNo=locale switch { "ja"=>0, "zh-Hans"=>2, "ko"=>1, _=>0 },
                });
            build.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx=size, MergeFont=build.Font });
            build.AddGameSymbol(new SafeFontConfig { SizePx=size,MergeFont=build.Font });
        }))).ToArray();
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
