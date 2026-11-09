using System.Text.RegularExpressions;
using BlackCore.Engine.Models;

namespace BlackCore.Frontend.Cpp;

/// <summary>
/// clang stderr çıktısını yapılandırılmış diagnostic'lere çevirir.
/// Biçim: <c>dosya:satır:sütun: seviye: mesaj</c>. Windows sürücü harfleri (C:\...) desteklenir.
/// </summary>
public static partial class ClangDiagnosticParser
{
    public static IReadOnlyList<Diagnostic> Parse(string stderr)
    {
        var result = new List<Diagnostic>();
        if (string.IsNullOrEmpty(stderr))
        {
            return result;
        }

        foreach (Match match in DiagnosticPattern().Matches(stderr))
        {
            var severity = match.Groups["sev"].Value switch
            {
                "warning" => DiagnosticSeverity.Warning,
                "error" or "fatal error" => DiagnosticSeverity.Error,
                _ => (DiagnosticSeverity?)null,
            };

            if (severity is null)
            {
                // "note" satırları ana hatanın ek açıklamasıdır; ayrı diagnostic olarak eklenmez.
                continue;
            }

            var code = severity == DiagnosticSeverity.Error ? "CLANG_ERROR" : "CLANG_WARNING";
            result.Add(new Diagnostic(
                severity.Value,
                code,
                match.Groups["msg"].Value.Trim(),
                match.Groups["file"].Value,
                int.Parse(match.Groups["line"].Value)));
        }

        return result;
    }

    [GeneratedRegex(
        @"^(?<file>(?:[A-Za-z]:)?[^:\r\n]+):(?<line>\d+):(?<col>\d+): (?<sev>fatal error|error|warning|note): (?<msg>[^\r\n]+)",
        RegexOptions.Multiline)]
    private static partial Regex DiagnosticPattern();
}
