using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace GSM.Application.Common.Logs;

/// <summary>
/// Huella para agrupar errores iguales (docs/GSM-logs.md §7): tipo de excepción + primer marco del código
/// propio (<c>GSM.*</c>) de la traza. No depende del mensaje ni de los datos del request.
/// </summary>
public static class ErrorFingerprint
{
    public static string Compute(Exception exception)
    {
        var root = Unwrap(exception);
        var frame = FirstOwnFrame(root) ?? "no-own-frame";
        var input = $"{root.GetType().FullName}|{frame}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();
    }

    /// <summary>La excepción que realmente se lanzó (las de reflexión envuelven la original).</summary>
    public static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: { } inner })
        {
            exception = inner;
        }

        return exception;
    }

    private static string? FirstOwnFrame(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type?.FullName is not { } fullName
                || !fullName.StartsWith("GSM.", StringComparison.Ordinal))
            {
                continue;
            }

            // Async/lambdas: "Handler+<HandleAsync>d__3.MoveNext" → "Handler.HandleAsync".
            var methodName = method.Name;
            while (type is { IsNested: true } && type.Name.StartsWith('<'))
            {
                var end = type.Name.IndexOf('>');
                if (end > 1)
                {
                    methodName = type.Name[1..end];
                }

                type = type.DeclaringType!;
            }

            return $"{type.FullName}.{methodName}";
        }

        return null;
    }
}
