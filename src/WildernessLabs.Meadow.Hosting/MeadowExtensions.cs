#pragma warning disable ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using System.ComponentModel.Design;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class MeadowExtensions
{
    public static IResourceBuilder<ProjectResource> AddMeadowProject<TProject>(this IDistributedApplicationBuilder builder, [ResourceName] string name) where TProject : IProjectMetadata, new()
    {
        var project = builder.AddProject<TProject>(name).WithExplicitStart();

        var projectMetadata = project.Resource.GetProjectMetadata();
        var projectPath = projectMetadata.ProjectPath;
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException($"Project path '{projectPath}' does not have a valid directory.");

        var meadowCliCheck = builder.AddExecutable(
            "meadow-cli-check",
            "dotnet",
            ".",
            ["tool", "run", "meadow"])
            .WithExplicitStart();

        var meadowCliInstall = builder.AddExecutable(
            "meadow-cli-install",
            "dotnet",
            ".",
            ["tool", "install", "--local", "WildernessLabs.Meadow.Cli", "--prerelease"])
            .WithExplicitStart();

        var meadowCliLoginCheck = builder.AddExecutable(
            "meadow-cli-login-check",
            "dotnet",
            ".",
            ["tool", "run", "meadow", "cloud", "collection", "list"])
            .WithExplicitStart();

        var meadowCliLogin = builder.AddExecutable(
            "meadow-login",
            "dotnet",
            ".",
            ["tool", "run", "meadow", "login"])
            .WithExplicitStart();

        var meadowCliPortList = builder.AddExecutable(
            "meadow-cli-port-list",
            "dotnet",
            ".",
            ["tool", "run", "meadow", "port", "list"])
            .WithExplicitStart();

        var meadowCloudCollectionList = builder.AddExecutable(
            "meadow-cli-cloud-collection-list",
            "dotnet",
            ".",
            ["tool", "run", "meadow", "cloud", "collection", "list"])
            .WithExplicitStart();

        var meadowCliUninstall = builder.AddExecutable(
            "meadow-cli-uninstall",
            "dotnet",
            ".",
            ["tool", "uninstall", "--local", "WildernessLabs.Meadow.Cli"])
            .WithExplicitStart();

        var meadowCliLogout = builder.AddExecutable(
            "meadow-cli-logout",
            "dotnet",
            ".",
            ["tool", "run", "meadow", "logout"])
            .WithExplicitStart();

        var meadowCloudPackageCreate = builder.AddExecutable(
            "meadow-cloud-package-create",
            "dotnet", projectDirectory,
            ["tool", "run", "meadow", "cloud", "package", "create"])
            .WithExplicitStart();

        meadowCloudPackageCreate.WithArgs(context =>
        {
            if (meadowCloudPackageCreate.Resource.TryGetLastAnnotation<MeadowPackageNameAnnotation>(out var annotation))
            {
                context.Args.Add("--");
                context.Args.Add("--name");
                context.Args.Add($"{annotation.PackageName}.mpak");
            }
            else
            {
                throw new InvalidOperationException("MeadowPackageNameAnnotation is required for meadow-cloud-package-create.");
            }
        });

        var meadowCloudPackageUpload = builder.AddExecutable(
            "meadow-cloud-package-upload",
            "dotnet", projectDirectory,
            ["tool", "run", "meadow", "cloud", "package", "upload"])
            .WithExplicitStart();

        meadowCloudPackageUpload.WithArgs(context =>
        {
            if (meadowCloudPackageUpload.Resource.TryGetLastAnnotation<MeadowPackageNameAnnotation>(out var annotation))
            {
                context.Args.Add("--");
                context.Args.Add($".\\mpak\\{annotation.PackageName}.mpak");
            }
            else
            {
                throw new InvalidOperationException("MeadowPackageNameAnnotation is required for meadow-cloud-package-upload.");
            }
        });

        var meadowCloudPackagePublish = builder.AddExecutable(
            "meadow-cloud-package-publish",
            "dotnet",
            projectDirectory,
            ["tool", "run", "meadow", "cloud", "package", "publish"])
            .WithExplicitStart();

        meadowCloudPackagePublish.WithArgs(context =>
        {
            if (meadowCloudPackagePublish.Resource.TryGetLastAnnotation<MeadowPackagePublishAnnotation>(out var annotation))
            {
                context.Args.Add("--");
                context.Args.Add(annotation.PackageId);
                context.Args.Add("--collectionId");
                context.Args.Add(annotation.CollectionId);
            }
            else
            {
                throw new InvalidOperationException("MeadPackagePublishAnnotation is required for meadow-cloud-package-publish.");
            }
        });

        project.WithCommand("deploy-firmware-to-cloud", "Deploy to Cloud",
            (context) => DeployFirmwareToCloudAsync(
                context,
                project.Resource,
                meadowCliCheck.Resource,
                meadowCliInstall.Resource,
                meadowCliLoginCheck.Resource,
                meadowCliLogin.Resource,
                meadowCloudCollectionList.Resource,
                meadowCloudPackageCreate.Resource,
                meadowCloudPackageUpload.Resource,
                meadowCloudPackagePublish.Resource
                ));

        meadowCliCheck.WithParentRelationship(project);
        meadowCliInstall.WithParentRelationship(project);
        meadowCliLoginCheck.WithParentRelationship(project);
        meadowCliLogin.WithParentRelationship(project);
        meadowCliPortList.WithParentRelationship(project);
        meadowCliUninstall.WithParentRelationship(project);
        meadowCliLogout.WithParentRelationship(project);
        meadowCloudCollectionList.WithParentRelationship(project);
        meadowCloudPackageCreate.WithParentRelationship(project);
        meadowCloudPackageUpload.WithParentRelationship(project);
        meadowCloudPackagePublish.WithParentRelationship(project);

        return project;
    }

    private static async Task<ExecuteCommandResult> DeployFirmwareToCloudAsync(
        ExecuteCommandContext context,
        ProjectResource projectResource,
        ExecutableResource meadowCliCheckResource,
        ExecutableResource meadowCliInstallResource,
        ExecutableResource meadlowCliLoginCheckResource,
        ExecutableResource meadowCliLoginResource,
        ExecutableResource meadowCloudCollectionListResource,
        ExecutableResource meadowCloudPackageCreateResource,
        ExecutableResource meadowCloudPackageUploadResource,
        ExecutableResource meadowCloudPackagePublishResource)
    {
        var rls = context.ServiceProvider.GetRequiredService<ResourceLoggerService>();
        var logger = rls.GetLogger(projectResource);
        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
        var notificationService = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();
        var commandService = context.ServiceProvider.GetRequiredService<ResourceCommandService>();

        logger.LogInformation("Checking that Meadow CLI is installed.");

        // Check if the Meadow CLI is installed by running the "meadow-cli-check" resource.
        var checkMeadowCliResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
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
        else if (checkMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceFinished)
        {
            meadowCliInstalled = checkMeadowCliResult.ExitCode == 0;
        }
        else
        {
            logger.LogInformation("Meadow CLI install check failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow CLI is not installed or the check timed out."
            };
        }

        if (!meadowCliInstalled)
        {
            var confirmMeadowCliInstall = await interactionService.PromptConfirmationAsync(
                "Deploy to Cloud",
                "The Meadow CLI is not installed. Would you like to install it now?",
                new()
                {
                    PrimaryButtonText = "Install",
                    SecondaryButtonText = "Cancel",
                    Intent = MessageIntent.Confirmation
                },
                context.CancellationToken
                );

            if (confirmMeadowCliInstall.Canceled)
            {
                return new ExecuteCommandResult
                {
                    Success = false,
                    ErrorMessage = "Meadow CLI installation was cancelled by the user."
                };
            }
        }

        if (!meadowCliInstalled)
        {
            var installMeadowCliResult = await RunExecutableResourceInteractivelyAsync(
                "Deploy to Cloud",
                "Installing Meadow CLI...",
                meadowCliInstallResource,
                commandService,
                interactionService,
                notificationService,
                TimeSpan.FromSeconds(60),
                context.CancellationToken
            );

            if (installMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
            {
                logger.LogError("Failed to start Meadow CLI install resource.");
                return installMeadowCliResult.CommandResult!;
            }
            else if (installMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && installMeadowCliResult.ExitCode != 0)
            {
                logger.LogError("Meadow CLI installation failed with exit code {ExitCode}.", installMeadowCliResult.ExitCode);
                return installMeadowCliResult.CommandResult!;
            }
            else if (installMeadowCliResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && installMeadowCliResult.ExitCode == 0)
            {
                logger.LogInformation("Meadow CLI installed successfully.");
            }
            else
            {
                logger.LogInformation("Meadow CLI install failed or timed out");
                return new ExecuteCommandResult
                {
                    Success = false,
                    ErrorMessage = "Meadow CLI installation failed or timed out."
                };
            }
        }

        var checkMeadowCloudLoginResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
            "Check Meadow CLI login status...",
            meadlowCliLoginCheckResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(60),
            context.CancellationToken
        );

        bool meadowCloudLoggedIn = false;

        if (checkMeadowCloudLoginResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
        {
            logger.LogError("Failed to start Meadow CLI login check resource.");
            return checkMeadowCloudLoginResult.CommandResult!;
        }
        else if (checkMeadowCloudLoginResult.Outcome == RunExecutableResourceOutcome.ResourceFinished)
        {
            meadowCloudLoggedIn = checkMeadowCloudLoginResult.ExitCode == 0;
        }
        else
        {
            logger.LogInformation("Meadow CLI login check failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow CLI login check failed or the check timed out."
            };
        }

        if (!meadowCloudLoggedIn)
        {
            var confirmMeadowCliLogin = await interactionService.PromptConfirmationAsync(
                "Deploy to Cloud",
                "The Meadow CLI is logged in. Would you like to login now?",
                new()
                {
                    PrimaryButtonText = "Login",
                    SecondaryButtonText = "Cancel",
                    Intent = MessageIntent.Confirmation
                },
                context.CancellationToken
                );

            if (confirmMeadowCliLogin.Canceled)
            {
                return new ExecuteCommandResult
                {
                    Success = false,
                    ErrorMessage = "Meadow CLI login was cancelled by the user."
                };
            }
        }

        if (!meadowCloudLoggedIn)
        {
            var meadowCliLoginResult = await RunExecutableResourceInteractivelyAsync(
                "Deploy to Cloud",
                "Logging into Meadow Cloud (check browser tab)...",
                meadowCliLoginResource,
                commandService,
                interactionService,
                notificationService,
                TimeSpan.FromSeconds(600),
                context.CancellationToken
            );

            if (meadowCliLoginResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
            {
                logger.LogError("Failed to start Meadow CLI login resource.");
                return meadowCliLoginResult.CommandResult!;
            }
            else if (meadowCliLoginResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCliLoginResult.ExitCode != 0)
            {
                logger.LogError("Meadow CLI login failed with exit code {ExitCode}.", meadowCliLoginResult.ExitCode);
                return meadowCliLoginResult.CommandResult!;
            }
            else if (meadowCliLoginResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCliLoginResult.ExitCode == 0)
            {
                logger.LogInformation("Meadow CLI logged in successfully.");
            }
            else
            {
                logger.LogInformation("Meadow CLI login failed or timed out");
                return new ExecuteCommandResult
                {
                    Success = false,
                    ErrorMessage = "Meadow CLI login failed or timed out."
                };
            }
        }

        var meadowCloudCollectionListResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
            "Fetching device collections from Meadow Cloud...",
            meadowCloudCollectionListResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(600),
            context.CancellationToken
        );

        // HACK: File feature request on Meadow CLI to return JSON output to file.
        var collectionsListLogLinesBatches = rls.GetAllAsync(meadowCloudCollectionListResource);
        var collections = new Dictionary<string, string>();
        await foreach (var logLineBatch in collectionsListLogLinesBatches.WithCancellation(context.CancellationToken))
        {
            foreach (var logLine in logLineBatch)
            {
                if (logLine.Content.Contains(" | "))
                {
                    var parts = logLine.Content.Split(" | ");
                    if (parts.Length == 2)
                    {
                        var collectionId = parts[0].Trim().Split(' ')[2];
                        var collectionName = parts[1].Trim();
                        collections[collectionId] = collectionName;
                    }
                }
            }
        }

        var collectionInput = new InteractionInput()
        {
            InputType = InputType.Choice,
            Label = "Select a device collection",
            Placeholder = "Device collection",
            Required = true,
            Options = collections.ToArray(),
        };

        var packageNameInput = new InteractionInput()
        {
            InputType = InputType.Text,
            Label = "Package name",
            Placeholder = "Enter the package name to crate",
            Required = true,
        };

        var deployPromptResults = await interactionService.PromptInputsAsync(
            "Deploy to Cloud",
            "Configure deployment options.",
            [collectionInput, packageNameInput],
            new()
            {
                PrimaryButtonText = "Deploy",
                SecondaryButtonText = "Cancel",
            },
            context.CancellationToken);

        if (deployPromptResults.Canceled)
        {
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Deployment was cancelled by the user."
            };
        }

        var selectedCollection = deployPromptResults.Data[0].Value;
        var packageName = deployPromptResults.Data[1].Value;

        meadowCloudPackageCreateResource.Annotations.Add(new MeadowPackageNameAnnotation(packageName!));

        // Hack: Delete postlink_bin folder if it exists as it can cause issues with the package creation.
        var postLinkBinPath = Path.Combine(meadowCloudPackageCreateResource.WorkingDirectory, "postlink_bin");
        if (Directory.Exists(postLinkBinPath))
        {
            Directory.Delete(postLinkBinPath, true);
        }
        var binPath = Path.Combine(meadowCloudPackageCreateResource.WorkingDirectory, "bin");
        if (Directory.Exists(binPath))
        {
            Directory.Delete(binPath, true);
        }

        var objPath = Path.Combine(meadowCloudPackageCreateResource.WorkingDirectory, "obj");
        if (Directory.Exists(objPath))
        {
            Directory.Delete(objPath, true);
        }

        var infoFile = Path.Combine(meadowCloudPackageCreateResource.WorkingDirectory, "info.json");
        if (File.Exists(infoFile))
        {
            File.Delete(infoFile);
        }

        var meadowCloudPackageCreateResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
            "Creating package for deployment...",
            meadowCloudPackageCreateResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(600),
            context.CancellationToken
            );

        if (meadowCloudPackageCreateResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
        {
            logger.LogError("Failed to start Meadow CLI cloud package create resource.");
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackageCreateResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackageCreateResult.ExitCode != 0)
        {
            logger.LogError("Meadow CLI cloud package create failed with exit code {ExitCode}.", meadowCloudPackageCreateResult.ExitCode);
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackageCreateResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackageCreateResult.ExitCode == 0)
        {
            logger.LogInformation("Meadow cloud package created successfully.");
        }
        else
        {
            logger.LogInformation("Meadow cloud package create failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow cloud package create failed or timed out."
            };
        }

        meadowCloudPackageUploadResource.Annotations.Add(new MeadowPackageNameAnnotation(packageName));

        var meadowCloudPackageUploadResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
            "Uploading package for deployment...",
            meadowCloudPackageUploadResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(600),
            context.CancellationToken
            );

        if (meadowCloudPackageUploadResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
        {
            logger.LogError("Failed to start Meadow CLI cloud package upload resource.");
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackageUploadResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackageUploadResult.ExitCode != 0)
        {
            logger.LogError("Meadow CLI cloud package upload failed with exit code {ExitCode}.", meadowCloudPackageUploadResult.ExitCode);
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackageUploadResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackageUploadResult.ExitCode == 0)
        {
            logger.LogInformation("Meadow cloud package uploaded successfully.");
        }
        else
        {
            logger.LogInformation("Meadow cloud package upload failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow cloud package upload failed or timed out."
            };
        }

        string? packageId = null;
        var packageUploadLogLinesBatches = rls.GetAllAsync(meadowCloudPackageUploadResource);
        await foreach (var logLineBatch in packageUploadLogLinesBatches.WithCancellation(context.CancellationToken))
        {
            foreach (var logLine in logLineBatch)
            {
                if (logLine.Content.Contains("Package Id: "))
                {
                    var parts = logLine.Content.Split("Package Id: ");
                    if (parts.Length == 2)
                    {
                        packageId = parts[1].Trim();
                    }
                }
            }
        }

        meadowCloudPackagePublishResource.Annotations.Add(new MeadowPackagePublishAnnotation(packageId!, selectedCollection!));

        var meadowCloudPackagePublishResult = await RunExecutableResourceInteractivelyAsync(
            "Deploy to Cloud",
            "Uploading package for deployment...",
            meadowCloudPackagePublishResource,
            commandService,
            interactionService,
            notificationService,
            TimeSpan.FromSeconds(600),
            context.CancellationToken
            );

        if (meadowCloudPackagePublishResult.Outcome == RunExecutableResourceOutcome.ResourceStartFailed)
        {
            logger.LogError("Failed to start Meadow CLI cloud package publish resource.");
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackagePublishResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackagePublishResult.ExitCode != 0)
        {
            logger.LogError("Meadow CLI cloud package publish failed with exit code {ExitCode}.", meadowCloudPackagePublishResult.ExitCode);
            return meadowCloudPackageCreateResult.CommandResult!;
        }
        else if (meadowCloudPackagePublishResult.Outcome == RunExecutableResourceOutcome.ResourceFinished && meadowCloudPackagePublishResult.ExitCode == 0)
        {
            logger.LogInformation("Meadow cloud package published successfully.");
        }
        else
        {
            logger.LogInformation("Meadow cloud package publish failed or timed out");
            return new ExecuteCommandResult
            {
                Success = false,
                ErrorMessage = "Meadow cloud package publish failed or timed out."
            };
        }

        await interactionService.PromptConfirmationAsync(
            "Deploy to Cloud",
            $"Deployment to Meadow Cloud completed successfully. <a href=\"https://www.meadowcloud.co/mitchdenny9044/devices?collectionId={selectedCollection}\">View device collection</a>",
            new()
            {
                EscapeMessageHtml = false,
                PrimaryButtonText = "OK",
                ShowSecondaryButton = false,
                ShowDismiss = false
            },
            context.CancellationToken);

        return new ExecuteCommandResult
        {
            Success = true,
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

    private sealed class MeadowPackageNameAnnotation(string packageName) : IResourceAnnotation
    {
        public string PackageName { get; } = packageName;
    }

    private sealed class MeadowPackageIdAnnotation(string packageId) : IResourceAnnotation
    {
        public string PackageId { get; } = packageId;
    }

    private sealed class MeadowPackagePublishAnnotation(string packageId, string collectionId) : IResourceAnnotation
    {
        public string PackageId { get; } = packageId;
        public string CollectionId { get; } = collectionId;
    }
}
