using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace FrenRider.Services;

public class FrenTracker
{
    private readonly Plugin plugin;
    private long lastUpdateMs;

    public FrenState? Fren { get; private set; }
    public List<PartyMemberState> Party { get; private set; } = new();

    public FrenTracker(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Update()
    {
        if (!Plugin.ClientState.IsLoggedIn)
        {
            Fren = null;
            Party.Clear();
            return;
        }

        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            Fren = null;
            Party.Clear();
            return;
        }

        var now = Environment.TickCount64;
        var intervalMs = (long)(config.UpdateInterval * 1000);
        if (now - lastUpdateMs < intervalMs) return;
        lastUpdateMs = now;

        ScanParty();
        FindFren(config.FrenName);
    }

    private void ScanParty()
    {
        Party.Clear();
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var partyCount = Plugin.PartyList.Length;
        for (var i = 0; i < partyCount; i++)
        {
            var member = Plugin.PartyList[i];
            if (member == null) continue;

            var memberName = member.Name.ToString();
            var worldName = member.World.Value.Name.ToString();

            uint classJobId = 0;
            string classJobName = "";
            try
            {
                classJobId = member.ClassJob.RowId;
                classJobName = member.ClassJob.Value.Name.ToString();
            }
            catch
            {
                // ClassJob access may fail for cross-world party members
            }

            var info = new PartyMemberState
            {
                Name = memberName,
                WorldName = worldName,
                ClassJobId = classJobId,
                ClassJobName = classJobName,
                Role = GetRole(classJobId),
                PartyIndex = i,
            };

            // Find in ObjectTable for position data
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj != null && obj.Name.ToString() == memberName)
                {
                    info.Position = obj.Position;
                    info.DistanceToPlayer = Vector3.Distance(localPlayer.Position, obj.Position);
                    info.IsVisible = true;

                    // Mount detection via FFXIVClientStructs
                    try
                    {
                        unsafe
                        {
                            var chara = (Character*)obj.Address;
                            info.IsMounted = chara->IsMounted();
                            info.MountId = chara->Mount.MountId;
                        }
                    }
                    catch { /* Mount data inaccessible */ }

                    break;
                }
            }

            Party.Add(info);
        }
    }

    private void FindFren(string frenName)
    {
        if (string.IsNullOrWhiteSpace(frenName))
        {
            Fren = null;
            return;
        }

        // Extract name part (before @, which is cosmetic)
        var searchName = frenName.Split('@')[0].Trim();
        if (string.IsNullOrEmpty(searchName))
        {
            Fren = null;
            return;
        }

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            Fren = null;
            return;
        }

        // Search party members first (preferred)
        foreach (var member in Party)
        {
            if (member.Name.Contains(searchName, StringComparison.OrdinalIgnoreCase))
            {
                // Check if fren is flying by looking at their GameObject
                var frenFlying = false;
                foreach (var obj in Plugin.ObjectTable)
                {
                    if (obj != null && obj.Name.ToString() == member.Name)
                    {
                        // Check if the object has InFlight condition
                        // Note: We can't directly check other player's conditions, so we check position height
                        // If mounted and Y position is significantly higher than ground, likely flying
                        frenFlying = member.IsMounted && obj.Position.Y > localPlayer.Position.Y + 2.0f;
                        break;
                    }
                }
                
                Fren = new FrenState
                {
                    Name = member.Name,
                    WorldName = member.WorldName,
                    Position = member.Position,
                    Distance = member.DistanceToPlayer,
                    IsFound = true,
                    IsVisible = member.IsVisible,
                    ClassJobId = member.ClassJobId,
                    ClassJobName = member.ClassJobName,
                    Role = member.Role,
                    InParty = true,
                    IsMounted = member.IsMounted,
                    MountId = member.MountId,
                    IsFlying = frenFlying,
                };
                return;
            }
        }

        // Not in party - scan ObjectTable for nearby player by partial name
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            var objName = obj.Name.ToString();
            if (string.IsNullOrEmpty(objName)) continue;
            if (obj == localPlayer) continue;
            if (!objName.Contains(searchName, StringComparison.OrdinalIgnoreCase)) continue;

            var frenState = new FrenState
            {
                Name = objName,
                Position = obj.Position,
                Distance = Vector3.Distance(localPlayer.Position, obj.Position),
                IsFound = true,
                IsVisible = true,
                InParty = false,
            };

            // Mount detection
            try
            {
                unsafe
                {
                    var chara = (Character*)obj.Address;
                    frenState.IsMounted = chara->IsMounted();
                    frenState.MountId = chara->Mount.MountId;
                }
            }
            catch { /* Mount data inaccessible */ }

            Fren = frenState;
            return;
        }

        // Not found anywhere
        Fren = new FrenState
        {
            Name = searchName,
            IsFound = false,
        };
    }

    /// <summary>
    /// Maps ClassJob IDs to combat role categories.
    /// </summary>
    public static string GetRole(uint classJobId)
    {
        return classJobId switch
        {
            // Tanks: GLA, MRD, PLD, WAR, DRK, GNB
            1 or 3 or 19 or 21 or 32 or 37 => "Tank",
            // Healers: CNJ, WHM, SCH, AST, SGE
            6 or 24 or 28 or 33 or 40 => "Healer",
            // Melee DPS: PGL, LNC, MNK, DRG, ROG, NIN, SAM, RPR, VPR
            2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 => "Melee",
            // Physical Ranged DPS: ARC, BRD, MCH, DNC
            5 or 23 or 31 or 38 => "Ranged",
            // Magical Ranged DPS: THM, BLM, ACN, SMN, SCH-base, RDM, BLU, PCT
            7 or 25 or 26 or 27 or 35 or 36 or 42 => "Caster",
            _ => "Other",
        };
    }

    /// <summary>Counts party composition by role.</summary>
    public Dictionary<string, int> GetPartyComposition()
    {
        var comp = new Dictionary<string, int>();
        foreach (var member in Party)
        {
            if (!comp.ContainsKey(member.Role))
                comp[member.Role] = 0;
            comp[member.Role]++;
        }
        return comp;
    }

    // --- Data Classes ---

    public class FrenState
    {
        public string Name { get; set; } = "";
        public string WorldName { get; set; } = "";
        public Vector3 Position { get; set; }
        public float Distance { get; set; }
        public bool IsFound { get; set; }
        public bool IsVisible { get; set; }
        public uint ClassJobId { get; set; }
        public string ClassJobName { get; set; } = "";
        public string Role { get; set; } = "";
        public bool InParty { get; set; }
        public bool IsMounted { get; set; }
        public ushort MountId { get; set; }
        public bool IsFlying { get; set; }
    }

    public class PartyMemberState
    {
        public string Name { get; set; } = "";
        public string WorldName { get; set; } = "";
        public Vector3 Position { get; set; }
        public float DistanceToPlayer { get; set; }
        public uint ClassJobId { get; set; }
        public string ClassJobName { get; set; } = "";
        public string Role { get; set; } = "";
        public int PartyIndex { get; set; }
        public bool IsVisible { get; set; }
        public bool IsMounted { get; set; }
        public ushort MountId { get; set; }
    }
}
