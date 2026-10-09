using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EFT;

namespace TutorialBackport.Client;

internal static class MainThread
{
    private static readonly Queue<Action> Pending = new Queue<Action>();

    public static Task Run(Func<Task> work)
    {
        var done = new TaskCompletionSource<bool>();
        lock (Pending)
        {
            Pending.Enqueue(async () =>
            {
                try
                {
                    await work();
                    done.TrySetResult(true);
                }
                catch (Exception error)
                {
                    done.TrySetException(error);
                }
            });
        }

        return done.Task;
    }

    public static void Drain()
    {
        while (true)
        {
            Action next;
            lock (Pending)
            {
                if (Pending.Count == 0)
                {
                    return;
                }

                next = Pending.Dequeue();
            }

            next();
        }
    }
}
