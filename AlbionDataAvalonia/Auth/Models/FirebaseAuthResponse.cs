using System;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Auth.Models;

public class FirebaseAuthResponse
{
    [JsonPropertyName("federatedId")]
    public string FederatedId { get; set; }

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; }

    [JsonPropertyName("localId")]
    public string LocalId { get; set; }

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("oauthIdToken")]
    public string OauthIdToken { get; set; }

    [JsonPropertyName("oauthAccessToken")]
    public string OauthAccessToken { get; set; }

    [JsonPropertyName("oauthTokenSecret")]
    public string OauthTokenSecret { get; set; }

    [JsonPropertyName("rawUserInfo")]
    public string RawUserInfo { get; set; }

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string LastName { get; set; }

    [JsonPropertyName("fullName")]
    public string FullName { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    [JsonPropertyName("photoUrl")]
    public string PhotoUrl { get; set; }

    [JsonPropertyName("idToken")]
    public string IdToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public string ExpiresIn { get; set; }

    [JsonPropertyName("needConfirmation")]
    public bool NeedConfirmation { get; set; }

    [JsonIgnore]
    public string Initials => $"{FirstName?[0]}. {LastName?[0]}.";

    [JsonIgnore]
    public string HiddenEmail => Email is not null && Email.Length > 4
        ? string.Concat(Email.AsSpan(0, 2), new string('*', Email.Length - 4), Email.AsSpan(Email.Length - 2))
        : string.Empty;
}

