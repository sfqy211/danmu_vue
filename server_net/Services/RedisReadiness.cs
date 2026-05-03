namespace Danmu.Server.Services;

public class RedisReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return _ready.Task.WaitAsync(cancellationToken);
    }

    public void MarkReady()
    {
        _ready.TrySetResult();
    }
}
