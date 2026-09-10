using System.IO;

namespace Kadr.App.Startup;

/// <summary>Папки данных и логов. Portable-режим: флаг --portable или файл portable рядом с exe.</summary>
public sealed class AppPaths
{
    public string ExeDirectory { get; }
    public string DataDirectory { get; }
    public string LogDirectory => Path.Combine(DataDirectory, "log");
    public bool IsPortable { get; }

    private AppPaths(string exeDir, string dataDir, bool portable)
    {
        ExeDirectory = exeDir;
        DataDirectory = dataDir;
        IsPortable = portable;
    }

    public static AppPaths Resolve(bool portableFlag)
    {
        var exeDir = AppContext.BaseDirectory;
        bool portable = portableFlag || File.Exists(Path.Combine(exeDir, "portable"));
        var dataDir = portable
            ? Path.Combine(exeDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "log"));
        return new AppPaths(exeDir, dataDir, portable);
    }
}
