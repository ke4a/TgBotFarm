using Bunit;
using Microsoft.AspNetCore.Components;
using NUnit.Framework.Internal;

namespace BotFarm.TestKit;

/// <summary>
/// Waits on rendered components with NUnit assertions.
/// </summary>
public static class RenderedComponentAssertExtensions
{
    /// <summary>
    /// Re-runs <paramref name="assertion"/> after each render until it passes or <paramref name="timeout"/> runs out.
    /// Use this instead of bUnit's <c>WaitForAssertion</c> with NUnit assertions.
    /// </summary>
    /// <remarks>
    /// NUnit records a failed assertion on the current test result before it throws, so with
    /// <c>WaitForAssertion</c> a check that fails before a later render would still fail the test after it passes.
    /// Each attempt here runs in an isolated NUnit context, so only the outcome of the whole wait counts.
    /// </remarks>
    /// <param name="cut">The rendered component to watch for renders.</param>
    /// <param name="assertion">The NUnit assertions to check after each render.</param>
    /// <param name="timeout">How long to wait; bUnit's default timeout when omitted.</param>
    public static void WaitForNUnitAssertion<TComponent>(
        this IRenderedComponent<TComponent> cut,
        Action assertion,
        TimeSpan? timeout = null)
        where TComponent : IComponent
    {
        cut.WaitForAssertion(
            () =>
            {
                using (new TestExecutionContext.IsolatedContext())
                {
                    assertion();
                }
            },
            timeout);
    }
}
