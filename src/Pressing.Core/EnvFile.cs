namespace Pressing.Core;

public static class EnvFile
{
    public static void Load(string? startDirectory = null)
    {
        try
        {
            DirectoryInfo? directory = new(startDirectory ?? Directory.GetCurrentDirectory());
            for (int depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, ".env");
                if (File.Exists(path))
                {
                    LoadFile(path);
                    return;
                }
            }
        }
        catch
        {
            // Une configuration absente ne doit jamais empêcher le démarrage.
        }
    }

    public static void LoadFile(string path)
    {
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
