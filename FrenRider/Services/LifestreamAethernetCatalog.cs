using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;

namespace FrenRider.Services;

// Lifestream has travel/active-node IPC, but no coordinate IPC. Keep the private
// catalog contract here; an unloaded plugin or changed structure rejects the read.
internal static class LifestreamAethernetCatalog
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static bool TryRead(uint territory, out LocalAethernetNetwork network, out Vector3 originPosition)
    {
        network = null!;
        originPosition = default;
        try
        {
            var ipc = Plugin.PluginInterface;
            var normalId = ipc.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte").InvokeFunc();
            var customId = ipc.GetIpcSubscriber<uint>("Lifestream.GetActiveCustomAetheryte").InvokeFunc();
            var residentialId = ipc.GetIpcSubscriber<uint>("Lifestream.GetActiveResidentialAetheryte").InvokeFunc();
            if ((normalId != 0 ? 1 : 0) + (customId != 0 ? 1 : 0) + (residentialId != 0 ? 1 : 0) != 1)
                return false;

            var instance = ipc.GetIpcSubscriber<IDalamudPlugin>("Lifestream.Instance").InvokeFunc();
            var assembly = instance.GetType().Assembly;
            var dataType = assembly.GetType("Lifestream.Services.Service+Data", throwOnError: true)!;
            var nodes = new List<LocalAethernetNode>();
            var activeId = normalId != 0 ? normalId : customId != 0 ? customId : residentialId;
            var maxDistance = 15f;

            if (normalId != 0)
            {
                var dataStore = dataType.GetField("DataStore")!.GetValue(null)!;
                var groups = (IDictionary)Read(dataStore, "Aetherytes");
                var renames = (IDictionary)Read(Read(instance, "Config"), "Renames");
                foreach (DictionaryEntry group in groups)
                {
                    var members = ((IEnumerable)group.Value!).Cast<object>().Prepend(group.Key).ToArray();
                    if (!members.Any(member => (uint)Read(member, "ID") == normalId))
                        continue;
                    if (nodes.Count != 0)
                        return false;

                    var groupId = (uint)Read(group.Key, "Group");
                    if (groupId == 0 || members.Any(member => (uint)Read(member, "Group") != groupId))
                        return false;
                    nodes.AddRange(members.Select(member => ReadNode(member, renames)));
                }
            }
            else
            {
                var catalog = dataType.GetField(customId != 0 ? "CustomAethernet" : "ResidentialAethernet")!.GetValue(null)!;
                var zones = (IDictionary)Read(catalog, "ZoneInfo");
                if (!zones.Contains(territory))
                    return false;
                var zone = zones[territory]!;
                maxDistance = customId != 0 ? (float)Read(zone, "MaxInteractionDistance") : 4.6f;
                nodes.AddRange(((IEnumerable)Read(zone, "Aetherytes")).Cast<object>().Select(node => ReadNode(node)));
                if (nodes.Any(node => node.Territory != territory))
                    return false;
            }

            if (nodes.Count < 2 || nodes.Select(node => node.Id).Distinct().Count() != nodes.Count
                || !float.IsFinite(maxDistance) || maxDistance <= 0)
                return false;
            var origins = nodes.Where(node => node.Id == activeId && node.Territory == territory).ToArray();
            if (origins.Length != 1)
                return false;

            var origin = origins[0];
            var shardIds = (uint[])assembly.GetType("Lifestream.Utils", throwOnError: true)!
                .GetProperty("AethernetShards")!.GetValue(null)!;
            var objects = Plugin.ObjectTable.Where(obj => obj.IsTargetable
                && (obj.ObjectKind == ObjectKind.Aetheryte || shardIds.Contains(obj.BaseId))
                && Vector2.Distance(LocalAethernetFollowDetector.Xz(obj.Position), origin.Position) < 10f).ToArray();
            if (objects.Length != 1)
                return false;

            originPosition = objects[0].Position;
            network = new LocalAethernetNetwork(origin, nodes, maxDistance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static LocalAethernetNode ReadNode(object node, IDictionary? renames = null)
    {
        var id = (uint)Read(node, "ID");
        var territory = (uint)Read(node, "TerritoryType");
        var position = (Vector2)Read(node, "Position");
        var name = (string)Read(node, "Name");
        if (id == 0 || territory == 0 || position == Vector2.Zero
            || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Incomplete Lifestream aethernet node");
        var alias = renames?.Contains(id) == true ? (string)renames[id]! : "";
        return new LocalAethernetNode(id, territory, position, name, alias);
    }

    private static object Read(object instance, string name)
        => instance.GetType().GetProperty(name, InstanceMembers)?.GetValue(instance)
            ?? instance.GetType().GetField(name, InstanceMembers)?.GetValue(instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, name);
}
