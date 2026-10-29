using System;
using System.Collections.Generic;
using System.Threading;
using TerraFX.Interop.Windows;
using Win32 = TerraFX.Interop.Windows.Windows;

namespace FrenRider.Windows;

/// <summary>Temporarily excludes only the supplied owned detached windows from OS captures.</summary>
internal sealed class FrenRiderCaptureExclusion : IDisposable
{
    internal const uint ExcludeFromCapture = 0x11;
    private const string OwnershipProperty = "FrenRider.CaptureExclusion.Owner";
    private static long nextCookie;
    private readonly nint cookie = (nint)Interlocked.Increment(ref nextCookie);
    private readonly Native native;
    private readonly Dictionary<nint, Entry> entries = [];
    private readonly HashSet<nint> pendingBoundaryWindows = [];
    private nint gameWindow;
    private bool boundaryPending;
    private bool disposed;

    internal bool Ready { get; private set; }
    internal long MinimumFrameTimestamp { get; private set; }
    // Published with a new freshness boundary; stable/failed synchronizations expose no publication.
    internal IReadOnlyCollection<nint> BoundaryChangedWindows { get; private set; } = Array.Empty<nint>();
    internal string? UnavailableReason { get; private set; }

    // The caller stops capture consumption before restoring exclusions or disposing this helper.
    internal FrenRiderCaptureExclusion() : this(CreateNative()) { }

    internal FrenRiderCaptureExclusion(Native native)
        => this.native = native ?? throw new ArgumentNullException(nameof(native));

    /// <summary>
    /// Supply only FrenRider's owned detached HWNDs, after their draw and before capture callbacks.
    /// Capture accepts only desktop image updates strictly newer than MinimumFrameTimestamp.
    /// Exclusion also hides these windows from other OS screen-capture and recording applications.
    /// </summary>
    internal bool Synchronize(IReadOnlyCollection<nint> ownedDetachedWindows, nint gameWindow, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(ownedDetachedWindows);
        Ready = false;
        UnavailableReason = null;
        BoundaryChangedWindows = Array.Empty<nint>();
        if (disposed) return Fail("Capture exclusion is disposed.");
        if (!enabled) return RestoreAll();
        if (!native.Supported) return Fail("Desktop capture exclusion requires Windows 10 version 2004 or later.");
        if (gameWindow == 0) return Fail("The game window is unavailable; capture exclusion is not ready.");
        this.gameWindow = gameWindow;

        var desired = new HashSet<nint>(ownedDetachedWindows);
        var complete = true;
        foreach (var window in new List<nint>(entries.Keys))
            if (!desired.Contains(window)) complete &= Restore(window);
        foreach (var window in desired)
            if (!CanModify(window))
                return Fail("Capture exclusion requires owned current-process top-level detached windows.");
        foreach (var window in desired)
            complete &= EnsureExcluded(window);
        if (!complete) return false;
        if (desired.Count == 0) return Fail("No owned detached windows require desktop capture.");

        if (boundaryPending || MinimumFrameTimestamp == 0)
        {
            var timestamp = native.GetTimestamp();
            if (timestamp <= 0) return Fail("The desktop capture timestamp is unavailable.");
            MinimumFrameTimestamp = Math.Max(MinimumFrameTimestamp, timestamp);
            BoundaryChangedWindows = new List<nint>(pendingBoundaryWindows);
            pendingBoundaryWindows.Clear();
            boundaryPending = false;
        }
        Ready = true;
        return true;
    }

    /// <summary>Call after capture consumption stops. Failed restorations remain available for explicit retry.</summary>
    internal bool RestoreAll()
    {
        Ready = false;
        UnavailableReason = null;
        BoundaryChangedWindows = Array.Empty<nint>();
        var restored = true;
        foreach (var window in new List<nint>(entries.Keys)) restored &= Restore(window);
        return restored;
    }

    private bool EnsureExcluded(nint window)
    {
        if (entries.TryGetValue(window, out var existing))
        {
            if (existing.Yielded) return Fail("Another owner changed a detached window's capture affinity.");
            if (native.GetMarker(window) != cookie)
            {
                existing.Yielded = true;
                MarkBoundary(window);
                return Fail("A detached window's capture-exclusion ownership changed.");
            }
            var current = native.GetAffinity(window);
            if (!current.Success) return Fail("A detached window's capture affinity could not be verified.");
            if (current.Affinity == ExcludeFromCapture) return true;
            existing.Yielded = true;
            MarkBoundary(window);
            RemoveOwnedMarker(window);
            return Fail("Another owner changed a detached window's capture affinity.");
        }

        // Reading first is mandatory: an unreadable original must never be replaced with an assumed NONE.
        var original = native.GetAffinity(window);
        if (!original.Success) return Fail("A detached window's original capture affinity is unreadable.");
        if (native.GetMarker(window) != 0)
            return Fail("A detached window already has a capture-exclusion owner.");
        var entry = new Entry(original.Affinity);
        entries.Add(window, entry);
        if (!native.SetMarker(window, cookie) || native.GetMarker(window) != cookie)
        {
            Fail("A detached window's capture-exclusion ownership could not be established.");
            _ = Restore(window);
            return false;
        }

        var beforeWrite = native.GetAffinity(window);
        if (!beforeWrite.Success)
            return Fail("A detached window's original capture affinity could not be verified before exclusion.");
        if (beforeWrite.Affinity != original.Affinity || !CanModify(window) || native.GetMarker(window) != cookie)
        {
            entry.Yielded = true;
            MarkBoundary(window);
            RemoveOwnedMarker(window);
            return Fail("A detached window's capture-exclusion state changed before exclusion.");
        }
        if (original.Affinity != ExcludeFromCapture)
        {
            if (!native.SetAffinity(window, ExcludeFromCapture))
            {
                Fail("Windows rejected capture exclusion for a detached window.");
                _ = Restore(window);
                return false;
            }
            entry.Applied = true;
            MarkBoundary(window);
        }
        var applied = native.GetAffinity(window);
        if (!applied.Success) return Fail("A detached window's capture exclusion could not be verified.");
        if (applied.Affinity != ExcludeFromCapture)
        {
            entry.Yielded = true;
            MarkBoundary(window);
            RemoveOwnedMarker(window);
            return Fail("Windows did not retain the requested detached-window capture exclusion.");
        }
        MarkBoundary(window);
        return true;
    }

