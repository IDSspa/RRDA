using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Areas.Data.Controllers;
using RRDA.Web.Security;
using Xunit;

namespace RRDA.Web.Tests;

public sealed class FilesControllerReferenceAuthorizationTests
{
    [Theory]
    [InlineData(nameof(FilesController.AddManualReference))]
    [InlineData(nameof(FilesController.DeleteManualReference))]
    public void ManualReferenceActions_RequireSupervisorAndAntiForgeryToken(string actionName)
    {
        var method = Assert.Single(
            typeof(FilesController).GetMethods(),
            candidate => candidate.Name == actionName);

        var authorize = Assert.Single(method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(Policies.AtLeastSupervisor, authorize.Policy);
        Assert.True(method.IsDefined(typeof(HttpPostAttribute), inherit: true));
        Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: true));
    }
}
