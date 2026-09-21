using System.Reflection;
using System.Runtime.Versioning;
using HaloPixelToolBox.Client.Utilities;
using HaloPixelToolBox.Core.Utilities;

namespace HaloPixelToolBox.Client.Test;

/// <summary>A console runner that also works with redirected output and no attached HID.</summary>
[SupportedOSPlatform("windows")]
internal static class RegressionTestRunner
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var count = 0;
            foreach (var method in typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (method.GetParameters().Length != 0 ||
                    !method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "SMTestAttribute"))
                    continue;
                if (method.Name == "TestMethod" && !args.Contains("--hardware"))
                    continue;
                var result = method.Invoke(null, null);
                if (result is Task task) await task;
                Console.WriteLine($"PASS {method.Name}");
                count++;
            }

            await StopCancelsAndDrainsWork();
            Console.WriteLine("PASS StopCancelsAndDrainsWork");
            await OwnershipSwitchWaitsForPreviousWrite();
            Console.WriteLine("PASS OwnershipSwitchWaitsForPreviousWrite");
            Console.WriteLine($"PASS {count + 2} checks; hardware checks are opt-in (--hardware).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException : ex);
            return 1;
        }
    }

    private static async Task StopCancelsAndDrainsWork()
    {
        await using var scope = new BackgroundTaskScope();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanedUp = false;
        scope.Run(async () =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, scope.Token); }
            finally
            {
                cancelled.SetResult();
                await release.Task;
                cleanedUp = true;
            }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = scope.StopAsync();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Ensure(!stop.IsCompleted, "Stop must wait for task cleanup.");
            Ensure(ReferenceEquals(stop, scope.StopAsync()), "Stopping must be idempotent.");
        }
        finally { release.TrySetResult(); }
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        var rejectedWorkRan = false;
        scope.Run(() => { rejectedWorkRan = true; return Task.CompletedTask; });
        await scope.StopAsync();
        Ensure(cleanedUp && !rejectedWorkRan, "Cleanup must complete and new work must be rejected.");
    }

    private static async Task OwnershipSwitchWaitsForPreviousWrite()
    {
        var oldGeneration = DeviceCoordinator.Activate(LyricSourceKind.CloudMusic);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var writing = false;
        var write = Task.Run(() => DeviceCoordinator.ExecuteIfOwner(LyricSourceKind.CloudMusic, oldGeneration, () =>
        {
            writing = true;
            entered.SetResult();
            Ensure(release.Wait(TimeSpan.FromSeconds(5)), "Test write timed out.");
            writing = false;
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var switching = Task.Run(() =>
        {
            var generation = DeviceCoordinator.Activate(LyricSourceKind.Spotify);
            Ensure(!writing, "Ownership changed while the previous write was in progress.");
            return generation;
        });
        release.Set();
        await write.WaitAsync(TimeSpan.FromSeconds(5));
        var currentGeneration = await switching.WaitAsync(TimeSpan.FromSeconds(5));
        Ensure(!DeviceCoordinator.ExecuteIfOwner(LyricSourceKind.CloudMusic, oldGeneration,
            () => throw new Exception("Stale source wrote to device.")), "Stale source retained ownership.");
        Ensure(!DeviceCoordinator.Deactivate(LyricSourceKind.CloudMusic, oldGeneration,
            () => throw new Exception("Stale source cleaned up the active session.")), "Stale cleanup was accepted.");
        var sameSourceGeneration = DeviceCoordinator.Activate(LyricSourceKind.Spotify);
        Ensure(!DeviceCoordinator.IsOwner(LyricSourceKind.Spotify, currentGeneration), "Old generation was reused.");
        DeviceCoordinator.BeginShutdown();
        Ensure(DeviceCoordinator.Activate(LyricSourceKind.CloudMusic) == 0, "Shutdown allowed activation.");
        Ensure(!DeviceCoordinator.ExecuteIfOwner(LyricSourceKind.Spotify, sameSourceGeneration,
            () => throw new Exception("Write after shutdown.")), "Shutdown allowed a write.");
        Ensure(DeviceCoordinator.Deactivate(LyricSourceKind.Spotify, sameSourceGeneration, () => { }),
            "Shutdown must still allow the owner's cleanup.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
