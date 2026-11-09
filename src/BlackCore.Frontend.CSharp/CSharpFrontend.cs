using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Infrastructure.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Diagnostic = BlackCore.Engine.Models.Diagnostic;

namespace BlackCore.Frontend.CSharp;

/// <summary>
/// C# frontend'i: Roslyn ile sözdizimi ve semantik diagnostic'ler, ayrıca NativeAOT açısından riskli
/// reflection çağrılarının tespiti. Roslyn analizi tek başına NativeAOT uyumluluğunu kanıtlamaz (şartname 9.1);
/// kesin karar NativeAOT yayımı ve çalışma testiyle verilir.
/// </summary>
public sealed class CSharpFrontend : ILanguageFrontend
{
    public const string Id = "csharp";
    public const string NativeAotRuleCode = "NATIVEAOT001";

    private const string GlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences = new(LoadRuntimeReferences);

    public string LanguageId => Id;

    public Task<AnalysisResult> AnalyzeAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        return Task.Run(() => Analyze(request, cancellationToken), cancellationToken);
    }

    private static AnalysisResult Analyze(BuildRequest request, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;

        var project = CSharpProjects.Find(request.SourcePath);
        if (project is null)
        {
            return Failed(started, ErrorCode.SourceNotFound, $"C# projesi (.csproj) bulunamadı: {request.SourcePath}");
        }

        var files = CSharpProjects.SourceFiles(project.Directory);
        if (files.Count == 0)
        {
            return Failed(started, ErrorCode.SourceNotFound, "Projede derlenecek .cs dosyası yok.");
        }

        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(GlobalUsings, new CSharpParseOptions(LanguageVersion.Latest), path: "GlobalUsings.g.cs"),
        };
        trees.AddRange(files.Select(f => CSharpSyntaxTree.ParseText(
            File.ReadAllText(f),
            new CSharpParseOptions(LanguageVersion.Latest),
            path: f)));

        var compilation = CSharpCompilation.Create(
            "BlackCore.Analysis",
            trees,
            RuntimeReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        var diagnostics = new List<Diagnostic>();
        foreach (var d in compilation.GetDiagnostics(ct))
        {
            var severity = d.Severity switch
            {
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error => BlackCore.Engine.Models.DiagnosticSeverity.Error,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => BlackCore.Engine.Models.DiagnosticSeverity.Warning,
                _ => (BlackCore.Engine.Models.DiagnosticSeverity?)null,
            };

            if (severity is null)
            {
                continue;
            }

            diagnostics.Add(ToEngineDiagnostic(d, severity.Value));
        }

        foreach (var tree in trees.Skip(1))
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new NativeAotRiskWalker(model);
            walker.Visit(tree.GetRoot(ct));
            diagnostics.AddRange(walker.Findings);
        }

        var hasErrors = diagnostics.Any(d => d.Severity == BlackCore.Engine.Models.DiagnosticSeverity.Error);
        var firstError = diagnostics.FirstOrDefault(d => d.Severity == BlackCore.Engine.Models.DiagnosticSeverity.Error);

        return new AnalysisResult
        {
            Succeeded = !hasErrors,
            ErrorCode = hasErrors ? ErrorCode.AnalysisFailed : ErrorCode.None,
            Message = hasErrors
                ? $"C# analizi hata buldu: {firstError!.FilePath}:{firstError.Line}: {firstError.Message}"
                : $"C# analizi tamamlandı ({files.Count} dosya, {diagnostics.Count} bulgu).",
            Diagnostics = diagnostics,
            ToolVersion = typeof(CSharpCompilation).Assembly.GetName().Version?.ToString(3),
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static BlackCore.Engine.Models.Diagnostic ToEngineDiagnostic(
        Microsoft.CodeAnalysis.Diagnostic d,
        BlackCore.Engine.Models.DiagnosticSeverity severity)
    {
        var span = d.Location.GetLineSpan();
        var path = span.IsValid ? span.Path : null;
        var line = span.IsValid ? span.StartLinePosition.Line + 1 : (int?)null;
        return new BlackCore.Engine.Models.Diagnostic(severity, d.Id, d.GetMessage(), path, line);
    }

    private static IReadOnlyList<MetadataReference> LoadRuntimeReferences()
    {
        // Çalışan çalışma zamanının yönetilen assembly'leri derleme referansı olarak kullanılır.
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>();
        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                // Yalnızca yönetilen (metadata içeren) assembly'ler; native DLL'ler atlanır.
                System.Reflection.AssemblyName.GetAssemblyName(dll);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(dll));
        }

        return references;
    }

    private static AnalysisResult Failed(DateTimeOffset started, ErrorCode code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };

    /// <summary>NativeAOT'ta güvenilmeyen veya çalışma zamanı kod üretimi gerektiren API çağrılarını işaretler.</summary>
    private sealed class NativeAotRiskWalker : CSharpSyntaxWalker
    {
        private static readonly HashSet<string> RiskyTypes = new(StringComparer.Ordinal)
        {
            "System.Activator",
            "System.Type",
            "System.Reflection.Assembly",
            "System.Reflection.MethodBase",
            "System.Reflection.Emit.DynamicMethod",
        };

        private readonly SemanticModel _model;

        public NativeAotRiskWalker(SemanticModel model)
        {
            _model = model;
        }

        public List<BlackCore.Engine.Models.Diagnostic> Findings { get; } = new();

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is IMethodSymbol method && method.ContainingType is { } type)
            {
                var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    .Split('<')[0];
                var fullName = type.ContainingNamespace.IsGlobalNamespace
                    ? type.Name
                    : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";

                if (RiskyTypes.Contains(fullName) && IsRiskyMember(method.Name))
                {
                    var span = node.GetLocation().GetLineSpan();
                    Findings.Add(new BlackCore.Engine.Models.Diagnostic(
                        BlackCore.Engine.Models.DiagnosticSeverity.Warning,
                        NativeAotRuleCode,
                        $"NativeAOT riski: {typeName}.{method.Name} çalışma zamanında reflection/kod üretimi gerektirebilir.",
                        span.Path,
                        span.StartLinePosition.Line + 1));
                }
            }

            base.VisitInvocationExpression(node);
        }

        private static bool IsRiskyMember(string name) => name is
            "CreateInstance" or "GetType" or "Load" or "LoadFrom" or "Invoke"
            or "MakeGenericType" or "MakeGenericMethod" or "GetMethod" or "GetMethods"
            or "GetProperty" or "GetField" or "GetConstructor";
    }
}
