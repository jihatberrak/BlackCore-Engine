using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;
using BlackCore.Frontend.Cpp;
using BlackCore.Infrastructure.Toolchains;

namespace BlackCore.Backend.LLVM;

/// <summary>
/// C++ → LLVM bitcode → IR dökümü → verify/optimize (opt) → Clang + lld-link → Windows x64 EXE.
/// Her harici çağrı ayrı argüman listesiyle yapılır; shell kullanılmaz.
/// </summary>
public sealed class LlvmBackend : ICompilerBackend
{
    public const string Id = "llvm";

    private static readonly TimeSpan StepTimeoutFloor = TimeSpan.FromSeconds(30);

    private readonly CppToolchainProvider _toolchains;
    private readonly IExternalToolRunner _runner;

    public LlvmBackend(CppToolchainProvider toolchains, IExternalToolRunner runner)
    {
        _toolchains = toolchains ?? throw new ArgumentNullException(nameof(toolchains));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public string BackendId => Id;

    /// <summary>Optimizasyon profilini opt pass pipeline'ına çevirir. Her durumda verify uygulanır.</summary>
    public static string PassPipeline(OptimizationProfile profile) => profile switch
    {
        OptimizationProfile.None => "verify",
        OptimizationProfile.O1 => "verify,default<O1>,verify",
        OptimizationProfile.O2 => "verify,default<O2>,verify",
        OptimizationProfile.O3 => "verify,default<O3>,verify",
        OptimizationProfile.Os => "verify,default<Os>,verify",
        OptimizationProfile.Oz => "verify,default<Oz>,verify",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    public async Task<CompilationResult> CompileAsync(
        BuildRequest request,
        AnalysisResult analysis,
        BuildWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var timeout = request.Timeout < StepTimeoutFloor ? StepTimeoutFloor : request.Timeout;

        CppToolchain toolchain;
        try
        {
            toolchain = await _toolchains.GetAsync(cancellationToken);
        }
        catch (BlackCoreException ex)
        {
            return Failed(started, ex.ErrorCode, ex.Message);
        }

        var llvm = toolchain.Llvm;
        var env = toolchain.Environment;
        var sources = CppSources.Enumerate(request.SourcePath);
        if (sources.Count == 0)
        {
            return Failed(started, ErrorCode.SourceNotFound, "Derlenecek C++ kaynağı yok.");
        }

        var produced = new List<string>();
        var optimizedObjects = new List<string>();
        var passes = PassPipeline(request.Optimization);
        var intermediate = workspace.IntermediateDirectory;

        foreach (var source in sources)
        {
            var stem = Path.GetFileNameWithoutExtension(source);
            var bitcode = Path.Combine(intermediate, stem + ".bc");
            var ir = Path.Combine(intermediate, stem + ".ll");
            var optimized = Path.Combine(intermediate, stem + ".opt.bc");

            // 1. Kaynak → LLVM bitcode (Clang frontend, optimizasyonsuz).
            var compile = await ToolInvocation.RunAsync(
                _runner, llvm.Clang,
                new[] { "-std=c++20", $"--target={CppFrontend.TargetTriple}", "-O0", "-emit-llvm", "-c", source, "-o", bitcode },
                timeout, workspace.WorkDirectory, env, cancellationToken);
            if (ToolInvocation.Classify(compile, "clang++ (bitcode)", ErrorCode.CompilationFailed) is { } f1)
            {
                return Failed(started, f1.ErrorCode, f1.Message);
            }

            // 2. Bitcode → okunabilir IR (denetim/kayıt için).
            var disassemble = await ToolInvocation.RunAsync(
                _runner, llvm.LlvmDis,
                new[] { bitcode, "-o", ir },
                timeout, workspace.WorkDirectory, env, cancellationToken);
            if (ToolInvocation.Classify(disassemble, "llvm-dis", ErrorCode.CompilationFailed) is { } f2)
            {
                return Failed(started, f2.ErrorCode, f2.Message);
            }

            // 3. Verifier + optimizasyon. Verifier hataları burada build'i durdurur.
            var optimize = await ToolInvocation.RunAsync(
                _runner, llvm.Opt,
                new[] { $"-passes={passes}", bitcode, "-o", optimized },
                timeout, workspace.WorkDirectory, env, cancellationToken);
            if (ToolInvocation.Classify(optimize, "opt (verify/optimize)", ErrorCode.CompilationFailed) is { } f3)
            {
                return Failed(started, f3.ErrorCode, f3.Message);
            }

            produced.Add(bitcode);
            produced.Add(ir);
            produced.Add(optimized);
            optimizedObjects.Add(optimized);
        }

        // 4. Bağlama: Clang sürücüsü lld-link ile native Windows x64 PE üretir.
        var candidate = Path.Combine(intermediate, "candidate.exe");
        var linkArgs = new List<string>
        {
            $"--target={CppFrontend.TargetTriple}",
            "-fuse-ld=lld-link",
            "-O0",
        };
        linkArgs.AddRange(optimizedObjects);
        linkArgs.Add("-o");
        linkArgs.Add(candidate);

        var link = await ToolInvocation.RunAsync(
            _runner, llvm.Clang, linkArgs, timeout, workspace.WorkDirectory, env, cancellationToken);
        if (ToolInvocation.Classify(link, "clang++ (link)", ErrorCode.CompilationFailed) is { } f4)
        {
            return Failed(started, f4.ErrorCode, f4.Message);
        }

        produced.Add(candidate);

        return new CompilationResult
        {
            Succeeded = true,
            Message = $"LLVM derlemesi tamamlandı ({sources.Count} birim, pipeline: {passes}).",
            OutputPath = candidate,
            ProducedFiles = produced,
            ToolVersion = llvm.Version,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static CompilationResult Failed(DateTimeOffset started, ErrorCode code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };
}
