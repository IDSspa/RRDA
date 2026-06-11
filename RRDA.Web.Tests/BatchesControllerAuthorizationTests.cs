using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Areas.Data.Controllers;
using RRDA.Web.Security;
using Xunit;

namespace RRDA.Web.Tests;

public sealed class BatchesControllerAuthorizationTests
{
    [Fact]
    public void Controller_AllowsAnyEnabledUserToViewBatchList()
    {
        var authorize = Assert.Single(typeof(BatchesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(Policies.AnyUser, authorize.Policy);
    }

    [Theory]
    [InlineData(nameof(BatchesController.Create))]
    [InlineData(nameof(BatchesController.Delete))]
    [InlineData(nameof(BatchesController.DeleteConfirmed))]
    public void ManagementActions_RequireAtLeastSupervisor(string actionName)
    {
        var methods = typeof(BatchesController).GetMethods()
            .Where(method => method.Name == actionName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var authorize = Assert.Single(method
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());
            Assert.Equal(Policies.AtLeastSupervisor, authorize.Policy);
        });
    }

    [Theory]
    [InlineData(nameof(BatchesController.Create))]
    [InlineData(nameof(BatchesController.DeleteConfirmed))]
    public void MutatingActions_ValidateAntiForgeryToken(string actionName)
    {
        var postMethods = typeof(BatchesController).GetMethods()
            .Where(method => method.Name == actionName)
            .Where(method => method.IsDefined(typeof(HttpPostAttribute), inherit: true))
            .ToArray();

        var method = Assert.Single(postMethods);
        Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: true));
    }
}
