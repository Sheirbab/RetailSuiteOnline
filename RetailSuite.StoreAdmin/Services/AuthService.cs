using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public class AuthService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);
    public string? Token { get; private set; }

    /// <summary>
    /// Role decoded from the JWT payload (e.g. "SuperAdmin", "Admin", "Staff", "Customer").
    /// Returns empty string when not authenticated.
    /// </summary>
    public string Role => GetClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                       ?? GetClaim("role")
                       ?? string.Empty;

    public bool IsSuperAdmin => Role == "SuperAdmin";

    public event Action? OnChange;

    /// <summary>
    /// Decodes a claim value from the stored JWT token without signature verification.
    /// Safe for UI display purposes only — server always re-validates the signature.
    /// </summary>
    private string? GetClaim(string claimType)
    {
        if (string.IsNullOrEmpty(Token)) return null;
        try
        {
            var parts = Token.Split('.');
            if (parts.Length < 2) return null;

            // JWT payload is base64url encoded
            var payload = parts[1];
            // Pad to multiple of 4
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(claimType, out var val))
                return val.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Stores the token and sets the Authorization header on the shared HttpClient.</summary>
    public void SetToken(string token)
    {
        Token = token;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        NotifyStateChanged();
    }

    public void Logout()
    {
        Token = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    /// <summary>Calls the API login endpoint and returns (success, errorMessage).</summary>
    public async Task<(bool Success, string? Error)> LoginAsync(string? email, string? password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password }, _jsonOptions);

            var response = await _httpClient.PostAsync(
                "api/auth/login",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var content = await response.Content.ReadAsStringAsync();
            var result  = JsonSerializer.Deserialize<ApiResponse<string>>(content, _jsonOptions);

            if (!response.IsSuccessStatusCode || result?.Data == null)
                return (false, result?.Message ?? "Invalid credentials.");

            SetToken(result.Data);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    // Local DTO — mirrors the shared ApiResponse<T> shape
    private class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }
}
