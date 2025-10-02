using System;

namespace FModelHeadless.Headless;

internal static class HeadlessDispatcher
{
    public static void Invoke(Action action)
    {
        action();
    }
}
