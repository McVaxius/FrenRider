using System.Collections.Generic;
using System.Linq;

namespace FrenRider.Models;

public enum AdsDutyCategory
{
    Solo = 0,
    FourMan = 1,
    EightMan = 2,
    Alliance = 3,
    GuildHest = 4,
    DeepDungeon = 5,
    TreasureDungeon = 6,
    Other = 7,
}

public readonly record struct AdsDutyFamilySettings(bool Enabled, int MaturityThreshold);

public sealed record AdsDutyCategoryEntry(AdsDutyCategory Category, string Label);

public static class AdsDutyCategoryCatalog
{
    public static readonly IReadOnlyList<AdsDutyCategoryEntry> Entries =
    [
        new(AdsDutyCategory.Solo, "Solo"),
        new(AdsDutyCategory.FourMan, "4-Man"),
        new(AdsDutyCategory.EightMan, "8-Man"),
        new(AdsDutyCategory.Alliance, "Alliance"),
        new(AdsDutyCategory.GuildHest, "Guild Hest"),
        new(AdsDutyCategory.DeepDungeon, "Deep Dungeon"),
        new(AdsDutyCategory.TreasureDungeon, "Treasure Dungeon"),
        new(AdsDutyCategory.Other, "Other"),
    ];

    public static string GetLabel(AdsDutyCategory category)
        => Entries.FirstOrDefault(x => x.Category == category)?.Label ?? category.ToString();
}
