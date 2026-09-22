using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FrenRider.Services;

internal sealed record LocalAethernetNode(uint Id, uint Territory, Vector2 Position, string Name, string Alias = "");

internal sealed record LocalAethernetNetwork(
    LocalAethernetNode Origin,
    IReadOnlyList<LocalAethernetNode> Nodes,
    float InteractionDistance);

internal sealed record LocalAethernetSample(string Target, uint Territory, Vector3 Position, long Timestamp);

internal sealed class LocalAethernetFollowDetector
{
    internal const long MaxSampleAgeMs = 3000;
    private LocalAethernetSample? previous;

    internal void Clear() => previous = null;

    internal bool Observe(
        LocalAethernetSample? sample,
        LocalAethernetNetwork? network,
        Vector3? originPosition,
        Vector3 followerPosition,
        long now,
        out LocalAethernetNode destination)
    {
        destination = null!;
        var before = previous;
        if (sample == null || string.IsNullOrEmpty(sample.Target) || sample.Territory == 0
            || !IsValidPosition(sample.Position) || sample.Timestamp > now
            || now - sample.Timestamp > MaxSampleAgeMs)
        {
            Clear();
            return false;
        }

        // Consume every new sample, including rejected jumps: a busy/rejected request is not retried.
        previous = sample;
        if (before == null || before.Target != sample.Target || sample.Timestamp <= before.Timestamp
            || now - before.Timestamp > MaxSampleAgeMs
            || Vector3.Distance(before.Position, sample.Position) <= 50f
            || network == null || originPosition == null || before.Territory != network.Origin.Territory
            || !IsInInteractionRange(before.Position, originPosition.Value, network.InteractionDistance)
            || !IsInInteractionRange(followerPosition, originPosition.Value, network.InteractionDistance))
            return false;

        var matches = network.Nodes.Where(node => node.Id != network.Origin.Id
            && node.Territory == sample.Territory
            && Vector2.Distance(node.Position, Xz(sample.Position)) <= 25f).ToArray();
        if (matches.Length != 1)
            return false;

        var match = matches[0];
        // Lifestream's name IPC can match substrings and configured aliases.
        if (string.IsNullOrWhiteSpace(match.Name) || network.Nodes.Any(node => node.Id != match.Id
            && (node.Name.Contains(match.Name, StringComparison.OrdinalIgnoreCase)
                || node.Alias.Contains(match.Name, StringComparison.OrdinalIgnoreCase))))
            return false;

        destination = match;
        return true;
    }

    internal static Vector2 Xz(Vector3 position) => new(position.X, position.Z);

    internal static bool IsValidPosition(Vector3 position)
        => position != Vector3.Zero && float.IsFinite(position.X)
            && float.IsFinite(position.Y) && float.IsFinite(position.Z);

    internal static bool IsInInteractionRange(Vector3 position, Vector3 origin, float maxDistance)
        => IsValidPosition(position) && IsValidPosition(origin)
            && Vector2.Distance(Xz(position), Xz(origin)) < 11f
            && Vector3.Distance(position, origin) < 15f
            && Vector3.Distance(position, origin) <= maxDistance;
}
