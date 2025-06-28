using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public static class MeadowExtensions
{
    public static IResourceBuilder<ProjectResource> AddMeadowProject<TProject>(this IDistributedApplicationBuilder builder, [ResourceName] string name) where TProject : IProjectMetadata, new()
    {
        return builder.AddProject<TProject>(name)
            .WithCommand("deploy-firmware", "Deploy to Device", DeployFirmwareToDeviceAsync))
    }

    private static async Task<ExecuteCommandResult> DeployFirmwareToDeviceAsync(ExecuteCommandContext context)
    {
        await Task.Delay(1000).ConfigureAwait(false);
        return new ExecuteCommandResult
        {
            Success = true,
        };
    }
}
