using System;
using CUE4Parse.FileProvider;
using FModelHeadless.Headless;

namespace FModelHeadless.Cli;

internal sealed class ProviderScope : IDisposable
{
    public ProviderScope(DefaultFileProvider provider)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        GlobalContext.Provider = provider;
    }

    public DefaultFileProvider Provider { get; }

    public void Dispose()
    {
        GlobalContext.Provider = null;
        Provider.Dispose();
    }
}
