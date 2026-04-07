using RetailSuite.Shared;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class AuthService
{
    private readonly HttpClient _httpClient;
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);
    public string? Token { get; private set; }
    public event Action? OnChange;
    public void SetToken(string token)
    {
        Token = token;
    }

    public void Logout()
    {
        Token = null;
    }
    private void NotifyStateChanged() => OnChange?.Invoke();
    public async Task<bool> LoginAsync(string? email, string? password)
    {
        var response = await _httpClient.PostAsync(
            "api/auth/login",
            new StringContent(
                JsonSerializer.Serialize(new { email, password }),
                Encoding.UTF8,
                "application/json"));

        if (!response.IsSuccessStatusCode)
            return false;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<string>>(content);

        Token = result?.Data;

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token);

        return true;
    }
}