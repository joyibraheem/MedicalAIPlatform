using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;

namespace MedicalAIPlatform.Infrastructure;

/// <summary>Marks a controller that must not exist outside Development.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevelopmentOnlyAttribute : Attribute;

/// <summary>Removes <see cref="DevelopmentOnlyAttribute"/> controllers from MVC when not in Development.</summary>
public sealed class RemoveDevelopmentOnlyControllersConvention : IApplicationModelConvention
{
    private readonly IHostEnvironment _environment;

    public RemoveDevelopmentOnlyControllersConvention(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public void Apply(ApplicationModel application)
    {
        if (_environment.IsDevelopment())
            return;

        for (var i = application.Controllers.Count - 1; i >= 0; i--)
        {
            if (application.Controllers[i].ControllerType
                    .GetCustomAttributes(typeof(DevelopmentOnlyAttribute), inherit: true)
                    .Length
                > 0)
            {
                application.Controllers.RemoveAt(i);
            }
        }
    }
}

/// <summary>Returns 404 when an action runs outside Development (defense in depth).</summary>
public sealed class DevelopmentOnlyActionFilter : IActionFilter
{
    private readonly IHostEnvironment _environment;

    public DevelopmentOnlyActionFilter(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (_environment.IsDevelopment())
            return;

        var isDevOnly = context.ActionDescriptor.EndpointMetadata
            .OfType<DevelopmentOnlyAttribute>()
            .Any()
            || context.Controller.GetType()
                .GetCustomAttributes(typeof(DevelopmentOnlyAttribute), inherit: true)
                .Length > 0;

        if (isDevOnly)
            context.Result = new NotFoundResult();
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
