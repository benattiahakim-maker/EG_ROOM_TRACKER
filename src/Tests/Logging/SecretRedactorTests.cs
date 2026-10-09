using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Logging;

public class SecretRedactorTests
{
    [Theory]
    [InlineData("accessToken")]
    [InlineData("refresh_token")]
    [InlineData("PASSWORD")]
    [InlineData("client-secret")]
    [InlineData("apiKey")]
    [InlineData("Authorization")]
    [InlineData("X-Amz-Signature")]
    public void IsSensitiveKey_DetectsSecrets(string key) => Assert.True(SecretRedactor.IsSensitiveKey(key));

    [Theory]
    [InlineData("designOption")]
    [InlineData("roomsCollected")]
    [InlineData("errorCode")]
    [InlineData("documentPath")]
    public void IsSensitiveKey_KeepsBusinessKeys(string key) => Assert.False(SecretRedactor.IsSensitiveKey(key));

    [Theory]
    [InlineData("https://h/x.zip?sig=abc", "https://h/x.zip?***")]
    [InlineData("Authorization: Bearer abc.def-ghi", "Authorization: Bearer ***")]
    [InlineData("Bearer abc.def-ghi", "Bearer ***")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "Authorization: Basic ***")]
    [InlineData("en-tête Basic dXNlcjpwYXNzd29yZDEyMw== refusé", "en-tête Basic *** refusé")]
    [InlineData("Type de mur : Basic Wall", "Type de mur : Basic Wall")]
    [InlineData("Basic Wall - Generic 200mm", "Basic Wall - Generic 200mm")]
    [InlineData("{\"password\": \"p@ss\"}", "{\"password\": \"***\"}")]
    [InlineData("client_secret=xyz&grant_type=client_credentials", "client_secret=***&grant_type=client_credentials")]
    [InlineData("Projet/EG_Batiment_A.rvt", "Projet/EG_Batiment_A.rvt")]
    public void Redact_MasksSecretsInText(string input, string expected) =>
        Assert.Equal(expected, SecretRedactor.Redact(input));
}
