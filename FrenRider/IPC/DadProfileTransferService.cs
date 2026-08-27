using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.IPC;

public static class DadProfileTransferContract
{
    public const int Version = 1;
    public const int MaximumProfileJsonBytes = 8 * 1024;

    public const string ResolveOrCreateProfileEndpoint = "FrenRider.Dad.ResolveOrCreateProfile";
    public const string ApplyProfileEndpoint = "FrenRider.Dad.ApplyProfile";
    public const string ReleaseTemporaryProfileEndpoint = "FrenRider.Dad.ReleaseTemporaryProfile";
}

internal sealed class DadProfileTransferService : IDisposable
{
    private static readonly JsonSerializerOptions ContractJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly HashSet<string> ExpectedProfileProperties = typeof(CharacterConfig)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead
                           && property.CanWrite
                           && property.Name != nameof(CharacterConfig.ProfileAcceptancePolicy))
        .Select(property => ContractJsonOptions.PropertyNamingPolicy!.ConvertName(property.Name))
        .ToHashSet(StringComparer.Ordinal);

    private readonly IDadProfileStore profileStore;
    private bool disposed;

    internal DadProfileTransferService(IDadProfileStore profileStore)
    {
        this.profileStore = profileStore;
    }

    internal string ResolveOrCreateProfile(string requestJson)
    {
        try
        {
            if (!TryReadRequest<DadProfileResolveRequest>(requestJson, out var request, out var error))
                return Serialize(error);

            if (!profileStore.TryResolveOrCreateRemoteProfile(
                    request!.OwnerId,
                    request.IslandId,
                    request.CharacterId,
                    request.DisplayLabel,
                    out var row,
                    out var code))
            {
                return Serialize(Failure(code));
            }

            if (row?.Config == null)
                return Serialize(Failure("incompatible-profile"));

            if (!TrySerializeProfile(row.Config, out var profileJson, out code))
                return Serialize(Failure(code));

            return Serialize(Success("exported", profileJson));
        }
        catch
        {
            return Serialize(Failure("internal-error"));
        }
    }

    internal string ApplyProfile(string requestJson)
    {
        try
        {
            if (!TryReadRequest<DadProfileApplyRequest>(requestJson, out var request, out var error))
                return Serialize(error);

            if (!profileStore.TryGetLocalActiveConfig(out var localConfig) || localConfig == null)
                return Serialize(Failure("no-active-character"));

            if (!Enum.IsDefined(localConfig.ProfileAcceptancePolicy))
                return Serialize(Failure("invalid-acceptance-policy"));

            if (localConfig.ProfileAcceptancePolicy == FrenRiderProfileAcceptancePolicy.Off)
                return Serialize(Success("opted-out"));

            if (!TryDeserializeProfile(request!.ProfileJson!, out var incoming, out var code))
                return Serialize(Failure(code));

            incoming!.ProfileAcceptancePolicy = localConfig.ProfileAcceptancePolicy;
            var identity = ToIdentity(request);
            switch (localConfig.ProfileAcceptancePolicy)
            {
                case FrenRiderProfileAcceptancePolicy.Temporary:
                    return profileStore.TryInstallTemporaryProfile(identity, incoming)
                        ? Serialize(Success("temporary-applied"))
                        : Serialize(Failure("overlay-conflict"));

                case FrenRiderProfileAcceptancePolicy.Permanent:
                    if (profileStore.HasTemporaryProfile)
                        return Serialize(Failure("overlay-conflict"));

                    return profileStore.TryReplaceActiveProfilePermanently(incoming)
                        ? Serialize(Success("permanent-applied"))
                        : Serialize(Failure("save-failed"));

                default:
                    return Serialize(Failure("invalid-acceptance-policy"));
            }
        }
        catch
        {
            return Serialize(Failure("internal-error"));
        }
    }

    internal string ReleaseTemporaryProfile(string requestJson)
    {
        try
        {
            if (!TryReadRequest<DadProfileReleaseRequest>(requestJson, out var request, out var error))
                return Serialize(error);

            if (!profileStore.HasTemporaryProfile)
                return Serialize(Failure("overlay-not-found"));

            return profileStore.TryReleaseTemporaryProfile(ToIdentity(request!))
                ? Serialize(Success("released"))
                : Serialize(Failure("overlay-not-owned"));
        }
        catch
        {
            return Serialize(Failure("internal-error"));
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        profileStore.ReleaseTemporaryProfileOnUnload();
    }

    internal static bool TrySerializeProfile(CharacterConfig config, out string profileJson, out string code)
    {
        profileJson = "";
        code = "incompatible-profile";
        try
        {
            if (!IsValidProfile(config))
                return false;

            var configNode = JsonSerializer.SerializeToNode(config, ContractJsonOptions) as JsonObject;
            if (configNode == null || !configNode.Remove("profileAcceptancePolicy"))
                return false;

            var envelope = new JsonObject
            {
                ["version"] = DadProfileTransferContract.Version,
                ["config"] = configNode,
            };
            profileJson = envelope.ToJsonString(ContractJsonOptions);
            if (Encoding.UTF8.GetByteCount(profileJson) > DadProfileTransferContract.MaximumProfileJsonBytes)
            {
                profileJson = "";
                code = "profile-too-large";
                return false;
            }

            code = "ok";
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryDeserializeProfile(string profileJson, out CharacterConfig? config, out string code)
    {
        config = null;
        code = "malformed-profile";
        if (string.IsNullOrWhiteSpace(profileJson))
            return false;

        if (Encoding.UTF8.GetByteCount(profileJson) > DadProfileTransferContract.MaximumProfileJsonBytes)
        {
            code = "profile-too-large";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(profileJson, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var profileVersion)
                || !root.TryGetProperty("config", out var configElement)
                || configElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (profileVersion != DadProfileTransferContract.Version)
            {
                code = "incompatible-profile";
                return false;
            }

            var rootProperties = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (rootProperties.Length != 2
                || !rootProperties.Contains("version", StringComparer.Ordinal)
                || !rootProperties.Contains("config", StringComparer.Ordinal))
            {
                code = "incompatible-profile";
                return false;
            }

            var actualProperties = configElement.EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            if (actualProperties.Length != ExpectedProfileProperties.Count
                || actualProperties.Distinct(StringComparer.Ordinal).Count() != actualProperties.Length
                || !ExpectedProfileProperties.SetEquals(actualProperties))
            {
                code = "incompatible-profile";
                return false;
            }

            var envelope = JsonSerializer.Deserialize<DadProfileEnvelope>(profileJson, ContractJsonOptions);
            if (envelope?.Config == null || !IsValidProfile(envelope.Config))
            {
                code = "incompatible-profile";
                return false;
            }

            config = envelope.Config;
            code = "ok";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch
        {
            code = "incompatible-profile";
            return false;
        }
    }

    private static bool TryReadRequest<TRequest>(
        string requestJson,
        out TRequest? request,
        out DadProfileResult error)
        where TRequest : DadProfileIdentityRequest
    {
        request = null;
        error = Failure("malformed-request");
        if (string.IsNullOrWhiteSpace(requestJson))
            return false;

        try
        {
            request = JsonSerializer.Deserialize<TRequest>(requestJson, ContractJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request == null)
            return false;

        if (request.Version != DadProfileTransferContract.Version)
        {
            error = Failure("incompatible-contract");
            return false;
        }

        if (!IsValidIdentityPart(request.OwnerId)
            || !IsValidIdentityPart(request.IslandId)
            || !IsValidIdentityPart(request.CharacterId)
            || !IsValidIdentityPart(request.ProposalId))
        {
            error = Failure("invalid-request");
            return false;
        }

        if (request is DadProfileResolveRequest resolve
            && (resolve.DisplayLabel == null
                || resolve.DisplayLabel.Length > 256
                || resolve.DisplayLabel.Any(char.IsControl)))
        {
            error = Failure("invalid-request");
            return false;
        }

        if (request is DadProfileApplyRequest apply && apply.ProfileJson == null)
        {
            error = Failure("invalid-request");
            return false;
        }

        return true;
    }

    private static bool IsValidIdentityPart(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 256
           && string.Equals(value, value.Trim(), StringComparison.Ordinal)
           && !value.Any(char.IsControl);

    private static bool IsValidProfile(CharacterConfig config)
    {
        if (config.FrenName == null
            || config.FoolFlier == null
            || config.FulfType == null
            || config.CompanionStrat == null
            || config.IdleAction == null
            || config.CustomIdleList == null
            || config.AutoRotationType == null
            || config.AutoRotationTypeDD == null
            || config.AutoRotationTypeFATE == null
            || config.FeedMeItem == null
            || config.InviteWhitelist == null
            || config.CustomIdleList.Any(item => item == null)
            || config.InviteWhitelist.Any(item => item == null))
        {
            return false;
        }

        foreach (var property in typeof(CharacterConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name != nameof(CharacterConfig.ProfileAcceptancePolicy)
                && property.PropertyType.IsEnum
                && property.GetValue(config) is object value
                && !Enum.IsDefined(property.PropertyType, value))
            {
                return false;
            }
        }

        return true;
    }

    private static DadProfileIdentity ToIdentity(DadProfileIdentityRequest request)
        => new(request.OwnerId, request.IslandId, request.CharacterId, request.ProposalId);

    private static DadProfileResult Success(string outcome, string? profileJson = null)
        => new()
        {
            Version = DadProfileTransferContract.Version,
            Code = "ok",
            Outcome = outcome,
            ProfileJson = profileJson,
        };

    private static DadProfileResult Failure(string code)
        => new()
        {
            Version = DadProfileTransferContract.Version,
            Code = code,
            Outcome = "none",
        };

    private static string Serialize(DadProfileResult result)
        => JsonSerializer.Serialize(result, ContractJsonOptions);
}

internal abstract class DadProfileIdentityRequest
{
    public int Version { get; set; }
    public string OwnerId { get; set; } = "";
    public string IslandId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string ProposalId { get; set; } = "";
}

internal sealed class DadProfileResolveRequest : DadProfileIdentityRequest
{
    public string DisplayLabel { get; set; } = "";
}

internal sealed class DadProfileApplyRequest : DadProfileIdentityRequest
{
    public string? ProfileJson { get; set; }
}

internal sealed class DadProfileReleaseRequest : DadProfileIdentityRequest
{
}

internal sealed class DadProfileResult
{
    public int Version { get; set; }
    public string Code { get; set; } = "";
    public string Outcome { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileJson { get; set; }
}

internal sealed class DadProfileEnvelope
{
    public int Version { get; set; }
    public CharacterConfig? Config { get; set; }
}
