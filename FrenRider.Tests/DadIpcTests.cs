using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class DadIpcTests
{
    private const string ActiveCharacterKey = "Participant One@Excalibur";

    [Fact]
    public void ApostropheTargetIsPersistedExactlyAndEnablesOnlyActiveCharacter()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];
        var otherConfig = account.Characters["Other Character@Excalibur"];
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "O'Brien Tia@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.True(succeeded);
        Assert.True(becameEnabled);
        Assert.Equal(1, saveCount);
        Assert.Equal("O'Brien Tia@Excalibur", activeConfig.FrenName);
        Assert.True(activeConfig.Enabled);
        Assert.Equal("Default Fren@Gilgamesh", account.DefaultConfig.FrenName);
        Assert.False(account.DefaultConfig.Enabled);
        Assert.Equal("Other Fren@Gilgamesh", otherConfig.FrenName);
        Assert.False(otherConfig.Enabled);
    }

    [Fact]
    public void MissingAccountIsRejectedWithoutPersistence()
    {
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            null,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Missing Character@Excalibur")]
    public void MissingActiveCharacterIsRejectedWithoutFallingBackToDefault(string selectedCharacterKey)
    {
        var account = CreateAccount();
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            selectedCharacterKey,
            "Venat Azem@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
        Assert.Equal("Default Fren@Gilgamesh", account.DefaultConfig.FrenName);
        Assert.False(account.DefaultConfig.Enabled);
        Assert.Equal("Old Fren@Gilgamesh", account.Characters[ActiveCharacterKey].FrenName);
        Assert.False(account.Characters[ActiveCharacterKey].Enabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Venat Azem")]
    [InlineData("@Excalibur")]
    [InlineData("Venat Azem@")]
    [InlineData("Venat Azem@@Excalibur")]
    [InlineData(" Venat Azem@Excalibur")]
    [InlineData("Venat Azem@Excalibur ")]
    [InlineData("Venat Azem @Excalibur")]
    [InlineData("Venat Azem@ Excalibur")]
    [InlineData("Venat Azem@Crystal Tower")]
    public void MalformedTargetIsRejectedWithoutMutation(string? target)
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            target,
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void SaveFailureRollsBackTargetAndEnabledState()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () => false,
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void SaveExceptionRollsBackTargetAndEnabledState()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () => throw new IOException("disk unavailable"),
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void RepeatedSuccessfulConfigurationIsIdempotent()
    {
        var account = CreateAccount();
        var saveCount = 0;

        bool Persist()
        {
            saveCount++;
            return true;
        }

        var first = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            Persist,
            out var firstBecameEnabled);
        var second = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            Persist,
            out var secondBecameEnabled);

        Assert.True(first);
        Assert.True(second);
        Assert.True(firstBecameEnabled);
        Assert.False(secondBecameEnabled);
        Assert.Equal(1, saveCount);
        Assert.Equal("Venat Azem@Excalibur", account.Characters[ActiveCharacterKey].FrenName);
        Assert.True(account.Characters[ActiveCharacterKey].Enabled);
    }

    [Fact]
    public void EndpointRegistersTypedHandlerAndForcesTrackerScanAfterSuccess()
    {
        Func<string, bool>? registeredHandler = null;
        var configuredTarget = string.Empty;
        var scanCount = 0;
        var unregisterCount = 0;

        var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => unregisterCount++,
            configureAndEnable: target =>
            {
                configuredTarget = target;
                return true;
            },
            forceNextTrackerScan: () => scanCount++);

        Assert.Equal("FrenRider.Dad.ConfigureAndEnable", DadIPC.ConfigureAndEnableEndpoint);
        Assert.NotNull(registeredHandler);
        Assert.True(registeredHandler!("O'Brien Tia@Excalibur"));
        Assert.Equal("O'Brien Tia@Excalibur", configuredTarget);
        Assert.Equal(1, scanCount);

        endpoint.Dispose();
        endpoint.Dispose();
        Assert.Equal(1, unregisterCount);
    }

    [Fact]
    public void EndpointRejectionDoesNotForceTrackerScan()
    {
        Func<string, bool>? registeredHandler = null;
        var scanCount = 0;
        using var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => { },
            configureAndEnable: _ => false,
            forceNextTrackerScan: () => scanCount++);

        Assert.False(registeredHandler!("Venat Azem@Excalibur"));
        Assert.Equal(0, scanCount);
    }

    [Fact]
    public void EndpointExceptionReturnsFalseAndDoesNotForceTrackerScan()
    {
        Func<string, bool>? registeredHandler = null;
        var scanCount = 0;
        using var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => { },
            configureAndEnable: _ => throw new InvalidOperationException("unexpected failure"),
            forceNextTrackerScan: () => scanCount++);

        Assert.False(registeredHandler!("Venat Azem@Excalibur"));
        Assert.Equal(0, scanCount);
    }

    private static AccountConfig CreateAccount()
        => new()
        {
            AccountId = "account",
            DefaultConfig = new CharacterConfig
            {
                FrenName = "Default Fren@Gilgamesh",
                Enabled = false,
            },
            Characters = new Dictionary<string, CharacterConfig>
            {
                [ActiveCharacterKey] = new()
                {
                    FrenName = "Old Fren@Gilgamesh",
                    Enabled = false,
                },
                ["Other Character@Excalibur"] = new()
                {
                    FrenName = "Other Fren@Gilgamesh",
                    Enabled = false,
                },
            },
        };
}
