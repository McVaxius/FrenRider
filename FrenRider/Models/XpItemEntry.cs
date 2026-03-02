namespace FrenRider.Models;

public class XpItemEntry
{
    public int ItemId { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int SlotId { get; set; } // 0=Head, 1=Body, 2=Hands, 3=Legs, 4=Feet, 5=Earring, 6=Necklace, 7=Bracelet, 8=Ring1, 9=Ring2

    public XpItemEntry()
    {
    }

    public XpItemEntry(int itemId, int minLevel, int maxLevel, int slotId)
    {
        ItemId = itemId;
        MinLevel = minLevel;
        MaxLevel = maxLevel;
        SlotId = slotId;
    }

    public XpItemEntry Clone()
    {
        return new XpItemEntry
        {
            ItemId = ItemId,
            MinLevel = MinLevel,
            MaxLevel = MaxLevel,
            SlotId = SlotId
        };
    }
}