    private bool Restore(nint window)
    {
        var entry = entries[window];
        // HWND destruction removes its properties. A missing/different cookie means no right to mutate it.
        if (!CanModify(window) || native.GetMarker(window) != cookie)
        {
            entries.Remove(window);
            MarkBoundary(window);
            return true;
        }
        if (entry.Applied && !entry.Yielded)
        {
            var current = native.GetAffinity(window);
            if (!current.Success) return Fail("A detached window's capture affinity could not be read for restoration.");
            if (current.Affinity == ExcludeFromCapture && entry.OriginalAffinity != ExcludeFromCapture)
            {
                if (!native.SetAffinity(window, entry.OriginalAffinity))
                    return Fail("Windows rejected restoration of a detached window's original capture affinity.");
                MarkBoundary(window);
                var restored = native.GetAffinity(window);
                if (!restored.Success)
                    return Fail("A detached window's original capture affinity could not be verified after restoration.");
                if (restored.Affinity != entry.OriginalAffinity)
                {
                    entry.Yielded = true;
                    RemoveOwnedMarker(window);
                    entries.Remove(window);
                    return Fail("A detached window's capture affinity changed during restoration.");
                }
            }
            // A later external affinity is retained, rather than overwritten with our saved value.
        }
        if (!RemoveOwnedMarker(window))
            return Fail("A detached window's capture-exclusion ownership marker could not be removed.");
        entries.Remove(window);
        MarkBoundary(window);
        return true;
    }

    private void MarkBoundary(nint window)
    {
        boundaryPending = true;
        pendingBoundaryWindows.Add(window);
    }

    private bool CanModify(nint window)
        => window != 0 && window != gameWindow && native.IsWindow(window)
            && native.IsCurrentProcessTopLevelWindow(window);

    private bool RemoveOwnedMarker(nint window)
    {
        if (!CanModify(window) || native.GetMarker(window) != cookie) return true;
        _ = native.RemoveMarker(window);
        return native.GetMarker(window) != cookie;
    }

    private bool Fail(string reason)
    {
        UnavailableReason ??= reason;
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        _ = RestoreAll();
        foreach (var window in entries.Keys)
            if (!RemoveOwnedMarker(window)) Fail("A detached window's capture-exclusion ownership marker could not be removed.");
        entries.Clear();
        pendingBoundaryWindows.Clear();
        BoundaryChangedWindows = Array.Empty<nint>();
        Ready = false;
        disposed = true;
    }

    private sealed class Entry(uint originalAffinity)
    {
        internal uint OriginalAffinity { get; } = originalAffinity;
        internal bool Applied { get; set; }
        internal bool Yielded { get; set; }
    }

    internal sealed record Native(
        bool Supported,
        Func<nint, bool> IsWindow,
        Func<nint, bool> IsCurrentProcessTopLevelWindow,
        Func<nint, (bool Success, uint Affinity)> GetAffinity,
        Func<nint, uint, bool> SetAffinity,
        Func<nint, nint> GetMarker,
        Func<nint, nint, bool> SetMarker,
        Func<nint, nint> RemoveMarker,
        Func<long> GetTimestamp);

    private static unsafe Native CreateNative() => new(
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
        window => Win32.IsWindow((HWND)window).Value != 0,
        window =>
        {
            uint process = 0;
            return Win32.GetWindowThreadProcessId((HWND)window, &process) != 0
                && process == (uint)Environment.ProcessId && (nint)Win32.GetAncestor((HWND)window, 2) == window;
        },
        window =>
        {
            uint affinity = 0;
            var success = Win32.GetWindowDisplayAffinity((HWND)window, &affinity).Value != 0;
            return (success, affinity);
        },
        (window, affinity) => Win32.SetWindowDisplayAffinity((HWND)window, affinity).Value != 0,
        window =>
        {
            fixed (char* property = OwnershipProperty) return (nint)Win32.GetPropW((HWND)window, property);
        },
        (window, value) =>
        {
            fixed (char* property = OwnershipProperty) return Win32.SetPropW((HWND)window, property, (HANDLE)value).Value != 0;
        },
        window =>
        {
            fixed (char* property = OwnershipProperty) return (nint)Win32.RemovePropW((HWND)window, property);
        },
        () =>
        {
            LARGE_INTEGER timestamp;
            return Win32.QueryPerformanceCounter(&timestamp).Value != 0 ? timestamp.QuadPart : 0;
        });
}
