using System.Runtime.Versioning;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;

namespace BlackCore.Infrastructure.Toolchains;

/// <summary>C++ derlemesi için birleşik araç seti: LLVM araçları + MSVC/SDK ortamı.</summary>
public sealed record CppToolchain(LlvmToolchain Llvm, IReadOnlyDictionary<string, string> Environment);

/// <summary>Araç keşfini bir kez yapar ve sonucu önbelleğe alır.</summary>
[SupportedOSPlatform("windows")]
public sealed class CppToolchainProvider
{
    private readonly string? _llvmRoot;
    private readonly IExternalToolRunner _runner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CppToolchain? _cached;

    public CppToolchainProvider(string? llvmRoot, IExternalToolRunner runner)
    {
        _llvmRoot = llvmRoot;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<CppToolchain> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var llvm = await LlvmToolchainLocator.ResolveAsync(_llvmRoot, _runner, cancellationToken);
            var environment = await MsvcEnvironment.DiscoverAsync(_runner, cancellationToken);
            _cached = new CppToolchain(llvm, environment);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
