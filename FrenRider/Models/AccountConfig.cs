using System;
using System.Collections.Generic;

namespace FrenRider.Models;

[Serializable]
public class AccountConfig
{
    public string AccountId { get; set; } = "";
    public string AccountAlias { get; set; } = "";
    public CharacterConfig DefaultConfig { get; set; } = new();
    public Dictionary<string, CharacterConfig> Characters { get; set; } = new();
    public List<RemoteProfileRow> RemoteProfiles { get; set; } = new();
}

[Serializable]
public class RemoteProfileRow
{
    public string RowId { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string IslandId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public CharacterConfig Config { get; set; } = new();
}
