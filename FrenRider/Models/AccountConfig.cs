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
}
