using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ZoneServiceTests
{
    [Theory]
    [InlineData(732)]
    [InlineData(920)]
    [InlineData(1237)]
    [InlineData(1252)]
    public void NonDiademForaysRestrictFlight(uint territoryId)
    {
        Assert.True(ZoneService.IsFlightRestrictedForay(territoryId));
    }

    [Fact]
    public void DiademRemainsFlightEnabled()
    {
        Assert.False(ZoneService.IsFlightRestrictedForay(ZoneService.DiademTerritoryId));
    }

    [Fact]
    public void NonForayTerritoriesDoNotRestrictFlight()
    {
        Assert.False(ZoneService.IsFlightRestrictedForay(129));
    }
}
