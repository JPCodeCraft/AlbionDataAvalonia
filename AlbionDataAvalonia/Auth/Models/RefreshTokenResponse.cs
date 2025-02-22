using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Auth.Models;

public class RefreshTokenResponse
{
    [JsonPropertyName("expires_in")]
    public string ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonPropertyName("id_token")]
    public string IdToken { get; set; }

    [JsonPropertyName("user_id")]
    public string UserId { get; set; }

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; }

    [JsonPropertyName("decodedToken")]
    public FirebaseDecodedToken FirebaseDecodedToken { get; set; }
}
