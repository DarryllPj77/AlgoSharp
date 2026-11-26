namespace AlgoSharp.Accounts;

public static class ReturnUrl
{
    /// <summary>
    /// Only same-site paths are allowed after sign-in ("/problems/mine"); anything else
    /// (absolute URLs, protocol-relative "//evil.com", backslash tricks) falls back to <paramref name="fallback"/>.
    /// </summary>
    public static string Safe(string? url, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(url)) return fallback;
        url = url.Trim();
        if (url[0] != '/') return fallback;
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return fallback;
        if (url.Any(c => char.IsControl(c) || c == '\\')) return fallback;
        if (url.Length > 2048) return fallback;
        return url;
    }
}
