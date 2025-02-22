using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Auth.Models;

public class FirebaseDecodedToken
{
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("picture")]
    public string Picture { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; }
}

public class FirebaseAuthResponse
{
    [JsonPropertyName("localId")]
    public string LocalId { get; set; }

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("fullName")]
    public string FullName { get; set; }

    [JsonPropertyName("photoUrl")]
    public string PhotoUrl { get; set; }

    [JsonPropertyName("idToken")]
    public string IdToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public string ExpiresIn { get; set; }

    [JsonIgnore]
    public string Initials => !string.IsNullOrEmpty(FullName)
        ? string.Join("", FullName.Split(' ').Select(n => n.Length > 0 ? $"{n[0]}." : ""))
        : string.Empty;

    [JsonIgnore]
    public string HiddenEmail => Email is not null && Email.Length > 4
        ? string.Concat(Email.AsSpan(0, 2), new string('*', Email.Length - 4), Email.AsSpan(Email.Length - 2))
        : string.Empty;
}

