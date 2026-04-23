using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Scoped per SignalR circuit (= per browser tab).
/// Persists the JWT in localStorage so a page refresh restores the session.
///
/// Call InitializeAsync() in OnAfterRenderAsync(firstRender: true) on every
/// page/layout that needs auth state — JS interop is only available after
/// the client-side Blazor circuit is established.
/// </summary>
public class AuthService
{
    private const string StorageKey = "retailsuite_token";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private bool _initialized;

    public AuthService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js   = js;
    }

    // -----------------------------------------------------------------------
    // Public state
    // -----------------------------------------------------------------------

    public string? Token { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>Role string decoded from the JWT payload ("SuperAdmin", "Admin", "Staff", "Customer").</summary>
    public string Role => GetClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                       ?? GetClaim("role")
                       ?? string.Empty;

    public bool IsSuperAdmin => Role == "SuperAdmin";

    public event Action? OnChange;

    // -----------------------------------------------------------------------
    // Initialization — call once in OnAfterRenderAsync(firstRender: true)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads the stored token from localStorage and restores the auth state.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(stored) && !IsTokenExpired(stored))
            {
                ApplyToken(stored);
                // Don't notify — caller will re-render after this returns
            }
            else if (!string.IsNullOrEmpty(stored))
            {
                // Token exists but is expired — clean it up
                await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            }
        }
        catch
        {
            // JS not yet available (SSR pass) — silently ignore
        }
    }

    // -----------------------------------------------------------------------
    // Login
    // -----------------------------------------------------------------------

    public async Task<(bool Success, string? Error)> LoginAsync(string? email, string? password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password }, _jsonOpts);
            var response = await _http.PostAsync(
                "api/auth/login",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var content = await response.Content.ReadAsStringAsync();
            var result  = JsonSerializer.Deserialize<ApiWrapper<string>>(content, _jsonOpts);

            if (!response.IsSuccessStatusCode || result?.Data == null)
                return (false, result?.Message ?? "Invalid credentials.");

            await PersistTokenAsync(result.Data);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Logout
    // -----------------------------------------------------------------------

    public async Task LogoutAsync()
    {
        Token = null;
        _http.DefaultRequestHeaders.Authorization = null;

        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch { /* ignore if JS unavailable */ }

        OnChange?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>Writes token to memory + localStorage and sets the HttpClient header.</summary>
    private async Task PersistTokenAsync(string token)
    {
        ApplyToken(token);

        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, token);
        }
        catch { /* ignore */ }

        OnChange?.Invoke();
    }

    /// <summary>Sets in-memory token and HttpClient Authorization header only.</summary>
    private void ApplyToken(string token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Returns true if the JWT's "exp" claim is in the past.
    /// Treats unparseable tokens as expired so they get cleaned up.
    /// </summary>
    private static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return true;

            var padded  = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var json    = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("exp", out var expEl)) return false;
            var exp = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
            return exp <= DateTimeOffset.UtcNow;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Decodes a single claim from the JWT payload (no signature check — UI only).</summary>
    private string? GetClaim(string claimType)
    {
        if (string.IsNullOrEmpty(Token)) return null;
        try
        {
            var parts = Token.Split('.');
            if (parts.Length < 2) return null;

            var padded = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var json   = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.TryGetProperty(claimType, out var val)
                ? val.GetString()
                : null;
        }
        catch { return null; }
    }

    // Local wrapper — mirrors ApiResponse<T>
    private class ApiWrapper<T>
    {
        public bool    Success { get; set; }
        public string? Message { get; set; }
        public T?      Data    { get; set; }
    }
}
