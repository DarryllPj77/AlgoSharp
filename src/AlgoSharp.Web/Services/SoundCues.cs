using Microsoft.JSInterop;

namespace AlgoSharp.Web.Services;

/// <summary>Plays the short Run / finished / failed sound cues (wwwroot/js/sound.js). Never throws: sound is a nicety.</summary>
public sealed class SoundCues(IJSRuntime js) : IAsyncDisposable
{
    public const string Start = "start", Success = "success", Done = "done", Fail = "fail";

    private IJSObjectReference? module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/sound.js");

    public async Task PlayAsync(string cue)
    {
        try { await (await ModuleAsync()).InvokeVoidAsync("play", cue); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { } // prerendering: no JS yet
    }

    public async Task<bool> IsMutedAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("isMuted"); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return false; }
    }

    public async Task<bool> ToggleMutedAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("toggleMuted"); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); } catch (JSDisconnectedException) { }
    }
}
