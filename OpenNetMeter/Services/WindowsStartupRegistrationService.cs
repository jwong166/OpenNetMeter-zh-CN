// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using OpenNetMeter.Localization;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenNetMeter.PlatformAbstractions;
using OpenNetMeter.Utilities;
using TaskScheduler = Microsoft.Win32.TaskScheduler;

namespace OpenNetMeter.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string TaskFolder = "OpenNetMeter";
    private const string TaskName = "OpenNetMeterLogon";

    public bool IsEnabled()
    {
        try
        {
            TaskScheduler.TaskFolder folder = TaskScheduler.TaskService.Instance.RootFolder.SubFolders[TaskFolder];
            return folder.Tasks.Exists(TaskName);
        }
        catch
        {
            return false;
        }
    }

    private void RemoveONMStartupTask()
    {
        try
        {
            TaskScheduler.TaskFolder rootFolder = TaskScheduler.TaskService.Instance.RootFolder;
            if (rootFolder.SubFolders.Exists(TaskFolder))
            {
                TaskScheduler.TaskFolder folder = rootFolder.SubFolders[TaskFolder];
                for (int i = folder.Tasks.Count - 1; i >= 0; i--)
                {
                    folder.DeleteTask(folder.Tasks[i].Name);
                }
                rootFolder.DeleteFolder(TaskFolder);
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error while removing startup task registration", ex);
        }
    }

    /// <summary>
    /// 启动时自检：任务里的可执行文件路径是否仍指向当前 exe。
    /// 用户把 exe 挪走（下载目录→安装目录、换盘、重新解压）后，旧路径会在登录时静默失败，
    /// 任务计划程序里只留下 0x80070002（找不到文件），界面上却仍显示"已开启"。
    /// </summary>
    public void EnsureUpToDate(bool startMinimized)
    {
        try
        {
            TaskScheduler.TaskService service = TaskScheduler.TaskService.Instance;
            TaskScheduler.TaskFolder rootFolder = service.RootFolder;
            if (!rootFolder.SubFolders.Exists(TaskFolder))
                return;

            TaskScheduler.TaskFolder folder = rootFolder.SubFolders[TaskFolder];
            if (!folder.Tasks.Exists(TaskName))
                return;

            (string expectedPath, string? expectedArguments) = ResolveLaunchCommand(startMinimized);
            string expectedArgs = expectedArguments ?? string.Empty;

            foreach (TaskScheduler.Action action in folder.Tasks[TaskName].Definition.Actions)
            {
                if (action is not TaskScheduler.ExecAction execAction)
                    continue;

                bool pathMatches = string.Equals(execAction.Path, expectedPath, StringComparison.OrdinalIgnoreCase);
                bool argsMatch = string.Equals(execAction.Arguments ?? string.Empty, expectedArgs, StringComparison.OrdinalIgnoreCase);
                if (pathMatches && argsMatch)
                    return;

                EventLogger.Info($"Startup task points to '{execAction.Path}', re-registering for '{expectedPath}'");
                CreateONMStartupTask(startMinimized);
                return;
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to validate the startup task registration", ex);
        }
    }

    public void SetEnabled(bool enabled, bool startMinimized)
    {
        if (enabled)
        {
            CreateONMStartupTask(startMinimized);
        }
        else
        {
            RemoveONMStartupTask();
        }
    }

    private static void CreateONMStartupTask(bool startMinimized)
    {
        try
        {
            TaskScheduler.TaskFolder rootFolder = TaskScheduler.TaskService.Instance.RootFolder;
            TaskScheduler.TaskFolder folder;
            if (rootFolder.SubFolders.Exists(TaskFolder))
            {
                folder = rootFolder.SubFolders[TaskFolder];
                for (int i = folder.Tasks.Count - 1; i >= 0; i--)
                {
                    folder.DeleteTask(folder.Tasks[i].Name);
                }
            }
            else
            {
                folder = rootFolder.CreateFolder(TaskFolder);
            }

            TaskScheduler.TaskDefinition td = TaskScheduler.TaskService.Instance.NewTask();
            td.RegistrationInfo.Description = Loc.S("TaskDesc");
            td.Principal.RunLevel = TaskScheduler.TaskRunLevel.Highest;
            td.Principal.LogonType = TaskScheduler.TaskLogonType.InteractiveToken;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.Compatibility = TaskScheduler.TaskCompatibility.V2_3;

            TaskScheduler.LogonTrigger logonTrigger = new TaskScheduler.LogonTrigger
            {
                Enabled = true,
                UserId = null
            };
            td.Triggers.Add(logonTrigger);

            (string path, string? arguments) = ResolveLaunchCommand(startMinimized);

            TaskScheduler.ExecAction action = new TaskScheduler.ExecAction
            {
                Path = path,
                Arguments = arguments ?? string.Empty
            };

            td.Actions.Add(action);

            folder.RegisterTaskDefinition(
                TaskName,
                td,
                TaskScheduler.TaskCreation.CreateOrUpdate,
                null,
                null,
                td.Principal.LogonType);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error creating startup task", ex);
        }
    }

    private static (string path, string? arguments) ResolveLaunchCommand(bool startMinimized)
    {
        string? processPath = Environment.ProcessPath;
        string minimizedArgument = startMinimized ? " /StartMinimized" : string.Empty;

        // MSIX: launching the exe by path runs it without package identity (separate AppData, so a second
        // database and settings file), and that path changes with every package update. The execution alias
        // keeps identity and a stable path. Requires the windows.appExecutionAlias manifest extension.
        if (IsPackaged())
        {
            string aliasPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", ExecutionAlias);

            if (File.Exists(aliasPath))
                return (aliasPath, startMinimized ? "/StartMinimized" : null);

            EventLogger.Error($"Packaged app has no execution alias at '{aliasPath}'; startup task will launch without package identity.");
        }

        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, startMinimized ? "/StartMinimized" : null);
        }

        string baseDirectory = AppContext.BaseDirectory;
        string assemblyFileName = $"{AppDomain.CurrentDomain.FriendlyName}.dll";
        string managedEntryPoint = Path.Combine(baseDirectory, assemblyFileName);

        if (File.Exists(managedEntryPoint))
        {
            return ("dotnet", $"\"{managedEntryPoint}\"{minimizedArgument}");
        }

        string fallbackExe = Path.Combine(AppContext.BaseDirectory, "OpenNetMeter.exe");
        return (fallbackExe, startMinimized ? "/StartMinimized" : null);
    }

    private static bool IsPackaged()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    // Must match <desktop:ExecutionAlias Alias="..."/> in the MSIX manifest.
    private const string ExecutionAlias = "OpenNetMeter.exe";
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}

