using System;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.BrowserBookmark.Tabs;

public static class SafeAsyncHelper
{
    /// <summary>
    /// Executes an async function safely from a sync method.
    /// Keeps the UI responsive (runs on thread pool).
    /// </summary>
    public static T RunSync<T>(Func<Task<T>> asyncFunc)
    {
        if (asyncFunc == null) throw new ArgumentNullException(nameof(asyncFunc));
        return Task.Run(async () => await asyncFunc().ConfigureAwait(false))
                   .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes an async action safely from a sync method.
    /// </summary>
    public static void RunSync(Func<Task> asyncAction)
    {
        if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));
        Task.Run(async () => await asyncAction().ConfigureAwait(false))
            .GetAwaiter().GetResult();
    }
}
