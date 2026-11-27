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

    /// <summary>The per-step playback sound (see <see cref="StepSounds"/>).</summary>
    public async Task StepAsync(string kind, double pitch)
    {
        try { await (await ModuleAsync()).InvokeVoidAsync("step", kind, Math.Round(pitch, 1)); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }

    public async Task<bool> IsMutedAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("isMuted"); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return false; }
    }

    public async Task<double> GetVolumeAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<double>("getVolume"); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return 0.6; }
    }

    public async Task<double> SetVolumeAsync(double volume)
    {
        try { return await (await ModuleAsync()).InvokeAsync<double>("setVolume", volume); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return volume; }
    }

    public async Task<bool> SetMutedAsync(bool muted)
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("setMuted", muted); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return muted; }
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
