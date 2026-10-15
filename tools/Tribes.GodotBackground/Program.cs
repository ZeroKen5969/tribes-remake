using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Tribes.GodotBackground;

/// <summary>
/// Runs a program (Godot) for development tests so that its windows stay below every other window
/// and never keep the keyboard focus. The program starts with its first window hidden; a guard
/// shows it at the bottom of the z-order, moves it back there whenever it is shown or raised, and
/// hands the focus back when Windows lets the program take the foreground (any process may, once
/// the user has been idle for the foreground lock time). Output, errors and the exit code are the
/// program's own; the program is killed when this launcher ends.
///
///   Tribes.GodotBackground &lt;exe&gt; [args...]
/// </summary>
internal static class Program
{
    private const uint GuardIntervalMilliseconds = 10;
    private const int UsageExitCode = 2;
    private const int StartFailedExitCode = 127;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: Tribes.GodotBackground <exe> [args...]");
            return UsageExitCode;
        }

        try
        {
            return Run(args[0], args[1..]);
        }
        catch (Win32Exception e)
        {
            Console.Error.WriteLine($"Tribes.GodotBackground: {e.Message}");
            return StartFailedExitCode;
        }
    }

    private static int Run(string exe, string[] args)
    {
        using KillOnCloseJob job = KillOnCloseJob.Create();
        IntPtr previousForeground = Native.GetForegroundWindow();
        using BackgroundProcess child = BackgroundProcess.Start(exe, args, job);
        using var guard = new WindowGuard(job, previousForeground);
        // Window events arrive as messages on this thread, so it pumps them while it waits.
        while (!child.WaitForExitOrMessage(GuardIntervalMilliseconds))
        {
            Native.PumpMessages();
            guard.Check();
        }

        return child.ExitCode;
    }
}

/// <summary>
/// Keeps the job's Godot windows (window class "Engine") at the bottom of the z-order and gives the
/// foreground back to the window that had it before. Each process's first Godot window starts
/// hidden and is shown at the bottom once Godot handles messages, so it never flashes on top while
/// Godot is still loading. Moves are posted, not waited for, because Godot's thread does them when
/// it next handles messages. Godot's helper windows (driver detection, input method) stay as Godot
/// made them, and several test runs at once share the bottom without pushing each other down.
/// </summary>
internal sealed class WindowGuard : IDisposable
{
    private const string GodotWindowClass = "Engine";
    private const long RepeatRequestMilliseconds = 200;
    private static readonly string[] DesktopWindowClasses = ["Progman", "WorkerW"];

    private readonly KillOnCloseJob job;
    private readonly Native.WinEventProc onEvent;
    private readonly IntPtr showHook;
    private readonly IntPtr foregroundHook;
    private readonly Dictionary<uint, IntPtr> mainWindows = [];
    private readonly Dictionary<IntPtr, long> lastRequests = [];
    private HashSet<uint> ids = [];
    private IntPtr lastOtherForeground;

    public WindowGuard(KillOnCloseJob job, IntPtr previousForeground)
    {
        this.job = job;
        lastOtherForeground = previousForeground;
        onEvent = OnWindowEvent;
        showHook = Native.SetWinEventHook(Native.EventObjectShow, Native.EventObjectShow, IntPtr.Zero, onEvent, 0, 0, Native.WinEventOutOfContext);
        foregroundHook = Native.SetWinEventHook(Native.EventSystemForeground, Native.EventSystemForeground, IntPtr.Zero, onEvent, 0, 0, Native.WinEventOutOfContext);
    }

    /// <summary>The periodic pass: shows main windows at the bottom and catches raises that send no event.</summary>
    public void Check()
    {
        ids = job.ProcessIds();
        Native.EnumWindows((window, _) =>
        {
            if (!IsOurGodotWindow(window, out uint owner))
            {
                return true;
            }

            bool isMain = mainWindows.TryAdd(owner, window) || mainWindows[owner] == window;
            if (Native.IsWindowVisible(window))
            {
                if (HasOtherWindowBelow(window))
                {
                    RequestBottom(window, show: false, force: false);
                }
            }
            else if (isMain)
            {
                RequestBottom(window, show: true, force: false);
            }

            return true;
        }, IntPtr.Zero);
        HandleForeground(Native.GetForegroundWindow());
    }

    public void Dispose()
    {
        Native.UnhookWinEvent(showHook);
        Native.UnhookWinEvent(foregroundHook);
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != Native.ObjIdWindow || window == IntPtr.Zero)
        {
            return;
        }

