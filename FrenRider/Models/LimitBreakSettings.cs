using System;

namespace FrenRider.Models;

public enum LimitBreakRole { None, Tank, Healer, Melee, PhysicalRanged, MagicalRanged }
public enum LimitBreakGauge { FullAvailable, LB1, LB2, LB3 }
public enum LimitBreakTarget { BossOnly, CurrentTarget, AnyEnemy }

public sealed class DamageLimitBreakPolicy
{
    public bool Enabled { get; set; }
    public LimitBreakGauge Gauge { get; set; } = LimitBreakGauge.FullAvailable;
    public LimitBreakTarget Target { get; set; } = LimitBreakTarget.BossOnly;
    public float TargetHpPercent { get; set; } = 100;
    public int MinimumEnemies { get; set; } = 1;
    public DamageLimitBreakPolicy Clone() => (DamageLimitBreakPolicy)MemberwiseClone();
    internal bool IsValid => Enum.IsDefined(Gauge) && Enum.IsDefined(Target)
        && float.IsFinite(TargetHpPercent) && TargetHpPercent is >= 0 and <= 100
        && MinimumEnemies is >= 1 and <= 100;
}

public sealed class TankLimitBreakPolicy
{
    public bool Enabled { get; set; }
    public LimitBreakGauge Gauge { get; set; } = LimitBreakGauge.LB3;
    public float PartyHpPercent { get; set; } = 30;
    public TankLimitBreakPolicy Clone() => (TankLimitBreakPolicy)MemberwiseClone();
    internal bool IsValid => Enum.IsDefined(Gauge) && float.IsFinite(PartyHpPercent)
        && PartyHpPercent is >= 0 and <= 100;
}

public sealed class HealerLimitBreakPolicy
{
    public bool Enabled { get; set; }
    public bool Revive { get; set; } = true;
    public int MinimumKoMembers { get; set; } = 3;
    public bool Heal { get; set; }
    public float PartyHpPercent { get; set; } = 30;
    public HealerLimitBreakPolicy Clone() => (HealerLimitBreakPolicy)MemberwiseClone();
    internal bool IsValid => MinimumKoMembers is >= 1 and <= 7
        && float.IsFinite(PartyHpPercent) && PartyHpPercent is >= 0 and <= 100;
}

public sealed class LimitBreakSettings
{
    public TankLimitBreakPolicy Tank { get; set; } = new();
    public HealerLimitBreakPolicy Healer { get; set; } = new();
    public DamageLimitBreakPolicy Melee { get; set; } = new();
    public DamageLimitBreakPolicy PhysicalRanged { get; set; } = new()
        { Target = LimitBreakTarget.AnyEnemy, MinimumEnemies = 3 };
    public DamageLimitBreakPolicy MagicalRanged { get; set; } = new()
        { Target = LimitBreakTarget.AnyEnemy, MinimumEnemies = 3 };

    internal bool IsValid => Tank?.IsValid == true && Healer?.IsValid == true
        && Melee?.IsValid == true && PhysicalRanged?.IsValid == true && MagicalRanged?.IsValid == true;

    public LimitBreakSettings Clone() => new()
    {
        Tank = Tank.Clone(), Healer = Healer.Clone(), Melee = Melee.Clone(),
        PhysicalRanged = PhysicalRanged.Clone(), MagicalRanged = MagicalRanged.Clone(),
    };

    internal static LimitBreakSettings FromLegacy(float threshold)
    {
        var settings = new LimitBreakSettings();
        if (float.IsFinite(threshold) && threshold is >= 0 and <= 100)
            settings.Melee.TargetHpPercent = settings.PhysicalRanged.TargetHpPercent
                = settings.MagicalRanged.TargetHpPercent = threshold;
        return settings; // The old runtime was a stub: never opt an old profile into spending gauge.
    }

    internal DamageLimitBreakPolicy? Damage(LimitBreakRole role) => role switch
    {
        LimitBreakRole.Melee => Melee, LimitBreakRole.PhysicalRanged => PhysicalRanged,
        LimitBreakRole.MagicalRanged => MagicalRanged, _ => null,
    };
}
