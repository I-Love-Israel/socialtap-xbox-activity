using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocialTapXboxActivity;

/// <summary>
/// SocialTap sign-in (OAuth authorization code + PKCE S256, opened in the default browser)
/// and idle reporting. The app never sees a SocialTap password.
/// </summary>
internal sealed class SocialTapClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Config.BaseUrl),
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SocialTapXboxActivity",
        "session.bin");

    private string? _accessToken;

    public SocialTapClient()
    {
        LoadSession();
    }

    public bool IsSignedIn => !string.IsNullOrEmpty(_accessToken);

    /// <summary>Opens SocialTap's approval page and waits for the loopback redirect (max 5 minutes).</summary>
    public async Task SignInAsync()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));   // 43 characters
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));      // 43 characters
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorizeUrl = Config.BaseUrl + "/api/oauth/authorize?" + string.Join("&",
            Pair("response_type", "code"),
            Pair("client_id", Config.ClientId),
            Pair("redirect_uri", Config.RedirectUri),
            Pair("scope", string.Join(' ', Config.Scopes)),
            Pair("state", state),
            Pair("code_challenge", challenge),
            Pair("code_challenge_method", "S256"));

        var listener = new TcpListener(IPAddress.Loopback, Config.CallbackPort);
        listener.Start();
        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            Dictionary<string, string> callback;
            try
            {
                callback = await WaitForCallbackAsync(listener, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Sign-in timed out. Please try again.");
            }

            if (callback.TryGetValue("error", out var error))
            {
                throw new InvalidOperationException($"SocialTap did not approve access ({error}).");
            }

            if (!callback.TryGetValue("state", out var returnedState) || returnedState != state)
            {
                throw new InvalidOperationException("Sign-in state did not match. Please try again.");
            }

            if (!callback.TryGetValue("code", out var code))
            {
                throw new InvalidOperationException("SocialTap did not return an authorization code.");
            }

            await ExchangeCodeAsync(code, verifier);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Returns null on success, or a message to show the user.</summary>
    public async Task<string?> ReportIdleAsync(int idleSeconds)
    {
        if (!IsSignedIn)
        {
            return "Not signed in.";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/presence")
            {
                Content = JsonContent.Create(new { idle_seconds = idleSeconds }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearSession();
                return "Your SocialTap session expired. Sign in again.";
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return "SocialTap refused the presence update (presence.write scope missing).";
            }

            return response.IsSuccessStatusCode
                ? null
                : $"Presence update failed ({(int)response.StatusCode}).";
        }
        catch (HttpRequestException)
        {
            return "Could not reach SocialTap. Will retry.";
        }
        catch (TaskCanceledException)
        {
            return "SocialTap did not respond. Will retry.";
        }
    }

    /// <summary>Revokes the authorization on SocialTap (best effort) and forgets the local session.</summary>
    public async Task SignOutAsync()
    {
        if (_accessToken is not null)
        {
            try
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = Config.ClientId,
                    ["token"] = _accessToken,
                });
                using var _ = await Http.PostAsync("/api/oauth/revoke", form);
            }
            catch (Exception)
            {
                // Still clear the local session below.
            }
        }

        ClearSession();
    }

    private async Task ExchangeCodeAsync(string code, string verifier)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = Config.ClientId,
            ["code"] = code,
            ["redirect_uri"] = Config.RedirectUri,
            ["code_verifier"] = verifier,
        });

        using var response = await Http.PostAsync("/api/oauth/token", form);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token exchange failed ({(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        SaveSession();
    }

    /// <summary>Accepts browser connections on the loopback port until the /callback request arrives.</summary>
    private static async Task<Dictionary<string, string>> WaitForCallbackAsync(TcpListener listener, CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 8192, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(token) ?? string.Empty;
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(token)))
            {
                // Drain request headers.
            }

            var parts = requestLine.Split(' ');
            var target = parts.Length > 1 ? parts[1] : "/";
            var queryStart = target.IndexOf('?');
            var path = queryStart >= 0 ? target[..queryStart] : target;

            if (!path.Equals("/callback", StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, 404, "<!doctype html><title>Not found</title>");
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (queryStart >= 0)
            {
                foreach (var pair in target[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    var key = Uri.UnescapeDataString(kv[0].Replace('+', ' '));
                    var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
                    values[key] = value;
                }
            }

            await WriteResponseAsync(stream, 200,
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>SocialTap Xbox Activity</title></head>" +
                "<body style=\"font-family:Segoe UI,sans-serif;background:#111;color:#fff;text-align:center;padding:60px\">" +
                "<h2>SocialTap Xbox Activity is connected.</h2><p>You can close this tab.</p></body></html>");
            return values;
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var reason = status == 200 ? "OK" : "Not Found";
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
    }

    private void SaveSession()
    {
        var directory = Path.GetDirectoryName(SessionPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.SerializeToUtf8Bytes(new { access_token = _accessToken });
        // DPAPI: only this Windows user account can decrypt the saved token.
        File.WriteAllBytes(SessionPath, ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser));
    }

    private void LoadSession()
    {
        try
        {
            if (!File.Exists(SessionPath))
            {
                return;
            }

            var json = ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser);
            using var doc = JsonDocument.Parse(json);
            _accessToken = doc.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        }
        catch
        {
            ClearSession();
        }
    }

    private void ClearSession()
    {
        _accessToken = null;
        try
        {
            if (File.Exists(SessionPath))
            {
                File.Delete(SessionPath);
            }
        }
        catch
        {
            // Ignore: the token is already cleared in memory.
        }
    }

    private static string Pair(string key, string value) => $"{key}={Uri.EscapeDataString(value)}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
