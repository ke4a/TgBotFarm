using BotFarm.Shared.Components;
using FluentResults;
using Microsoft.JSInterop;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardComponentBaseTests
{
    private class TestDashboardComponent : DashboardComponentBase
    {
        public void SetJsRuntime(IJSRuntime jsRuntime) => JSRuntime = jsRuntime;

        public Task InvokeShowToastAsync(string message, bool success) => ShowToastAsync(message, success);

        public string InvokeGetResultMessage(ResultBase result, string successFallback, string failureFallback)
            => GetResultMessage(result, successFallback, failureFallback);

        public string? InvokeGetResultMetadata(ResultBase result, string key)
            => GetResultMetadata(result, key);
    }

    [Test]
    public async Task ShowToastAsync_InvokesJsRuntimeWithExpectedArguments()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        var component = new TestDashboardComponent { BotName = "TestBot" };
        component.SetJsRuntime(jsRuntime);

        await component.InvokeShowToastAsync("hello", true);

        await jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => args.Length == 2 && args[0] as string == "hello" && args[1] as bool? == true));
    }

    [Test]
    public async Task ShowToastAsync_WithFailureFlag_PassesFalseToJsRuntime()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        var component = new TestDashboardComponent { BotName = "TestBot" };
        component.SetJsRuntime(jsRuntime);

        await component.InvokeShowToastAsync("error occurred", false);

        await jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => args.Length == 2 && args[0] as string == "error occurred" && args[1] as bool? == false));
    }

    [Test]
    public void GetResultMessage_WithSuccess_ReturnsSuccessMessage()
    {
        var result = Result.Ok().WithSuccess("Operation completed");

        var component = new TestDashboardComponent();

        var message = component.InvokeGetResultMessage(result, "Success fallback", "Failure fallback");

        Assert.That(message, Is.EqualTo("Operation completed"));
    }

    [Test]
    public void GetResultMessage_WithFailure_ReturnsFailureMessage()
    {
        var result = Result.Fail("Something went wrong");

        var component = new TestDashboardComponent();

        var message = component.InvokeGetResultMessage(result, "Success fallback", "Failure fallback");

        Assert.That(message, Is.EqualTo("Something went wrong"));
    }

    [Test]
    public void GetResultMessage_WithMissingMessages_UsesFallbacks()
    {
        var component = new TestDashboardComponent();

        var successMessage = component.InvokeGetResultMessage(Result.Ok(), "Success fallback", "Failure fallback");
        var failureMessage = component.InvokeGetResultMessage(Result.Fail(string.Empty), "Success fallback", "Failure fallback");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(successMessage, Is.EqualTo("Success fallback"));
            Assert.That(failureMessage, Is.EqualTo("Failure fallback"));
        }
    }

    [Test]
    public void GetResultMessage_WithWhitespaceMessage_UsesFallback()
    {
        var component = new TestDashboardComponent();

        var successMessage = component.InvokeGetResultMessage(
            Result.Ok().WithSuccess("   "), 
            "Success fallback", 
            "Failure fallback");
        
        var failureMessage = component.InvokeGetResultMessage(
            Result.Fail("   "), 
            "Success fallback", 
            "Failure fallback");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(successMessage, Is.EqualTo("Success fallback"), "Whitespace messages use fallback due to IsNullOrWhiteSpace check");
            Assert.That(failureMessage, Is.EqualTo("Failure fallback"), "Whitespace messages use fallback due to IsNullOrWhiteSpace check");
        }
    }

    [Test]
    public void GetResultMessage_WithMultipleSuccesses_ReturnsFirstSuccess()
    {
        var result = Result.Ok()
            .WithSuccess("First success")
            .WithSuccess("Second success");

        var component = new TestDashboardComponent();

        var message = component.InvokeGetResultMessage(result, "Success fallback", "Failure fallback");

        Assert.That(message, Is.EqualTo("First success"));
    }

    [Test]
    public void GetResultMessage_WithMultipleErrors_ReturnsFirstError()
    {
        var result = Result.Fail("First error")
            .WithError("Second error");

        var component = new TestDashboardComponent();

        var message = component.InvokeGetResultMessage(result, "Success fallback", "Failure fallback");

        Assert.That(message, Is.EqualTo("First error"));
    }

    [Test]
    public void GetResultMetadata_WithExistingKey_ReturnsMetadataValue()
    {
        var success = new Success("ok").WithMetadata("file", "backup.zip");
        var result = Result.Ok().WithSuccess(success);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "file");

        Assert.That(metadata, Is.EqualTo("backup.zip"));
    }

    [Test]
    public void GetResultMetadata_WithMissingMetadata_ReturnsNull()
    {
        var result = Result.Ok();

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "file");

        Assert.That(metadata, Is.Null);
    }

    [Test]
    public void GetResultMetadata_WithNonExistentKey_ReturnsNull()
    {
        var success = new Success("ok").WithMetadata("file", "backup.zip");
        var result = Result.Ok().WithSuccess(success);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "nonexistent");

        Assert.That(metadata, Is.Null);
    }

    [Test]
    public void GetResultMetadata_WithNullMetadataValue_ReturnsNull()
    {
        var success = new Success("ok").WithMetadata("file", null);
        var result = Result.Ok().WithSuccess(success);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "file");

        Assert.That(metadata, Is.Null);
    }

    [Test]
    public void GetResultMetadata_WithNumericMetadataValue_ReturnsStringRepresentation()
    {
        var success = new Success("ok").WithMetadata("count", 42);
        var result = Result.Ok().WithSuccess(success);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "count");

        Assert.That(metadata, Is.EqualTo("42"));
    }

    [Test]
    public void GetResultMetadata_WithComplexObjectMetadata_ReturnsToStringRepresentation()
    {
        var testObject = new { Name = "Test", Value = 123 };
        var success = new Success("ok").WithMetadata("data", testObject);
        var result = Result.Ok().WithSuccess(success);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "data");

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata, Does.Contain("Name"));
    }

    [Test]
    public void GetResultMetadata_WithMultipleSuccesses_ReturnsMetadataFromFirstSuccess()
    {
        var success1 = new Success("first").WithMetadata("key", "value1");
        var success2 = new Success("second").WithMetadata("key", "value2");
        var result = Result.Ok()
                           .WithSuccess(success1)
                           .WithSuccess(success2);

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "key");

        Assert.That(metadata, Is.EqualTo("value1"));
    }

    [Test]
    public void GetResultMetadata_WithFailedResult_ReturnsNull()
    {
        var result = Result.Fail("error");

        var component = new TestDashboardComponent();

        var metadata = component.InvokeGetResultMetadata(result, "key");

        Assert.That(metadata, Is.Null);
    }
}
