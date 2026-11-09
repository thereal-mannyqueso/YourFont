using FontBook.Core.Data;
using Microsoft.Extensions.Logging;

namespace FontBook.App.Services.Settings;

/// <summary>
/// Batches frequent setting changes (dragging the size slider, typing specimen text,
/// resizing panes) and writes only the final value after a short pause.
/// Call <see cref="FlushAsync"/> on exit so nothing is lost.
/// </summary>
public sealed class DeferredSettingsWriter(ISettingsStore store, ILogger<DeferredSettingsWriter> logger) : IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private CancellationTokenSource? _delay;

    public void Schedule(string key, string value)
    {
        CancellationToken token;
        lock (_gate)
        {
            _pending[key] = value;
            _delay?.Cancel();
            _delay?.Dispose();
            _delay = new CancellationTokenSource();
            token = _delay.Token;
        }

        _ = WriteAfterDelayAsync(token);
    }

    public async Task FlushAsync()
    {
        KeyValuePair<string, string>[] items;
        lock (_gate)
        {
            items = [.. _pending];
            _pending.Clear();
        }

        foreach (var (key, value) in items)
        {
            try
            {
                await store.SetAsync(key, value).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException or IOException or InvalidOperationException)
            {
                // A lost preference is not worth interrupting the user for; log it instead.
                logger.LogWarning(ex, "Could not save setting {Key}", key);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _delay?.Dispose();
            _delay = null;
        }
    }

    private async Task WriteAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // A newer change restarted the timer.
        }

        await FlushAsync().ConfigureAwait(false);
    }
}