        if (eventType == Native.EventSystemForeground)
        {
            HandleForeground(window);
        }
        else if (IsOurGodotWindow(window, out _, refreshIds: true))
        {
            RequestBottom(window, show: false, force: true);
        }
    }

    private void HandleForeground(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        if (!IsOurs(foreground, out _, refreshIds: true))
        {
            lastOtherForeground = foreground;
            return;
        }

        if (lastOtherForeground != IntPtr.Zero)
        {
            Native.SetForegroundWindow(lastOtherForeground);
        }

        RequestBottom(foreground, show: false, force: true);
    }

    /// <summary>Posts "move to the bottom (and show)" unless the same request is still on its way.</summary>
    private void RequestBottom(IntPtr window, bool show, bool force)
    {
        long now = Environment.TickCount64;
        if (!force && lastRequests.TryGetValue(window, out long last) && now - last < RepeatRequestMilliseconds)
        {
            return;
        }

        lastRequests[window] = now;
        uint flags = Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpAsyncWindowPos;
        Native.SetWindowPos(window, Native.HwndBottom, 0, 0, 0, 0, show ? flags | Native.SwpShowWindow : flags);
    }

    private bool IsOurs(IntPtr window, out uint owner, bool refreshIds = false)
    {
        Native.GetWindowThreadProcessId(window, out owner);
        if (!ids.Contains(owner) && refreshIds)
        {
            ids = job.ProcessIds();
        }

        return ids.Contains(owner);
    }

    private bool IsOurGodotWindow(IntPtr window, out uint owner, bool refreshIds = false)
    {
        owner = 0;
        return ClassOf(window) == GodotWindowClass && IsOurs(window, out owner, refreshIds);
    }

    /// <summary>True when a visible window that is not the desktop or another Godot window lies below.</summary>
    private static bool HasOtherWindowBelow(IntPtr window)
    {
        for (IntPtr next = Native.GetWindow(window, Native.GwHwndNext); next != IntPtr.Zero; next = Native.GetWindow(next, Native.GwHwndNext))
        {
            if (!Native.IsWindowVisible(next) || Native.IsCloaked(next))
            {
                continue;
            }

            string cls = ClassOf(next);
            if (cls != GodotWindowClass && Array.IndexOf(DesktopWindowClasses, cls) < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        return Native.GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }
}

/// <summary>A child process started suspended with a hidden first window, inside the job, sharing this console's handles.</summary>
internal sealed class BackgroundProcess : IDisposable
{
    private readonly IntPtr process;

    private BackgroundProcess(IntPtr process)
    {
        this.process = process;
    }

    public int ExitCode
    {
        get
        {
            if (!Native.GetExitCodeProcess(process, out uint code))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess");
            }

            return unchecked((int)code);
        }
    }

    public static BackgroundProcess Start(string exe, IReadOnlyList<string> args, KillOnCloseJob job)
    {
        var startup = new Native.StartupInfo
        {
            cb = Marshal.SizeOf<Native.StartupInfo>(),
            dwFlags = Native.StartfUseShowWindow | Native.StartfUseStdHandles,
            wShowWindow = Native.SwHide,
            hStdInput = InheritableStdHandle(Native.StdInputHandle),
            hStdOutput = InheritableStdHandle(Native.StdOutputHandle),
            hStdError = InheritableStdHandle(Native.StdErrorHandle),
        };
        string fullExe = WithoutConsoleWrapper(Path.GetFullPath(exe));
        var commandLine = new StringBuilder();
        CommandLine.Append(commandLine, fullExe);
        foreach (string arg in args)
        {
            CommandLine.Append(commandLine, arg);
        }

        if (!Native.CreateProcessW(fullExe, commandLine, IntPtr.Zero, IntPtr.Zero, true, Native.CreateSuspended,
                IntPtr.Zero, null, ref startup, out Native.ProcessInformation info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"cannot start {exe}");
        }

        try
        {
            job.Assign(info.hProcess);
            if (Native.ResumeThread(info.hThread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread");
            }
        }
        catch
        {
            Native.TerminateProcess(info.hProcess, 1);
            Native.CloseHandle(info.hProcess);
            throw;
        }
        finally
        {
            Native.CloseHandle(info.hThread);
        }

        return new BackgroundProcess(info.hProcess);
    }

    /// <summary>
    /// Godot's "_console.exe" only starts the real executable next to it, without passing the
    /// hidden first window on, so the real one is started directly; its output still reaches the
    /// inherited handles.
    /// </summary>
    private static string WithoutConsoleWrapper(string exe)
    {
        const string wrapperSuffix = "_console.exe";
        if (!exe.EndsWith(wrapperSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return exe;
        }

        string real = exe[..^wrapperSuffix.Length] + ".exe";
        return File.Exists(real) ? real : exe;
    }

    /// <summary>Waits until the process exits (true), a message arrives or the time passes (false).</summary>
    public bool WaitForExitOrMessage(uint milliseconds) =>
        Native.MsgWaitForMultipleObjects(1, [process], false, milliseconds, Native.QsAllInput) == 0;

    public void Dispose() => Native.CloseHandle(process);

    private static IntPtr InheritableStdHandle(int which)
    {
        IntPtr handle = Native.GetStdHandle(which);
        if (handle != IntPtr.Zero && handle != Native.InvalidHandle)
        {
            Native.SetHandleInformation(handle, Native.HandleFlagInherit, Native.HandleFlagInherit);
        }

        return handle;
    }
}

/// <summary>A job object whose processes are killed when the launcher exits or is killed.</summary>
internal sealed class KillOnCloseJob : IDisposable
{
    private const int MaxListedProcesses = 64;
    private readonly IntPtr handle;

    private KillOnCloseJob(IntPtr handle)
    {
        this.handle = handle;
    }

    public static KillOnCloseJob Create()
    {
        IntPtr handle = Native.CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
        }

        var limits = new Native.ExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = Native.JobObjectLimitKillOnJobClose;
        int size = Marshal.SizeOf<Native.ExtendedLimitInformation>();
        if (!Native.SetInformationJobObject(handle, Native.JobObjectExtendedLimitInformationClass, ref limits, (uint)size))
        {
            int error = Marshal.GetLastWin32Error();
            Native.CloseHandle(handle);
            throw new Win32Exception(error, "SetInformationJobObject");
        }

        return new KillOnCloseJob(handle);
    }

    public void Assign(IntPtr process)
    {
        if (!Native.AssignProcessToJobObject(handle, process))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
        }
    }

    /// <summary>The ids of the processes in the job (the child and anything it started).</summary>
    public HashSet<uint> ProcessIds()
    {
        int headerSize = 2 * sizeof(uint);
        int size = headerSize + (MaxListedProcesses * IntPtr.Size);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ids = new HashSet<uint>();
            if (!Native.QueryInformationJobObject(handle, Native.JobObjectBasicProcessIdListClass, buffer, (uint)size, IntPtr.Zero))
            {
                return ids;
            }

            int listed = Marshal.ReadInt32(buffer, sizeof(uint));
            for (int i = 0; i < listed; i++)
            {
                ids.Add((uint)Marshal.ReadIntPtr(buffer, headerSize + (i * IntPtr.Size)).ToInt64());
            }

            return ids;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => Native.CloseHandle(handle);
}

/// <summary>Builds a command line that CommandLineToArgvW splits back into the same arguments.</summary>
internal static class CommandLine
{
    public static void Append(StringBuilder line, string arg)
    {
        if (line.Length > 0)
        {
            line.Append(' ');
        }

        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            line.Append(arg);
            return;
        }

        line.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            line.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            line.Append(c);
            backslashes = 0;
        }

        line.Append('\\', backslashes * 2);
        line.Append('"');
    }
}

internal static class Native
{
    public const short SwHide = 0;
    public const int ObjIdWindow = 0;
    public const uint GwHwndNext = 2;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectShow = 0x8002;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint QsAllInput = 0x04FF;
    public const uint PmRemove = 0x0001;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpAsyncWindowPos = 0x4000;
    public const int DwmwaCloaked = 14;
    public static readonly IntPtr HwndBottom = new(1);
    public const int StartfUseShowWindow = 0x1;
    public const int StartfUseStdHandles = 0x100;
    public const uint CreateSuspended = 0x4;
    public const int StdInputHandle = -10;
    public const int StdOutputHandle = -11;
    public const int StdErrorHandle = -12;
    public const uint HandleFlagInherit = 0x1;
    public const uint JobObjectLimitKillOnJobClose = 0x2000;
    public const int JobObjectBasicProcessIdListClass = 3;
    public const int JobObjectExtendedLimitInformationClass = 9;
    public static readonly IntPtr InvalidHandle = new(-1);

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(out Message message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    /// <summary>Hidden by the window manager although marked visible (suspended store apps, other desktops).</summary>
    public static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static void PumpMessages()
    {
        while (PeekMessageW(out Message message, IntPtr.Zero, 0, 0, PmRemove))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
}
