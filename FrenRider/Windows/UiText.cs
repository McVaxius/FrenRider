using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using AethertekUI;

namespace FrenRider.Windows;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the FrenRider UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    internal ResourceSet Resources { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    internal string[] RequiredText { get; }
    private readonly (Regex Pattern, string Key, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("FrenRider.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        this.pushFont=pushFont;
        var englishManager = new ResourceManager("FrenRider.Localization.Strings_en", typeof(UiText).Assembly);
        var english = englishManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)
            ?? throw new MissingManifestResourceException("en");
        RequiredText = Values(Resources).Concat(Values(english)).Concat(Languages.Where(l => l.Code != "hi").Select(l => l.Name))
            .Append("⚠•—").Distinct().ToArray();
        englishManager.ReleaseAllResources();
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = Resources.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => parameter.IsMatch(key) && parameter.Replace(key, "").Any(char.IsLetter))
            .OrderBy(key => !key.StartsWith('{') ? 0 : CapturePattern(key, 0) != ".*?" ? 1 : 2)
            .ThenByDescending(key => parameter.Replace(key, "").Length).Select(key =>
            {
                var patternKey = key;
                var pattern = "\\A";
                var offset = 0;
                var holes = parameter.Matches(patternKey);
                foreach (Match hole in holes)
                {
                    var index = int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture);
                    pattern += Regex.Escape(patternKey[offset..hole.Index]) + $"(?<arg{index}>{CapturePattern(key, index)})";
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(patternKey[offset..]) + "\\z";
                return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key,
                    holes.Cast<Match>().Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1);
            }).ToArray();
    }
    internal static string T(string english)
    {
        if (Current.Resources.GetString(english, true) is { } exact) return exact;
        if (TryActionOutcomes(english) is { } outcomes) return outcomes;
        foreach (var template in Current.messageTemplates)
        {
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            // Typed UI values are formatted by F; service captures retain their representation.
            var args = Enumerable.Range(0, template.ArgumentCount)
                .Select(index => (object)Argument(template.Key, index, match.Groups[$"arg{index}"].Value)).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(template.Key, false)!, args);
        }
        return english; // Names, game data and raw diagnostic values are consumer data.
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args);
    internal static string F(FormattableString text) => string.Format(Current.Culture, T(text.Format),
        text.GetArguments().Select(value => value is bool flag ? T(flag ? "Yes" : "No")
            : value is Enum state ? T(state.ToString())
            : text.Format == "Shown when Fren Rider is {0}" && value is string label ? T(label)
            : value).ToArray());
    internal static string StateDetail(string state, string detail)
        => T(state) + (string.IsNullOrWhiteSpace(detail) ? "" : " - " + T(detail));
    internal static string TeleportStatus(string english)
    {
        const string suffix = "; Party window left open";
        if (english.EndsWith(suffix, StringComparison.Ordinal))
        {
            var status = english[..^suffix.Length];
            if (status is "Not logged in" or "FrenRider disabled" or "Off" or "No fren configured"
                or "Zone changed" or "Fren not found" or "Fren not in party" or "Fren visible"
                or "Blocked: area transition" or "Blocked: in combat" or "Blocked: unconscious"
                or "Blocked: in duty" or "Blocked: ADS handoff pending" or "Blocked: ADS active"
                or "Blocked: ADS utility active") return T(status) + T(suffix);
        }
        return T(english);
    }

    // Only source-authored slots recurse. Item/duty/mount names, presets, commands,
    // IPC utility details and exceptions retain their exact service representation.
    private static string Argument(string key, int index, string value)
    {
        if ((key == "Repair timed out after {0}s; ADS: {1}" && index == 1
            || key == "{0} repair running; FrenRider paused. ADS: {1}" && index == 1
            || key == "{0} repair still needed below {1}%; retry in {2}s. ADS: {3}" && index == 3
            || key == "{0} repair still needed below {1}%; waiting while {2}. ADS: {3}" && index == 3
            || key == "ADS did not accept {0} repair: {1}" && index == 1
            || key == "Retry wait after ADS failure: {0}" || key == "Retry wait after rejected start: {0}")
            && value is "No ADS utility status." or "ADS repair running" or "ADS not loaded."
                or "ADS did not accept the utility request." or "ADS status was not readable.") return T(value);
        if (key is "Captured automation snapshot ({0})." or "Partial restore ({0}).")
            return SnapshotSummary(value);
        if (key == "ADS unavailable/pending: {0}.") return ReflectionFailures(value);
        if (key is "ADS loaded; {0}." or "ADS solo combat held; {0}.") return AdsReason(value);
        if (key is "ADS reflection: {0}" or "ADS Status: {0}" or "Combat held: {0}"
            or "{0}; FrenRider local duty logic stays active"
            or "{0}; restarting readiness delay with 5s retry backoff"
            or "{0}; waiting for authoritative ownership"
            or "Far chase blocked: {0}" or "Far chase preempted: {0}"
            or "Own mount retained: {0}" or "Mount state preserved: {0}"
            or "Blocked: {0}" or "Waiting: {0}." or "Reading party window ({0})") return T(value);
        if (key == "{0} {1}: M{2}/T{3}, {4}." && index == 0) return T(value);
        if (key == "{0} {1}: M{2}/T{3}, {4}." && index == 4) return AdsReason(value);
        if (key == "{0} handoff is off; FrenRider local duty logic stays active" && index == 0) return T(value);
        if (key.StartsWith("ADS ", StringComparison.Ordinal) && key.Contains(" via {0}", StringComparison.Ordinal) && index == 0) return T(value);
        if (key.StartsWith("{0} repair ", StringComparison.Ordinal) && (index == 0 || index == 2 && key.Contains("waiting while {2}", StringComparison.Ordinal))) return T(value);
        if (key == "ADS did not accept {0} repair: {1}" && index == 0) return T(value);
        if (key == "{0}: mounting {1}" && index == 0) return T(value);
        if (key == "RSR {0}{1}" && index == 0) return T(value);
        if (key == "Leaving duty (attempt #{0}) - {1}" && index == 1) return T(value);
        if (key == "live instance discovery failed: {0}" && value is "Dalamud LocalPlugin wrapper was not reachable from IExposedPlugin"
            or "Dalamud LocalPlugin had no live instance" or "Dalamud LocalPlugin had no live assembly"
            or "Dalamud LocalPlugin instance and assembly did not match") return T(value);
        if (key == "Waiting for {0} SelectYesno dialog") return T(value);
        if (key is "Run={0}, FlyYouFools={1}, FarChase={2}, FollowMode={3}/{4}" && index < 3
            || key is "Loaded={0}, Pending={1}, RuntimeOwned={2}, Readable={3}, Source={4}, ExitTakeover={5}"
            || key is "Source={0}, Readable={1}, ExitTakeover={2}") return T(value);
        if (key == "{0}{1}; retry in {2}s" && index == 0) return T(value);
        if (key == "{0}{1}; retry in {2}s" && index == 1) return T(value);
        if (key == "Following local aetheryte network to {0}{1}" && index == 1) return T(value);
        if (key == "Sent {0}; matched {1}; found {2} aetherytes{3}" && index == 3) return T(value);
        return value;
    }

    private static string AdsReason(string value)
    {
        const string suffix = "; FrenRider local duty logic stays active";
        if (value.EndsWith(suffix, StringComparison.Ordinal))
        {
            var detail = value[..^suffix.Length];
            // ResolveReadiness adds this suffix to CurrentDutyDetail, including
            // JSON exception envelopes whose own captured text must stay raw.
            if (detail.StartsWith("ADS.GetStatusJson ", StringComparison.Ordinal)
                || detail.StartsWith("ADS current-duty catalog row ", StringComparison.Ordinal)
                || detail.StartsWith("Live duty identity changed from territory/CFC ", StringComparison.Ordinal)
                || detail.StartsWith("ADS current-duty identity ", StringComparison.Ordinal) && detail.EndsWith('.')
                || Current.Resources.GetString(detail, true) is not null)
                return F("{0}; FrenRider local duty logic stays active", T(detail));
        }
        return T(value);
    }

    private static string CapturePattern(string key, int index)
    {
        if (key == "{0} {1}: M{2}/T{3}, {4}." && index == 0
            || key == "{0} handoff is off; FrenRider local duty logic stays active" && index == 0)
            return "Solo|4-Man|8-Man|Alliance|Guild Hest|Deep Dungeon|Treasure Dungeon|Other";
        if (key.StartsWith("{0} repair ", StringComparison.Ordinal) && index == 0)
            return "NPC no-inn/no-teleport|NPC no-inn|NPC repair \\+ inn room|Self|ADS";
        if (key == "{0}: mounting {1}" && index == 0) return "Far chase|Fly You Fools";
        if (key == "RSR {0}{1}") return index == 0 ? "None|Manual|Support|Auto" : "(?: \\[.*\\])?";
        if (key is "{0} active" or "{0} auto" or "{0} active{1}" or "{0} auto{1}" or "{0} unavailable{1}")
            return index == 0 ? "BMR|VBM|WRATH|DAEDALUS" : "(?: \\[.*\\])?";
        if (key is "{0} captured" or "{0} unavailable" or "{0} unavailable: {1}" && index == 0)
            return "BMR|VBM|CBT|Daedalus";
        if (key == "{0}{1}; retry in {2}s" && index == 1
            || key == "Following local aetheryte network to {0}{1}" && index == 1
            || key == "Sent {0}; matched {1}; found {2} aetherytes{3}" && index == 3)
            return "(?:; Party window left open)?";
        if (key == "Waiting for {0} SelectYesno dialog") return "Unknown|Teleport|Raise|DeathReturn|Party|Misc";
        return ".*?";
    }

    private static string? TryActionOutcomes(string value)
    {
        var pieces = value.Split("; ", StringSplitOptions.None);
        var labels = new[] { "BMR", "VBM", "RSR" };
        if (pieces.Length != labels.Length) return null;
        for (var index = 0; index < labels.Length; index++)
        {
            var prefix = labels[index] + ": ";
            if (!pieces[index].StartsWith(prefix, StringComparison.Ordinal)) return null;
            var outcome = pieces[index][prefix.Length..];
            if (outcome is not ("applied" or "already set" or "not loaded" or "unavailable" or "failed")) return null;
            pieces[index] = prefix + T(outcome);
        }
        return string.Join("; ", pieces);
    }

    private static string SnapshotSummary(string value)
    {
        var pieces = Regex.Split(value, @"; (?=VBM (?:captured|unavailable)|CBT (?:captured|unavailable)|Daedalus (?:captured|unavailable))");
        var labels = new[] { "BMR", "VBM", "CBT", "Daedalus" };
        if (pieces.Length is not (3 or 4)) return value;
        for (var index = 0; index < pieces.Length; index++)
        {
            var label = labels[index];
            if (pieces[index] == label + " captured") pieces[index] = F("{0} captured", label);
            else if (pieces[index] == label + " unavailable") pieces[index] = F("{0} unavailable", label);
            else if (pieces[index].StartsWith(label + " unavailable: ", StringComparison.Ordinal))
                pieces[index] = F("{0} unavailable: {1}", label, SnapshotDetail(pieces[index][(label.Length + 14)..]));
            else return value;
        }
        return string.Join("; ", pieces);
    }

    private static string SnapshotDetail(string value)
        => value is "typed IPC unavailable" or "live AutoFollow read unavailable" or "not loaded"
            or "BossMod.Service not found" or "BossMod.Service.Config unavailable" or "BossMod.AI.AIConfig not found"
            || value.StartsWith("live instance discovery failed: ", StringComparison.Ordinal) ? T(value) : value;

    private static string ReflectionFailures(string value)
    {
        var pieces = Regex.Split(value, @", (?=(?:range|range reset|hunts|hunts reset|queen|queen reset) \()");
        for (var index = 0; index < pieces.Length; index++)
        {
            var separator = pieces[index].IndexOf(" (", StringComparison.Ordinal);
            if (separator < 0 || !pieces[index].EndsWith(')')) return value;
            var label = pieces[index][..separator];
            if (label is not ("range" or "range reset" or "hunts" or "hunts reset" or "queen" or "queen reset")) return value;
            var detail = pieces[index][(separator + 2)..^1];
            pieces[index] = T(label) + " (" + (detail == "ADS returned false" ? T(detail) : detail) + ")";
        }
        return string.Join(", ", pieces);
    }
    internal string Format(string key, params object?[] args) => string.Format(Culture, Resources.GetString(key, false) ?? key, args);
    internal string Label(string key) => Resources.GetString(key, false) ?? key;
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(t=>MaterialText.NativeGlyphText(t)).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() => manager.ReleaseAllResources();
}
