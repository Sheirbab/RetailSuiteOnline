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
    public bool IsAdmin      => Role == "Admin" || IsSuperAdmin;

    /// <summary>True when the server's login response flagged that the user must change password.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>Permission codes loaded from /api/me — used to gate nav links and pages.</summary>
    public HashSet<string> Permissions { get; private set; } = new();

    /// <summary>The current tenant's Subdomain (used as its storefront slug), loaded from /api/me.</summary>
    public string? TenantSubdomain { get; private set; }

    /// <summary>Where to send the user right after login / password change / hitting "/".</summary>
    public string LandingUrl =>
        IsSuperAdmin
            ? "/admin/tenants"
            : !string.IsNullOrEmpty(TenantSubdomain)
                ? $"/{TenantSubdomain}/admin"
                : "/login";

    /// <summary>True if the user has the given permission. Admins have everything implicitly.</summary>
    public bool Can(string permissionCode) => IsAdmin || Permissions.Contains(permissionCode);

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
                // Refresh permissions from the server so nav has fresh state on reload.
                await LoadPermissionsAsync();
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
            using var doc = JsonDocument.Parse(content);

            if (!response.IsSuccessStatusCode)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "Invalid credentials.";
                return (false, msg);
            }

            // Server now returns either { data: "<token>" } (legacy shape) or
            // { data: { token, mustChangePassword } } (new shape).
            string? token = null;
            var mustChange = false;
            if (doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.ValueKind == JsonValueKind.String)
                {
                    token = dataEl.GetString();
                }
                else if (dataEl.ValueKind == JsonValueKind.Object)
                {
                    token = dataEl.TryGetProperty("token", out var t) ? t.GetString() : null;
                    mustChange = dataEl.TryGetProperty("mustChangePassword", out var mc) && mc.GetBoolean();
                }
            }

            if (string.IsNullOrEmpty(token))
                return (false, "Login response missing token.");

            MustChangePassword = mustChange;
            await PersistTokenAsync(token);

            // Fetch user's permission set so nav can render correctly right after login.
            await LoadPermissionsAsync();

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    /// <summary>Refresh the in-memory permission set from /api/me.</summary>
    public async Task LoadPermissionsAsync()
    {
        if (!IsAuthenticated) return;
        try
        {
            var resp = await _http.GetAsync("api/me");
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var d = doc.RootElement.GetProperty("data");

            MustChangePassword = d.TryGetProperty("mustChangePassword", out var mc) && mc.GetBoolean();
            TenantSubdomain = d.TryGetProperty("tenantSubdomain", out var ts) && ts.ValueKind == JsonValueKind.String
                ? ts.GetString()
                : null;

            Permissions = new();
            if (d.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in perms.EnumerateArray())
                {
                    var code = p.GetString();
                    if (!string.IsNullOrEmpty(code)) Permissions.Add(code);
                }
            }
            OnChange?.Invoke();
        }
        catch { /* network — leave permissions empty, user just sees minimal nav */ }
    }

    // -----------------------------------------------------------------------
    // Logout
    // -----------------------------------------------------------------------

    public async Task LogoutAsync()
    {
        Token = null;
        Permissions = new();
        MustChangePassword = false;
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
