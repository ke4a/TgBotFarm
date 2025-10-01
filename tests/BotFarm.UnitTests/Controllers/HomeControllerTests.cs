using BotFarm.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Principal;

namespace BotFarm.UnitTests.Controllers;

[TestFixture]
public class HomeControllerTests
{
    private HomeController _controller;
    private HttpContext _httpContext;
    private ClaimsPrincipal _user;

    [SetUp]
    public void SetUp()
    {
        _controller = new HomeController();
        _httpContext = Substitute.For<HttpContext>();
        _user = Substitute.For<ClaimsPrincipal>();
        
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = _httpContext
        };
        
        _httpContext.User.Returns(_user);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    [Test]
    public void Index_WhenUserIsNotAuthenticatedAndDebuggerNotAttached_ReturnsTextPlainContent()
    {
        // Arrange
        var identity = Substitute.For<IIdentity>();
        identity.IsAuthenticated.Returns(false);
        _user.Identity.Returns(identity);

        // Act
        var result = _controller.Index();

        // Assert
        if (!Debugger.IsAttached)
        {
            var contentResult = result as ContentResult;
            Assert.That(contentResult, Is.Not.Null);
            Assert.That(contentResult.ContentType, Is.EqualTo("text/plain"));
        }
    }

    [Test]
    public void Index_WhenUserIsNotAuthenticatedAndDebuggerNotAttached_ReturnsExpectedAsciiArt()
    {
        // Arrange
        var identity = Substitute.For<IIdentity>();
        identity.IsAuthenticated.Returns(false);
        _user.Identity.Returns(identity);

        var expectedContent = @"
     _
   _| |
 _| | |
| | | |
| | | | __
| | | |/  \
|       /\ \
|      /  \/
|      \  /\
|       \/ /
 \        /
  |     /
  |    |
";

        // Act
        var result = _controller.Index();

        // Assert
        if (!Debugger.IsAttached)
        {
            var contentResult = result as ContentResult;
            Assert.That(contentResult, Is.Not.Null);
            Assert.That(contentResult.Content, Is.EqualTo(expectedContent));
        }
    }

    [Test]
    public void Index_WhenUserIdentityIsNull_ReturnsContentResult()
    {
        // Arrange
        _user.Identity.Returns((IIdentity)null);

        // Act
        var result = _controller.Index();

        // Assert
        if (!Debugger.IsAttached)
        {
            Assert.That(result, Is.InstanceOf<ContentResult>());
            Assert.DoesNotThrow(() => _controller.Index());
        }
    }
}
