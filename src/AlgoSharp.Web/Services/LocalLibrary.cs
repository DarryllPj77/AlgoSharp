using System.Security.Claims;
using System.Text.Json;
using AlgoSharp.Playground.Library;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AlgoSharp.Web.Services;

/// <summary>Result of a storage call; failures carry a user-readable message.</summary>
public sealed record StoreResult<T>(bool Ok, T? Value, string? Error);

/// <summary>Who a library belongs to: the browser's guest library, or one signed-in account.</summary>
public sealed record LibraryOwner(string? UserId, string? UserName)
{
    public const string GuestDb = "algosharp";

    public static readonly LibraryOwner Guest = new(null, null);

    public bool IsGuest => UserId is null;

    /// <summary>The IndexedDB database holding this owner's library.</summary>
    public string DbName => IsGuest ? GuestDb : $"algosharp-u-{UserId}";

    /// <summary>The value stored on each problem's Owner field.</summary>
    public string Tag => IsGuest ? "local" : $"user:{UserId}";
}

/// <summary>
/// The user's personal problem library, stored in this browser's IndexedDB (see wwwroot/js/store.js).
/// It is not stored on the server. Signed-out visitors use the guest library; each signed-in account
/// gets its own database on this browser, so accounts sharing a browser don't see each other's
/// problems in AlgoSharp. The guest library is never modified or deleted when someone signs in;
/// <see cref="CopyGuestLibraryAsync"/> copies it into the account on request.
/// Must be called after the first render (JS interop isn't available while prerendering).
/// </summary>
public sealed class LocalLibrary(IJSRuntime js, AuthenticationStateProvider auth) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private LibraryOwner? owner;

    /// <summary>The owner of the library this circuit reads and writes (fixed for the circuit's lifetime).</summary>
    public async Task<LibraryOwner> OwnerAsync()
    {
        if (owner is not null) return owner;
        var user = (await auth.GetAuthenticationStateAsync()).User;
        string? id = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        return owner = id is null ? LibraryOwner.Guest : new LibraryOwner(id, user.Identity!.Name);
    }

    private sealed record JsResult(bool Ok, JsonElement? Value, string? Error);

    private async Task<IJSObjectReference> Module() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/store.js");

    private async Task<JsResult> Call(string fn, params object?[] args) => await CallOn((await OwnerAsync()).DbName, fn, args);

    private async Task<JsResult> CallOn(string db, string fn, params object?[] args)
    {
        try
        {
            return await (await Module()).InvokeAsync<JsResult>(fn, [db, .. args]);
        }
        catch (JSException ex)
        {
            return new JsResult(false, null, ex.Message);
        }
    }

    public async Task<StoreResult<List<UserProblem>>> ProblemsAsync() => await ProblemsIn((await OwnerAsync()).DbName);

    private async Task<StoreResult<List<UserProblem>>> ProblemsIn(string db)
    {
        var r = await CallOn(db, "getAll", "problems");
        if (!r.Ok) return new(false, null, r.Error);
        var list = new List<UserProblem>();
        foreach (var json in r.Value?.EnumerateArray() ?? default)
        {
            try
            {
                if (JsonSerializer.Deserialize<UserProblem>(json.GetString()!, LibraryJson.Options) is { } p) list.Add(Migrate(p));
            }
            catch (JsonException)
            {
                // A damaged record shouldn't hide the rest of the library.
            }
        }
        return new(true, list, null);
    }

    public async Task<StoreResult<UserProblem>> ProblemAsync(string id)
    {
        var r = await Call("get", "problems", id);
        if (!r.Ok) return new(false, null, r.Error);
        if (r.Value is not { ValueKind: JsonValueKind.String } v) return new(false, null, "This problem isn't in your library (it may have been deleted, or saved in another browser).");
        return new(true, Migrate(JsonSerializer.Deserialize<UserProblem>(v.GetString()!, LibraryJson.Options)!), null);
    }

    public async Task<StoreResult<bool>> SaveProblemAsync(UserProblem p)
    {
        var r = await Call("put", "problems", JsonSerializer.Serialize(p with { UpdatedAt = DateTimeOffset.UtcNow, Owner = (await OwnerAsync()).Tag }, LibraryJson.Options));
        return new(r.Ok, r.Ok, r.Error);
    }

    public async Task<StoreResult<bool>> SaveProblemsAsync(IEnumerable<UserProblem> problems, IEnumerable<ProblemProgress>? progress = null)
    {
        string tag = (await OwnerAsync()).Tag;
        var r = await Call("putMany", JsonSerializer.Serialize(problems.Select(p => p with { Owner = tag }).ToList(), LibraryJson.Options), JsonSerializer.Serialize((progress ?? []).ToList(), LibraryJson.Options));
        return new(r.Ok, r.Ok, r.Error);
    }

    public async Task<StoreResult<bool>> DeleteProblemAsync(string id)
    {
        var r = await Call("removeProblem", id);
        return new(r.Ok, r.Ok, r.Error);
    }

    public async Task<StoreResult<List<ProblemProgress>>> ProgressAsync() => await ProgressIn((await OwnerAsync()).DbName);

    private async Task<StoreResult<List<ProblemProgress>>> ProgressIn(string db)
    {
        var r = await CallOn(db, "getAll", "progress");
        if (!r.Ok) return new(false, null, r.Error);
        var list = (r.Value?.EnumerateArray() ?? default)
            .Select(j => JsonSerializer.Deserialize<ProblemProgress>(j.GetString()!, LibraryJson.Options)!)
            .ToList();
        return new(true, list, null);
    }

    public async Task<StoreResult<ProblemProgress?>> ProgressAsync(string problemId)
    {
        var r = await Call("get", "progress", problemId);
        if (!r.Ok) return new(false, null, r.Error);
        return new(true, r.Value is { ValueKind: JsonValueKind.String } v ? JsonSerializer.Deserialize<ProblemProgress>(v.GetString()!, LibraryJson.Options) : null, null);
    }

    public async Task<StoreResult<bool>> SaveProgressAsync(ProblemProgress progress)
    {
        var r = await Call("put", "progress", JsonSerializer.Serialize(progress with { UpdatedAt = DateTimeOffset.UtcNow }, LibraryJson.Options));
        return new(r.Ok, r.Ok, r.Error);
    }

    /// <summary>Exports everything as a JSON backup and starts a download.</summary>
    public async Task<StoreResult<int>> ExportAsync()
    {
        var problems = await ProblemsAsync();
        var progress = await ProgressAsync();
        if (!problems.Ok || !progress.Ok) return new(false, 0, problems.Error ?? progress.Error);
        var backup = new LibraryBackup { Problems = problems.Value!, Progress = progress.Value! };
        string json = JsonSerializer.Serialize(backup, new JsonSerializerOptions(LibraryJson.Options) { WriteIndented = true });
        var who = await OwnerAsync();
        string suffix = who.IsGuest ? "" : "-" + new string((who.UserName ?? "account").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        await (await Module()).InvokeVoidAsync("download", $"algosharp-library{suffix}-{DateTime.Now:yyyy-MM-dd}.json", json);
        return new(true, backup.Problems.Count, null);
    }

    /// <summary>Restores a backup: problems with the same id are replaced by the newer copy.</summary>
    public async Task<StoreResult<int>> RestoreAsync(string json)
    {
        LibraryBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<LibraryBackup>(json, LibraryJson.Options);
        }
        catch (JsonException ex)
        {
            return new(false, 0, $"That file isn't a valid AlgoSharp backup ({ex.Message.Split(" Path:")[0]}).");
        }
        if (backup is null || backup.Format != "algosharp/library-backup") return new(false, 0, "That file isn't an AlgoSharp library backup.");
        if (backup.Version > 1) return new(false, 0, $"This backup was made by a newer version of AlgoSharp (version {backup.Version}).");

        var existing = await ProblemsAsync();
        if (!existing.Ok) return new(false, 0, existing.Error);
        var byId = existing.Value!.ToDictionary(p => p.Id);
        var incoming = backup.Problems.Where(p => !byId.TryGetValue(p.Id, out var cur) || p.UpdatedAt >= cur.UpdatedAt).Select(Migrate).ToList();
        var ids = incoming.Select(p => p.Id).ToHashSet();
        var r = await SaveProblemsAsync(incoming, backup.Progress.Where(g => ids.Contains(g.ProblemId)));
        return new(r.Ok, incoming.Count, r.Error);
    }

    /// <summary>
    /// For a signed-in account: problems in this browser's guest library that the account's library
    /// doesn't have yet (by id). Empty for guests or when there's no guest library.
    /// </summary>
    public async Task<StoreResult<List<UserProblem>>> GuestOnlyProblemsAsync()
    {
        var who = await OwnerAsync();
        if (who.IsGuest) return new(true, [], null);
        var exists = await CallOn(LibraryOwner.GuestDb, "exists");
        if (!exists.Ok) return new(false, null, exists.Error);
        if (exists.Value is { ValueKind: JsonValueKind.False }) return new(true, [], null);
        var guest = await ProblemsIn(LibraryOwner.GuestDb);
        var mine = await ProblemsIn(who.DbName);
        if (!guest.Ok || !mine.Ok) return new(false, null, guest.Error ?? mine.Error);
        var have = mine.Value!.Select(p => p.Id).ToHashSet();
        return new(true, guest.Value!.Where(p => !have.Contains(p.Id)).ToList(), null);
    }

    /// <summary>
    /// Copies guest-library problems (and their progress and drafts) into the signed-in account's
    /// library. The guest library is left exactly as it was, and problems the account already has are
    /// never overwritten.
    /// </summary>
    public async Task<StoreResult<int>> CopyGuestLibraryAsync()
    {
        var who = await OwnerAsync();
        if (who.IsGuest) return new(false, 0, "Sign in to copy the guest library into an account.");
        var missing = await GuestOnlyProblemsAsync();
        if (!missing.Ok) return new(false, 0, missing.Error);
        if (missing.Value!.Count == 0) return new(true, 0, null);
        var guestProgress = await ProgressIn(LibraryOwner.GuestDb);
        if (!guestProgress.Ok) return new(false, 0, guestProgress.Error);
        var ids = missing.Value.Select(p => p.Id).ToHashSet();
        var r = await SaveProblemsAsync(missing.Value, guestProgress.Value!.Where(g => ids.Contains(g.ProblemId)));
        return new(r.Ok, r.Ok ? missing.Value.Count : 0, r.Error);
    }

    public async Task<string?> ReadFileAsync(Microsoft.AspNetCore.Components.ElementReference input)
    {
        try
        {
            return await (await Module()).InvokeAsync<string?>("readFile", input);
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public async Task<bool> CopyAsync(string text) => await (await Module()).InvokeAsync<bool>("copyText", text);

    /// <summary>A small per-library setting (stored in the library's prefs store).</summary>
    public async Task<string?> PrefAsync(string key)
    {
        var r = await Call("get", "prefs", key);
        if (!r.Ok || r.Value is not { ValueKind: JsonValueKind.String } v) return null;
        return JsonDocument.Parse(v.GetString()!).RootElement.TryGetProperty("value", out var val) ? val.GetString() : null;
    }

    public async Task SetPrefAsync(string key, string value) =>
        await Call("put", "prefs", JsonSerializer.Serialize(new { key, value }));

    public async Task<bool> RequestPersistenceAsync() => await (await Module()).InvokeAsync<bool>("persist");

    /// <summary>Upgrades records written by older schema versions (none yet beyond v1).</summary>
    private static UserProblem Migrate(UserProblem p) => p.Schema switch
    {
        _ => p with { Schema = UserProblem.CurrentSchema },
    };

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); } catch (JSDisconnectedException) { }
    }
}
