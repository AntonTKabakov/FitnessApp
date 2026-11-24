using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FitnessApp.Filters;

// Keep the old UI available only for explicit migration compatibility tests.
public sealed class ApiOnlyControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var controller in feature.Controllers.ToArray())
            if (controller.Namespace != "FitnessApp.Controllers.Api" && controller.Name != "UsersController")
                feature.Controllers.Remove(controller);
    }
}
