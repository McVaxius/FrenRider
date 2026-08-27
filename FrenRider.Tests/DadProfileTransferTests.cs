using System.Reflection;
using System.Text;
using System.Text.Json;
using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class DadProfileTransferTests
{
    private const string ActiveCharacterKey = "Local Character@World";

    [Fact]
    public void RemoteRowsAreExactSeparateAndRejectDuplicates()
    {
        var account = CreateAccount();
        account.DefaultConfig.FrenName = "Default Fren";
        var localCount = account.Characters.Count;

        Assert.True(ConfigManager.TryResolveOrCreateRemoteProfile(
            account, "owner", "island", "opaque", "Remote Label",
            out var created, out var added, out var code));
        Assert.Equal("ok", code);
        Assert.True(added);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.RowId));
        Assert.Equal("owner", created.OwnerId);
        Assert.Equal("island", created.IslandId);
        Assert.Equal("opaque", created.CharacterId);
        Assert.Equal("Remote Label", created.DisplayLabel);
        Assert.Equal("Default Fren", created.Config.FrenName);
        Assert.NotSame(account.DefaultConfig, created.Config);
        Assert.Equal(localCount, account.Characters.Count);
        Assert.DoesNotContain(created.RowId, account.Characters.Keys);

        Assert.True(ConfigManager.TryResolveOrCreateRemoteProfile(
            account, "owner", "island", "opaque", "Changed Cosmetic Label",
            out var resolved, out added, out code));
        Assert.False(added);
        Assert.Same(created, resolved);
        Assert.Equal("Remote Label", resolved!.DisplayLabel);

        account.RemoteProfiles.Add(new RemoteProfileRow
        {
            OwnerId = "owner",
            IslandId = "island",
            CharacterId = "opaque",
        });
        Assert.False(ConfigManager.TryResolveOrCreateRemoteProfile(
            account, "owner", "island", "opaque", "Ignored",
            out resolved, out added, out code));
        Assert.Equal("duplicate-remote-profile", code);
        Assert.Null(resolved);
        Assert.False(added);
        Assert.Equal(localCount, account.Characters.Count);
    }

    [Fact]
    public void LegacyAccountJsonGetsAnEmptySeparateRemoteCollection()
    {
        var account = JsonSerializer.Deserialize<AccountConfig>(
            "{\"AccountId\":\"legacy\",\"Characters\":{\"Local@World\":{\"FrenName\":\"Existing\"}}}")!;

        Assert.NotNull(account.RemoteProfiles);
        Assert.Empty(account.RemoteProfiles);
        Assert.Equal("Existing", account.Characters["Local@World"].FrenName);
        Assert.Equal(
            FrenRiderProfileAcceptancePolicy.Temporary,
            account.Characters["Local@World"].ProfileAcceptancePolicy);
    }

    [Fact]
    public void ExportContainsEverySerializableSettingExceptAcceptancePolicy()
    {
        var config = new CharacterConfig
        {
            FrenName = "  o'Brien TIA@ExactWorld  ",
            Enabled = true,
            ProfileAcceptancePolicy = FrenRiderProfileAcceptancePolicy.Off,
            InviteWhitelist = new List<string> { "One", "Two" },
            CustomIdleList = new[] { "/wave", "/dance" },
        };

        Assert.True(DadProfileTransferService.TrySerializeProfile(config, out var json, out var code));
        Assert.Equal("ok", code);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= DadProfileTransferContract.MaximumProfileJsonBytes);

        using var document = JsonDocument.Parse(json);
        var actual = document.RootElement.GetProperty("config")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var expected = typeof(CharacterConfig)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead
                               && property.CanWrite
                               && property.Name != nameof(CharacterConfig.ProfileAcceptancePolicy))
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(expected.SetEquals(actual));
        Assert.DoesNotContain("profileAcceptancePolicy", actual);
        Assert.True(DadProfileTransferService.TryDeserializeProfile(json, out var roundTrip, out code));
        Assert.Equal("  o'Brien TIA@ExactWorld  ", roundTrip!.FrenName);
        Assert.True(roundTrip.Enabled);
    }

    [Fact]
    public void TemporaryApplyUsesExactOverlayAndExactReleaseTuple()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Temporary);
        var persisted = account.Characters[ActiveCharacterKey];
        var manager = new TestProfileStore(account, ActiveCharacterKey);
        using var service = new DadProfileTransferService(manager);
        var incoming = Export(new CharacterConfig
        {
            FrenName = "Incoming EXACT value",
            Enabled = true,
            Cling = 9.25f,
            ProfileAcceptancePolicy = FrenRiderProfileAcceptancePolicy.Permanent,
        });

        var apply = ReadResult(service.ApplyProfile(ApplyRequest(incoming)));
        Assert.Equal("ok", apply.Code);
        Assert.Equal("temporary-applied", apply.Outcome);
        Assert.True(manager.HasTemporaryProfile);
        Assert.Equal("Incoming EXACT value", manager.GetActiveConfig().FrenName);
        Assert.True(manager.GetActiveConfig().Enabled);
        Assert.Equal(9.25f, manager.GetActiveConfig().Cling);
        Assert.Equal(FrenRiderProfileAcceptancePolicy.Temporary, manager.GetActiveConfig().ProfileAcceptancePolicy);
        Assert.Same(persisted, account.Characters[ActiveCharacterKey]);
        Assert.Equal("Local Fren", persisted.FrenName);
        Assert.False(persisted.Enabled);

        foreach (var wrong in new[]
                 {
                     ReleaseRequest(ownerId: "wrong-owner"),
                     ReleaseRequest(islandId: "wrong-island"),
                     ReleaseRequest(characterId: "wrong-character"),
                     ReleaseRequest(proposalId: "wrong-proposal"),
                 })
        {
            var rejected = ReadResult(service.ReleaseTemporaryProfile(wrong));
            Assert.Equal("overlay-not-owned", rejected.Code);
            Assert.Equal("none", rejected.Outcome);
            Assert.True(manager.HasTemporaryProfile);
            Assert.Equal("Incoming EXACT value", manager.GetActiveConfig().FrenName);
        }

        var released = ReadResult(service.ReleaseTemporaryProfile(ReleaseRequest()));
        Assert.Equal("ok", released.Code);
        Assert.Equal("released", released.Outcome);
        Assert.False(manager.HasTemporaryProfile);
        Assert.Same(persisted, manager.GetActiveConfig());
    }

    [Fact]
    public void CharacterTransitionReleaseReportsTheEffectiveEnabledChange()
    {
        var overlay = new TemporaryProfileOverlay();
        var identity = new DadProfileIdentity("owner", "island", "opaque", "proposal");
        var local = new CharacterConfig { Enabled = false };
        Assert.True(overlay.TryInstall(
            identity,
            "account",
            ActiveCharacterKey,
            new CharacterConfig { Enabled = true }));

        Assert.True(overlay.TryReleaseForActiveCharacter(
            "account",
            ActiveCharacterKey,
            local,
            out var previousEnabled,
            out var currentEnabled));
        Assert.True(previousEnabled);
        Assert.False(currentEnabled);
        Assert.False(overlay.IsInstalled);
    }

    [Fact]
    public void PermanentApplyPreservesLocalPolicyAndSavesExactlyOnce()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Permanent);
        var saveCount = 0;
        var manager = new TestProfileStore(account, ActiveCharacterKey, _ =>
        {
            saveCount++;
            return true;
        });
        using var service = new DadProfileTransferService(manager);
        var incoming = Export(new CharacterConfig
        {
            FrenName = "verbatim target @VALUE",
            Enabled = true,
            Cling = 7.75f,
            ProfileAcceptancePolicy = FrenRiderProfileAcceptancePolicy.Off,
        });

        var result = ReadResult(service.ApplyProfile(ApplyRequest(incoming)));

        Assert.Equal("ok", result.Code);
        Assert.Equal("permanent-applied", result.Outcome);
        Assert.Equal(1, saveCount);
        var saved = account.Characters[ActiveCharacterKey];
        Assert.Equal("verbatim target @VALUE", saved.FrenName);
        Assert.True(saved.Enabled);
        Assert.Equal(7.75f, saved.Cling);
        Assert.Equal(FrenRiderProfileAcceptancePolicy.Permanent, saved.ProfileAcceptancePolicy);
        Assert.False(manager.HasTemporaryProfile);
    }

    [Fact]
    public void PermanentSaveFailureRollsBackTheExactLocalRow()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Permanent);
        var original = account.Characters[ActiveCharacterKey];
        var saveCount = 0;
        var manager = new TestProfileStore(account, ActiveCharacterKey, _ =>
        {
            saveCount++;
            return false;
        });
        using var service = new DadProfileTransferService(manager);

        var result = ReadResult(service.ApplyProfile(ApplyRequest(Export(new CharacterConfig
        {
            FrenName = "Must Roll Back",
            Enabled = true,
        }))));

        Assert.Equal("save-failed", result.Code);
        Assert.Equal("none", result.Outcome);
        Assert.Equal(1, saveCount);
        Assert.Same(original, account.Characters[ActiveCharacterKey]);
        Assert.Equal("Local Fren", original.FrenName);
        Assert.False(original.Enabled);
    }

    [Fact]
    public void OptOutReturnsLocalOutcomeWithoutParsingOrMutation()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Off);
        var original = account.Characters[ActiveCharacterKey];
        var saveCount = 0;
        var manager = new TestProfileStore(account, ActiveCharacterKey, _ =>
        {
            saveCount++;
            return true;
        });
        using var service = new DadProfileTransferService(manager);

        var result = ReadResult(service.ApplyProfile(ApplyRequest("not profile json")));

        Assert.Equal("ok", result.Code);
        Assert.Equal("opted-out", result.Outcome);
        Assert.Equal(0, saveCount);
        Assert.Same(original, account.Characters[ActiveCharacterKey]);
        Assert.False(manager.HasTemporaryProfile);
    }

    [Fact]
    public void MalformedIncompatibleAndOversizedProfilesAreRejectedWithoutMutation()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Temporary);
        var original = account.Characters[ActiveCharacterKey];
        var manager = new TestProfileStore(account, ActiveCharacterKey);
        using var service = new DadProfileTransferService(manager);
        var valid = Export(new CharacterConfig());
        using var validDocument = JsonDocument.Parse(valid);
        var configJson = validDocument.RootElement.GetProperty("config").GetRawText();
        var incompatible = $"{{\"version\":2,\"config\":{configJson}}}";
        var incomplete = "{\"version\":1,\"config\":{\"frenName\":\"partial\"}}";
        var oversized = new string('x', DadProfileTransferContract.MaximumProfileJsonBytes + 1);

        Assert.Equal("malformed-profile", ReadResult(service.ApplyProfile(ApplyRequest("{"))).Code);
        Assert.Equal("incompatible-profile", ReadResult(service.ApplyProfile(ApplyRequest(incompatible))).Code);
        Assert.Equal("incompatible-profile", ReadResult(service.ApplyProfile(ApplyRequest(incomplete))).Code);
        Assert.Equal("profile-too-large", ReadResult(service.ApplyProfile(ApplyRequest(oversized))).Code);
        Assert.False(manager.HasTemporaryProfile);
        Assert.Same(original, account.Characters[ActiveCharacterKey]);
    }

    [Fact]
    public void ServiceDisposeReleasesItsExactTemporaryOverlay()
    {
        var account = CreateAccount(FrenRiderProfileAcceptancePolicy.Temporary);
        var original = account.Characters[ActiveCharacterKey];
        var manager = new TestProfileStore(account, ActiveCharacterKey);
        var service = new DadProfileTransferService(manager);
        var result = ReadResult(service.ApplyProfile(ApplyRequest(Export(new CharacterConfig
        {
            FrenName = "Temporary",
            Enabled = true,
        }))));
        Assert.Equal("temporary-applied", result.Outcome);
        Assert.True(manager.HasTemporaryProfile);

        service.Dispose();
        service.Dispose();

        Assert.False(manager.HasTemporaryProfile);
        Assert.Same(original, manager.GetActiveConfig());
    }

    [Fact]
    public void TemporaryOverlayDoesNotResolveAcrossLocalContext()
    {
        var overlay = new TemporaryProfileOverlay();
        var identity = new DadProfileIdentity("owner", "sender-island", "character", "proposal");
        var config = new CharacterConfig { FrenName = "Temporary" };

        Assert.True(overlay.TryInstall(identity, "account-a", "Local A@World", config));
        Assert.Same(config, overlay.Resolve("account-a", "Local A@World"));
        Assert.Null(overlay.Resolve("account-b", "Local A@World"));
        Assert.Null(overlay.Resolve("account-a", "Local B@World"));
        Assert.True(overlay.TryRelease(identity, out var released));
        Assert.Same(config, released);
    }

    [Fact]
    public void ResolveCreatesFromDefaultSavesOnceAndReturnsOnlySuccessfulProfileJson()
    {
        var account = CreateAccount();
        account.DefaultConfig.FrenName = "Default Remote Fren";
        account.DefaultConfig.Enabled = true;
        var saveCount = 0;
        var manager = new TestProfileStore(account, ActiveCharacterKey, _ =>
        {
            saveCount++;
            return true;
        });
        using var service = new DadProfileTransferService(manager);

        var first = ReadResult(service.ResolveOrCreateProfile(ResolveRequest("Remote Display")));
        var second = ReadResult(service.ResolveOrCreateProfile(ResolveRequest("Changed Display")));

        Assert.Equal("ok", first.Code);
        Assert.Equal("exported", first.Outcome);
        Assert.NotNull(first.ProfileJson);
        Assert.Equal("ok", second.Code);
        Assert.Equal("exported", second.Outcome);
        Assert.NotNull(second.ProfileJson);
        Assert.Equal(1, saveCount);
        Assert.Single(account.RemoteProfiles);
        Assert.Equal("Remote Display", account.RemoteProfiles[0].DisplayLabel);
        Assert.Equal("Default Remote Fren", account.RemoteProfiles[0].Config.FrenName);
        Assert.True(account.RemoteProfiles[0].Config.Enabled);

        account.RemoteProfiles.Add(new RemoteProfileRow
        {
            OwnerId = "owner",
            IslandId = "island",
            CharacterId = "character",
        });
        var duplicate = ReadResult(service.ResolveOrCreateProfile(ResolveRequest()));
        Assert.Equal("duplicate-remote-profile", duplicate.Code);
        Assert.Equal("none", duplicate.Outcome);
        Assert.Null(duplicate.ProfileJson);
    }

    [Fact]
    public void OversizedRemoteExportReturnsNoProfileJson()
    {
        var account = CreateAccount();
        account.DefaultConfig.InviteWhitelist = new List<string>
        {
            new('x', DadProfileTransferContract.MaximumProfileJsonBytes),
        };
        var manager = new TestProfileStore(account, ActiveCharacterKey);
        using var service = new DadProfileTransferService(manager);

        var result = ReadResult(service.ResolveOrCreateProfile(ResolveRequest()));

        Assert.Equal("profile-too-large", result.Code);
        Assert.Equal("none", result.Outcome);
        Assert.Null(result.ProfileJson);
    }

    [Fact]
    public void ProfileEndpointsAndLegacyEndpointRemainStable()
    {
        Assert.Equal("FrenRider.Dad.ConfigureAndEnable", DadIPC.ConfigureAndEnableEndpoint);
        Assert.Equal("FrenRider.Dad.ResolveOrCreateProfile", DadProfileTransferContract.ResolveOrCreateProfileEndpoint);
        Assert.Equal("FrenRider.Dad.ApplyProfile", DadProfileTransferContract.ApplyProfileEndpoint);
        Assert.Equal("FrenRider.Dad.ReleaseTemporaryProfile", DadProfileTransferContract.ReleaseTemporaryProfileEndpoint);
        Assert.Equal(1, DadProfileTransferContract.Version);
        Assert.Equal(8192, DadProfileTransferContract.MaximumProfileJsonBytes);
    }

    [Fact]
    public void ProfileEndpointRegistrationAndCleanupAreIdempotent()
    {
        Func<string, string>? resolve = null;
        Func<string, string>? apply = null;
        Func<string, string>? release = null;
        var unregisters = new int[3];
        var endpoint = new DadProfileIpcEndpoint(
            handler => resolve = handler, () => unregisters[0]++,
            handler => apply = handler, () => unregisters[1]++,
            handler => release = handler, () => unregisters[2]++,
            request => $"resolve:{request}",
            request => $"apply:{request}",
            request => $"release:{request}");

        Assert.Equal("resolve:x", resolve!("x"));
        Assert.Equal("apply:y", apply!("y"));
        Assert.Equal("release:z", release!("z"));
        endpoint.Dispose();
        endpoint.Dispose();
        Assert.Equal(new[] { 1, 1, 1 }, unregisters);
    }

    private static AccountConfig CreateAccount(
        FrenRiderProfileAcceptancePolicy policy = FrenRiderProfileAcceptancePolicy.Temporary)
        => new()
        {
            AccountId = "local-account",
            DefaultConfig = new CharacterConfig(),
            Characters = new Dictionary<string, CharacterConfig>
            {
                [ActiveCharacterKey] = new()
                {
                    FrenName = "Local Fren",
                    Enabled = false,
                    ProfileAcceptancePolicy = policy,
                },
            },
        };

    private static string Export(CharacterConfig config)
    {
        Assert.True(DadProfileTransferService.TrySerializeProfile(config, out var json, out var code));
        Assert.Equal("ok", code);
        return json;
    }

    private static string ResolveRequest(
        string displayLabel = "Remote",
        string ownerId = "owner",
        string islandId = "island",
        string characterId = "character",
        string proposalId = "proposal")
        => JsonSerializer.Serialize(new
        {
            version = DadProfileTransferContract.Version,
            ownerId,
            islandId,
            characterId,
            proposalId,
            displayLabel,
        });

    private static string ApplyRequest(
        string profileJson,
        string ownerId = "owner",
        string islandId = "island",
        string characterId = "character",
        string proposalId = "proposal")
        => JsonSerializer.Serialize(new
        {
            version = DadProfileTransferContract.Version,
            ownerId,
            islandId,
            characterId,
            proposalId,
            profileJson,
        });

    private static string ReleaseRequest(
        string ownerId = "owner",
        string islandId = "island",
        string characterId = "character",
        string proposalId = "proposal")
        => JsonSerializer.Serialize(new
        {
            version = DadProfileTransferContract.Version,
            ownerId,
            islandId,
            characterId,
            proposalId,
        });

    private static DadProfileResult ReadResult(string json)
        => JsonSerializer.Deserialize<DadProfileResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        })!;

    private sealed class TestProfileStore : IDadProfileStore
    {
        private readonly AccountConfig account;
        private readonly string activeCharacterKey;
        private readonly Func<AccountConfig, bool> persist;
        private readonly TemporaryProfileOverlay overlay = new();

        internal TestProfileStore(
            AccountConfig account,
            string activeCharacterKey,
            Func<AccountConfig, bool>? persist = null)
        {
            this.account = account;
            this.activeCharacterKey = activeCharacterKey;
            this.persist = persist ?? (_ => true);
        }

        public bool HasTemporaryProfile => overlay.IsInstalled;

        internal CharacterConfig GetActiveConfig()
            => overlay.Resolve(account.AccountId, activeCharacterKey)
               ?? account.Characters[activeCharacterKey];

        public bool TryResolveOrCreateRemoteProfile(
            string ownerId,
            string islandId,
            string characterId,
            string displayLabel,
            out RemoteProfileRow? row,
            out string code)
        {
            if (!ConfigManager.TryResolveOrCreateRemoteProfile(
                    account,
                    ownerId,
                    islandId,
                    characterId,
                    displayLabel,
                    out row,
                    out var added,
                    out code))
            {
                return false;
            }

            if (!added || persist(account))
                return true;

            account.RemoteProfiles.Remove(row!);
            row = null;
            code = "save-failed";
            return false;
        }

        public bool TryGetLocalActiveConfig(out CharacterConfig? activeConfig)
            => account.Characters.TryGetValue(activeCharacterKey, out activeConfig);

        public bool TryInstallTemporaryProfile(DadProfileIdentity identity, CharacterConfig config)
        {
            if (!TryGetLocalActiveConfig(out var local) || local == null)
                return false;

            config.ProfileAcceptancePolicy = local.ProfileAcceptancePolicy;
            return overlay.TryInstall(identity, account.AccountId, activeCharacterKey, config);
        }

        public bool TryReleaseTemporaryProfile(DadProfileIdentity identity)
            => overlay.TryRelease(identity, out _);

        public bool TryReplaceActiveProfilePermanently(CharacterConfig incoming)
            => !overlay.IsInstalled
               && ConfigManager.TryReplaceActiveProfilePermanently(
                   account,
                   activeCharacterKey,
                   incoming,
                   () => persist(account),
                   out _,
                   out _);

        public void ReleaseTemporaryProfileOnUnload()
        {
            if (overlay.Identity is { } identity)
                overlay.TryRelease(identity, out _);
        }
    }
}
