#pragma warning disable ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class MeadowExtensions
{
    public static IResourceBuilder<ProjectResource> AddMeadowProject<TProject>(this IDistributedApplicationBuilder builder, [ResourceName] string name) where TProject : IProjectMetadata, new()
    {

        var meadowCliCheck = builder.AddExecutable("meadow-cli-check", "dotnet", ".", ["tool", "run", "meadow"])
            .WithExplicitStart();

        var meadowCliInstall = builder.AddExecutable("meadow-cli-install", "dotnet", ".", ["tool", "install", "--local", "WildernessLabs.Meadow.Cli", "--prerelease"])
            .WithExplicitStart();

        var meadowCliLoginCheck = builder.AddExecutable("meadow-cli-login-check", "dotnet", ".", ["tool", "run", "meadow", "cloud", "collection", "list"])
            .WithExplicitStart();

        var meadowCliLogin = builder.AddExecutable("meadow-login", "dotnet", ".", ["tool", "run", "meadow", "login"])
            .WithExplicitStart();

        var meadowCliPortList = builder.AddExecutable("meadow-cli-port-list", "dotnet", ".", ["tool", "run", "meadow", "port", "list"])
            .WithExplicitStart();

        var meadowCliUninstall = builder.AddExecutable("meadow-cli-uninstall", "dotnet", ".", ["tool", "uninstall", "--local", "WildernessLabs.Meadow.Cli"])
            .WithExplicitStart();

        var meadowCliLogout = builder.AddExecutable("meadow-cli-logout", "dotnet", ".", ["tool", "run", "meadow", "logout"])
            .WithExplicitStart();

        var project = builder.AddProject<TProject>(name)
            .WithCommand("deploy-firmware-to-cloud", "Deploy to Cloud",
            (context) => DeployFirmwareToCloudAsync(
                context,
                meadowCliCheck.Resource,
                meadowCliInstall.Resource,
                meadowCliLoginCheck.Resource,
                meadowCliLogin.Resource
                ));

        meadowCliCheck.WithParentRelationship(project);
        meadowCliInstall.WithParentRelationship(project);
        meadowCliLoginCheck.WithParentRelationship(project);
        meadowCliLogin.WithParentRelationship(project);
        meadowCliPortList.WithParentRelationship(project);
        meadowCliUninstall.WithParentRelationship(project);
        meadowCliLogout.WithParentRelationship(project);

        return project;
    }

    private static async Task<ExecuteCommandResult> DeployFirmwareToCloudAsync(
        ExecuteCommandContext context,
        ExecutableResource meadowCliCheckResource,
        ExecutableResource meadowCliInstallResource,
        ExecutableResource meadlowCliLoginCheckResource,
        ExecutableResource meadowCliLoginResource)
    {
        try
        {
            var rls = context.ServiceProvider.GetRequiredService<ResourceLoggerService>();
            var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
            var notificationService = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();
            var commandService = context.ServiceProvider.GetRequiredService<ResourceCommandService>();

            // First start the CLI check to see if the Meadow CLI is installed.
            var meadowCliCheckResourceStartResult = await commandService.ExecuteCommandAsync(
                meadowCliCheckResource,
                "resource-start",
                context.CancellationToken
                );

            // The command to start the Meadow CLI check resource could fail - if that is the case
            // then something is seriously broken so we just pipe the error message back to the user.
            if (!meadowCliCheckResourceStartResult.Success)
            {
                return meadowCliCheckResourceStartResult;
            }

            // Wait for the Meadow CLI check to finish.
            var meadowCliCheckResourceState = await notificationService.WaitForResourceAsync(
                meadowCliCheckResource.Name,
                (@event) => @event.Snapshot.State == KnownResourceStates.Finished,
                context.CancellationToken);

            if (meadowCliCheckResourceState.Snapshot.State == KnownResourceStates.Finished && meadowCliCheckResourceState.Snapshot.ExitCode != 0)
            {
                var installConfirmation = await interactionService.PromptConfirmationAsync(
                    title: "Install Meadow CLI",
                    message: "The Meadow CLI is not installed. Do you want to install it now?",
                    new()
                    {
                        PrimaryButtonText = "Install",
                        SecondaryButtonText = "Cancel",
                        Intent = MessageIntent.Confirmation
                    },
                    context.CancellationToken
                    );

                if (installConfirmation.Canceled)
                {
                    return new ExecuteCommandResult
                    {
                        Success = false,
                        ErrorMessage = "User canceled the installation of the Meadow CLI."
                    };
                }

                var meadowCliInstallResourceStartResult = await commandService.ExecuteCommandAsync(
                    meadowCliInstallResource,
                    "resource-start",
                    context.CancellationToken
                    );

                if (!meadowCliInstallResourceStartResult.Success)
                {
                    return meadowCliInstallResourceStartResult;
                }

                var meadowCliInstallResourceState = await notificationService.WaitForResourceAsync(
                    meadowCliInstallResource.Name,
                    (@event) => @event.Snapshot.State == KnownResourceStates.Finished,
                    context.CancellationToken);

                if (meadowCliInstallResourceState.Snapshot.ExitCode != 0)
                {
                    return new ExecuteCommandResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to install the Meadow CLI. Please check the logs for more details."
                    };
                }
            }

            var meadowCliLoginCheckResult = await commandService.ExecuteCommandAsync(
                meadlowCliLoginCheckResource,
                "resource-start",
                context.CancellationToken
                );

            if (!meadowCliLoginCheckResult.Success)
            {
                return meadowCliLoginCheckResult;
            }

            var meadowCliLoginCheckResourceState = await notificationService.WaitForResourceAsync(
                meadlowCliLoginCheckResource.Name,
                (@event) => @event.Snapshot.State == KnownResourceStates.Finished,
                context.CancellationToken);

            if (meadowCliLoginCheckResourceState.Snapshot.ExitCode != 0)
            {
                var loginConfirmation = await interactionService.PromptConfirmationAsync(
                    title: "Login to Meadow Cloud",
                    message: "You must be logged in to deploy firmware to the cloud. Do you want to log in now?",
                    new()
                    {
                        PrimaryButtonText = "Login",
                        SecondaryButtonText = "Cancel",
                        Intent = MessageIntent.Confirmation
                    },
                    context.CancellationToken
                    );

                if (loginConfirmation.Canceled)
                {
                    return new ExecuteCommandResult
                    {
                        Success = false,
                        ErrorMessage = "User canceled the login operation."
                    };
                }
            }

            var meadowCliLogin = await commandService.ExecuteCommandAsync(
                meadowCliLoginResource,
                "resource-start",
                context.CancellationToken);

            if (!meadowCliLogin.Success)
            {
                return meadowCliLogin;
            }

            var meadowCliLoginResourceState = await notificationService.WaitForResourceAsync(
                meadowCliLoginResource.Name,
                (@event) => @event.Snapshot.State == KnownResourceStates.Finished,
                context.CancellationToken);

            if (meadowCliLoginResourceState.Snapshot.ExitCode != 0)
            {
                return new ExecuteCommandResult
                {
                    Success = false,
                    ErrorMessage = "Failed to install the Meadow CLI. Please check the logs for more details."
                };
            }

            return new ExecuteCommandResult
                {
                    Success = true,
                };
        }
        catch (Exception ex)
        {
            
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = $"An error occurred while deploying firmware to the cloud: {ex.Message}"
            };
        }
    }
}
