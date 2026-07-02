using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace L2InterludeUpdater;

internal sealed record LauncherInstallResult(string LauncherPath, string ShortcutPath);

internal static class LauncherInstallation
{
    public const string InstalledFileName = "L2 Hamburgo Launcher.exe";
    public const string ShortcutFileName = "L2 Hamburgo.lnk";

    public static LauncherInstallResult EnsureInstalled(
        string clientDirectory,
        string? shortcutDirectory = null)
    {
        var clientRoot = Path.GetFullPath(clientDirectory);
        Directory.CreateDirectory(clientRoot);

        var sourcePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo determinar la ruta del launcher actual.");
        var launcherPath = Path.Combine(clientRoot, InstalledFileName);
        if (!PathsEqual(sourcePath, launcherPath) && !FilesMatch(sourcePath, launcherPath))
        {
            var temporaryPath = launcherPath + ".new";
            File.Copy(sourcePath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, launcherPath, overwrite: true);
        }

        shortcutDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(shortcutDirectory);
        var shortcutPath = Path.Combine(shortcutDirectory, ShortcutFileName);
        var gamePath = Path.Combine(clientRoot, "system-e", "l2.exe");
        CreateShortcut(
            shortcutPath,
            launcherPath,
            clientRoot,
            File.Exists(gamePath) ? gamePath : launcherPath);

        return new LauncherInstallResult(launcherPath, shortcutPath);
    }

    private static bool FilesMatch(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return false;
        }

        var source = new FileInfo(sourcePath);
        var destination = new FileInfo(destinationPath);
        if (source.Length != destination.Length)
        {
            return false;
        }

        using var sourceStream = File.OpenRead(sourcePath);
        using var destinationStream = File.OpenRead(destinationPath);
        return SHA256.HashData(sourceStream).SequenceEqual(SHA256.HashData(destinationStream));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        var shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            shellLink.SetPath(targetPath);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetDescription("Abrir L2 Hamburgo");
            shellLink.SetIconLocation(iconPath, 0);
            shellLink.SetShowCmd(1);
            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
