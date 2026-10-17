using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetPad.Application;
using NetPad.Apps;
using NetPad.Apps.Plugins;
using NetPad.Apps.Security;
using NetPad.Compilation;
using NetPad.Configuration;
using NetPad.Data;
using NetPad.DotNet;
using NetPad.Scripts;
using NetPad.Sessions;
using NetPad.Sessions.Workspaces;

namespace NetPad.BackgroundServices;

/// <summary>
/// Runs setup operations when the app starts and cleanup when the app is stopping.
/// </summary>
public class AppSetupAndCleanupBackgroundService(
    ISession session,
    IScriptRepository scriptRepository,
    IAutoSaveScriptRepository autoSaveScriptRepository,
    IWorkspaceStore workspaceStore,
    ITrivialDataStore trivialDataStore,
    IPluginManager pluginManager,
    IAppStatusMessagePublisher appStatusMessagePublisher,
    IDotNetInfo dotNetInfo,
    ILoggerFactory loggerFactory)
    : BackgroundService(loggerFactory)
{
    protected override async Task StartingAsync(CancellationToken stoppingToken)
    {
        // Clean up temp directories and stale connection files left behind by previous instances
        _ = Task.Run(() =>
        {
            AppDataProvider.CleanUpStaleTempDirectories();
            ConnectionFileManager.CleanupStale();
        }, stoppingToken);

        // Pre-warm framework assembly cache on a background thread to avoid blocking startup
        _ = Task.Run(() =>
        {
            var frameworkVersions = dotNetInfo.GetDotNetSdkVersions()
                .Select(sdk => sdk.GetFrameworkVersion())
                .Distinct();

            foreach (var version in frameworkVersions)
            {
                try
                {
                    var root = dotNetInfo.LocateDotNetRootDirectoryForFramework(version);
                    if (root != null)
                    {
                        FrameworkAssemblies.PreWarm(new IO.DirectoryPath(root), version);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Failed to pre-warm framework assembly cache for {Framework}", version);
                }
            }
        }, stoppingToken);

        // Open auto-saved scripts
        var autoSavedScripts = await autoSaveScriptRepository.GetScriptsAsync();
        await session.OpenAsync(autoSavedScripts, false);

        WorkspaceDocument? workspaceDocument = null;
        try
        {
            workspaceDocument = await workspaceStore.LoadAsync(stoppingToken);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error loading workspace state");
        }

        var activeWorkspace = workspaceDocument?.Workspaces
            .FirstOrDefault(workspace => workspace.Id == workspaceDocument.ActiveWorkspaceId);

        try
        {
            HashSet<Guid>? previousSessionOpenScriptIds;
            if (workspaceDocument?.IsInitialized == true)
            {
                previousSessionOpenScriptIds = activeWorkspace?.Documents
                    .Where(document => document.Kind == "script")
                    .Select(document => Guid.TryParse(document.Id, out var id) ? id : (Guid?)null)
                    .Where(id => id.HasValue && !session.IsOpen(id.Value))
                    .Select(id => id!.Value)
                    .ToHashSet();
            }
            else
            {
                // Import the legacy session list only until the first workspace snapshot is saved.
                previousSessionOpenScriptIds = trivialDataStore.Get<Guid[]>(Session.OpenScriptsSaveKey)?
                    .Where(sid => !session.IsOpen(sid))
                    .ToHashSet();
            }

            if (previousSessionOpenScriptIds is { Count: > 0 })
            {
                var scripts = await scriptRepository.GetAsync(previousSessionOpenScriptIds);
                await session.OpenAsync(scripts, false);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error opening scripts from previous session");
        }

        var activeDocumentId = activeWorkspace?.ActiveDocumentId;
        if (workspaceDocument?.IsInitialized == true
            && activeDocumentId != null
            && Guid.TryParse(activeDocumentId, out var activeScriptId)
            && session.IsOpen(activeScriptId))
        {
            await session.ActivateAsync(activeScriptId);
        }
        else if (workspaceDocument?.IsInitialized == true
                 && activeDocumentId != null
                 && activeWorkspace?.Documents.Any(document => document.Id == activeDocumentId && document.Kind == "file") == true)
        {
            await session.ActivateAsync(null);
        }
        else
        {
            await session.ActivateBestCandidateAsync();
        }
    }

    protected override async Task StoppingAsync(CancellationToken cancellationToken)
    {
        _ = appStatusMessagePublisher.PublishTransientAsync("Closing...");

        var environments = session.GetOpened();

        try
        {
            var scriptIds = environments.Select(e => e.Script.Id).ToArray();
            await session.CloseAsync(scriptIds, false, false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error closing environments");
        }

        Try.Run(AppDataProvider.ProcessTempDirectoryPath.DeleteIfExists);
        Try.Run(ConnectionFileManager.Delete);

        foreach (var registration in pluginManager.PluginRegistrations)
        {
            try
            {
                await registration.Plugin.CleanupAsync();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error cleaning up plugin: {PluginName} ({PluginId})",
                    registration.Plugin.Name,
                    registration.Plugin.Id);
            }
        }
    }
}
