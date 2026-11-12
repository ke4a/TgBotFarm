using BotFarm.TestKit;
using Bunit;
using Bunit.Extensions.WaitForHelpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Checks that waiting for an NUnit assertion on a rendered component keeps the test green when the assertion
/// only starts to pass after a later render.
/// </summary>
[TestFixture]
public class WaitForNUnitAssertionTests
{
    [Test]
    public async Task WaitForNUnitAssertion_WhenContentAppearsAfterLaterRender_Passes()
    {
        await using var context = new BunitContext();
        var cut = context.Render<DelayedContent>();

        cut.WaitForNUnitAssertion(() => Assert.That(cut.FindAll(".loaded"), Has.Count.EqualTo(1)));

        Assert.That(cut.Find(".loaded").TextContent, Is.EqualTo("Ready"));
    }

    [Test]
    public async Task WaitForNUnitAssertion_WhenAssertionNeverPasses_ThrowsAfterTimeout()
    {
        await using var context = new BunitContext();
        var cut = context.Render<DelayedContent>();

        Assert.Throws<WaitForFailedException>(() => cut.WaitForNUnitAssertion(
            () => Assert.That(cut.FindAll(".missing"), Is.Not.Empty),
            TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>
    /// Shows its content only after a short asynchronous delay, so the first render never has it.
    /// </summary>
    private sealed class DelayedContent : ComponentBase
    {
        private bool loaded;

        protected override async Task OnInitializedAsync()
        {
            await Task.Delay(50);
            loaded = true;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (loaded)
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "loaded");
                builder.AddContent(2, "Ready");
                builder.CloseElement();
            }
        }
    }
}
