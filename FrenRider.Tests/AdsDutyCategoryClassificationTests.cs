using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsDutyCategoryClassificationTests
{
    [Theory]
    [InlineData(1, AdsDutyCategory.Solo)]
    [InlineData(4, AdsDutyCategory.FourMan)]
    [InlineData(8, AdsDutyCategory.EightMan)]
    [InlineData(24, AdsDutyCategory.Alliance)]
    public void DesignedPartySizeDeterminesStandardCategory(int partySize, AdsDutyCategory expected)
    {
        var category = Classify(partySize: partySize);

        Assert.Equal(expected, category);
    }

    [Fact]
    public void DutySupportDungeonRemainsFourManWithOneHumanPlayer()
    {
        // Classification deliberately receives the ContentMemberType design size,
        // not the live human party count.
        var category = Classify(partySize: 4, contentTypeName: "Duty Support");

        Assert.Equal(AdsDutyCategory.FourMan, category);
    }

    [Theory]
    [InlineData(5u, 4, "", AdsDutyCategory.GuildHest)]
    [InlineData(21u, 4, "", AdsDutyCategory.DeepDungeon)]
    [InlineData(0u, 4, "Deep Dungeon", AdsDutyCategory.DeepDungeon)]
    [InlineData(0u, 1, "Treasure Hunt", AdsDutyCategory.TreasureDungeon)]
    [InlineData(0u, 8, "Alliance Raids", AdsDutyCategory.Alliance)]
    public void SpecialCategoriesTakePriorityOverPartySize(
        uint contentTypeRowId,
        int partySize,
        string contentTypeName,
        AdsDutyCategory expected)
    {
        var category = Classify(
            contentTypeRowId: contentTypeRowId,
            partySize: partySize,
            contentTypeName: contentTypeName);

        Assert.Equal(expected, category);
    }

    [Fact]
    public void KnownTreasureTerritoryTakesPriorityOverSoloPartySize()
    {
        var category = Classify(territoryTypeId: 558, partySize: 1);

        Assert.Equal(AdsDutyCategory.TreasureDungeon, category);
    }

    [Fact]
    public void UnknownDesignMetadataDoesNotBecomeSolo()
    {
        var category = Classify(partySize: 0, contentMemberTypeRowId: 0);

        Assert.Equal(AdsDutyCategory.Other, category);
    }

    [Theory]
    [InlineData(3u, AdsDutyCategory.Solo)]
    [InlineData(4u, AdsDutyCategory.FourMan)]
    [InlineData(5u, AdsDutyCategory.EightMan)]
    [InlineData(6u, AdsDutyCategory.Alliance)]
    public void ContentMemberTypeFallbackIsUsedWhenRoleCountsAreUnavailable(
        uint contentMemberTypeRowId,
        AdsDutyCategory expected)
    {
        var category = Classify(
            partySize: 0,
            contentMemberTypeRowId: contentMemberTypeRowId);

        Assert.Equal(expected, category);
    }

    private static AdsDutyCategory Classify(
        uint territoryTypeId = 9999,
        uint contentTypeRowId = 0,
        uint contentMemberTypeRowId = 0,
        int partySize = 4,
        string contentTypeName = "Dungeon")
    {
        return AdsIntegrationService.ClassifyDutyCategory(
            territoryTypeId,
            contentTypeRowId,
            contentMemberTypeRowId,
            partySize,
            contentTypeName);
    }
}
