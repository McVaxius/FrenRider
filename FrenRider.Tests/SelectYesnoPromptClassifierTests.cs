using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class SelectYesnoPromptClassifierTests
{
    [Theory]
    [InlineData("Accept Teleport to Limsa Lominsa?")]
    [InlineData("Would you like to teleport to the nearest aetheryte?")]
    [InlineData("Teleport to the estate hall?")]
    public void TeleportPromptsClassifyAsTeleport(string prompt)
    {
        Assert.Equal(SelectYesnoPromptKind.Teleport, SelectYesnoPromptClassifier.Classify(prompt));
    }

    [Theory]
    [InlineData("Return to your home point?")]
    [InlineData("Would you like to return to your Home Point?")]
    [InlineData("Return to the aetheryte plaza?")]
    public void DeathReturnPromptsClassifyAsDeathReturn(string prompt)
    {
        Assert.Equal(SelectYesnoPromptKind.DeathReturn, SelectYesnoPromptClassifier.Classify(prompt));
    }

    [Theory]
    [InlineData("Would you like to be raised?")]
    [InlineData("Accept Raise?")]
    public void RaisePromptsClassifyAsRaise(string prompt)
    {
        Assert.Equal(SelectYesnoPromptKind.Raise, SelectYesnoPromptClassifier.Classify(prompt));
    }

    [Theory]
    [InlineData("Would you like to join the party?")]
    [InlineData("Join Jane Doe's party?")]
    public void PartyPromptsClassifyAsParty(string prompt)
    {
        Assert.Equal(SelectYesnoPromptKind.Party, SelectYesnoPromptClassifier.Classify(prompt));
    }

    [Theory]
    [InlineData("Return to the levemete?")]
    [InlineData("Return to the starting point for the Praetorium?")]
    public void LeveAndStartingPointReturnsDoNotClassifyAsDeathReturn(string prompt)
    {
        var kind = SelectYesnoPromptClassifier.Classify(prompt);

        Assert.Equal(SelectYesnoPromptKind.Misc, kind);
        Assert.NotEqual(SelectYesnoPromptKind.DeathReturn, kind);
    }

    [Fact]
    public void UnknownPromptClassifiesAsUnknown()
    {
        Assert.Equal(SelectYesnoPromptKind.Unknown, SelectYesnoPromptClassifier.Classify("Do the thing?"));
    }
}
