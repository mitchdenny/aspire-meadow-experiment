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

        var project = builder.AddProject<TProject>(name);
        project.WithCommand("deploy-firmware-to-cloud", "Deploy to Cloud",
            (context) => DeployFirmwareToCloudAsync(
                context,
                project.Resource,
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
        ProjectResource projectResource,
        ExecutableResource meadowCliCheckResource,
        ExecutableResource meadowCliInstallResource,
        ExecutableResource meadlowCliLoginCheckResource,
        ExecutableResource meadowCliLoginResource)
    {
        var rls = context.ServiceProvider.GetRequiredService<ResourceLoggerService>();
        var logger = rls.GetLogger(projectResource);
        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
        var notificationService = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();
        var commandService = context.ServiceProvider.GetRequiredService<ResourceCommandService>();

        logger.LogInformation("Checking that Meadow CLI is installed.", projectResource.Name);

        // Check if the Meadow CLI is installed by running the "meadow-cli-check" resource.
        var checkMeadowCliResult = await RunExecutableResourceInteractivelyAsync(
            "Meadow CLI",
            "Checking if the Meadow CLI is installed...",
            meadowCliCheckResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(60),
            context.CancellationToken
        );

        bool meadowCliInstalled = false;

        if (checkMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
        {
            logger.LogError("Failed to start Meadow CLI check resource.");
            return checkMeadowCliResult.CommandResult!;
        }
        else if (checkMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && checkMeadowCliResult.ExitCode == 0)
        {
            logger.LogInformation("Meadow CLI is installed.");
            meadowCliInstalled = true;
        }
        else
        {
            logger.LogError("Meadow CLI install check failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow CLI is not installed or the check timed out."
            };
        }

        if (!meadowCliInstalled)
        {
            
        }

        return new ExecuteCommandResult
            {
                Success = false,
            };
    }

    private static Task<ExecuteCommandResult> UnexpectedError(ProjectResource resource)
    {
        return Task.FromResult(new ExecuteCommandResult
        {
            Success = false,
            ErrorMessage = $"An unexpected error occurred while deploying '{resource.Name}' to cloud."
        });
    }

    private static async Task<(RunExecutableResourceOutcome Outcome, ExecuteCommandResult? CommandResult, int? ExitCode)> RunExecutableResourceInteractivelyAsync(string title, string message, ExecutableResource resource, ResourceCommandService commandService, IInteractionService interactionService, ResourceNotificationService notificationService, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var startResult = await commandService.ExecuteCommandAsync(
                resource,
                "resource-start",
                cancellationToken
            );

            if (!startResult.Success)
            {
                return (RunExecutableResourceOutcome.ResourceStartFailed, startResult, null);
            }

            // TODO: We probably want to have some kind of timeout behavior here.
            linkedCts.CancelAfter(timeout);

            var pendingMessageBox = interactionService.PromptMessageBoxAsync(
                title: title,
                message: message,
                new()
                {
                    PrimaryButtonText = "Cancel",
                    Intent = MessageIntent.None
                },
                linkedCts.Token
            );

            var pendingResourceState = notificationService.WaitForResourceAsync(
                resource.Name,
                (@event) => @event.Snapshot.State == KnownResourceStates.Finished,
                linkedCts.Token
            );

            var completedTask = await Task.WhenAny(pendingMessageBox, pendingResourceState);

            if (completedTask == pendingMessageBox)
            {
                // User probably cancelled the operation.
                var interactionResult = await pendingMessageBox;
                if (interactionResult.Canceled)
                {
                    linkedCts.Cancel();
                    return (RunExecutableResourceOutcome.CancelledByUser, null, null);
                }
                else
                {
                    throw new InvalidOperationException("Should this ever happen?");
                }
            }
            else
            {
                linkedCts.Cancel();
                var resourceState = await pendingResourceState;
                return (RunExecutableResourceOutcome.ResourceFinished, null, resourceState.Snapshot.ExitCode);
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            return (RunExecutableResourceOutcome.CancelledByCaller, null, null);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == linkedCts.Token)
        {
            return (RunExecutableResourceOutcome.ResourceTimedOut, null, null);
        }
    }

    private enum RunExecutableResourceOutcome
    {
        ResourceStartFailed,
        ResourceFinished,
        ResourceTimedOut,
        CancelledByUser,
        CancelledByCaller
    }
}
