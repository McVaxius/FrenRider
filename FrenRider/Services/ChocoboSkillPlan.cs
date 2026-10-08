using System.Collections.Generic;

namespace FrenRider.Services;

internal enum ChocoboSkillTree
{
    Defender = 0,
    Attacker = 1,
    Healer = 2,
}

internal readonly record struct ChocoboSkillStep(ChocoboSkillTree Tree, int Level, int Cost, int Payload);

internal static class ChocoboSkillPlan
{
    // This plans a candidate only; the caller verifies native eligibility and acceptance.
    internal static bool TryGetNext(CompanionSnapshot snapshot, IReadOnlyList<ChocoboSkillTree> priority,
        out ChocoboSkillStep step)
    {
        step = default;
        if (priority is null || priority.Count is < 1 or > 3
            || snapshot.SkillPoints is 0 or > 165
            || snapshot.DefenderLevel > 10 || snapshot.AttackerLevel > 10 || snapshot.HealerLevel > 10)
            return false;

        var trees = 0;
        foreach (var tree in priority)
        {
            var index = (int)tree;
            if (index is < 0 or > 2) return false;
            var flag = 1 << index;
            if ((trees & flag) != 0) return false;
            trees |= flag;
        }

        foreach (var tree in priority)
        {
            var currentLevel = tree switch
            {
                ChocoboSkillTree.Defender => snapshot.DefenderLevel,
                ChocoboSkillTree.Attacker => snapshot.AttackerLevel,
                _ => snapshot.HealerLevel, // All entries were validated above.
            };
            if (currentLevel == 10) continue;

            var nextLevel = currentLevel + 1;
            // Waiting for the first unfinished tree must not spend on a lower priority.
            if (snapshot.SkillPoints < nextLevel) return false;
            step = new(tree, nextLevel, nextLevel, (nextLevel - 1) * 3 + (int)tree);
            return true;
        }

        return false;
    }
}
