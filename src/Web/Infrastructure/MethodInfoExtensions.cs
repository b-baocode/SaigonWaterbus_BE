using System.Reflection;

namespace SaigonWaterbus.Web.Infrastructure;

public static class MethodInfoExtensions
{
    private static readonly char[] AnonymousMethodChars = ['<', '>'];

    ///
    public static bool IsAnonymous(this MethodInfo method) =>
        method.Name.Any(AnonymousMethodChars.Contains);

    ///
    public static void AnonymousMethod(this IGuardClause guardClause, Delegate input)
    {
        if (input.Method.IsAnonymous())
            throw new ArgumentException("The endpoint name must be specified when using anonymous handlers.");
    }
}
