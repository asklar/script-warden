using ScriptWarden.Core;
using ScriptWarden.Web;

namespace ScriptWarden.Commands;

/// <summary>Deletes all captured audit data (events + scripts) from the readable roots.</summary>
internal static class ClearCommand
{
    public static int Run(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        bool yes = opts.Has("yes") || opts.Has("y") || opts.Has("force");

        var roots = DataRoots.ForViewer().Where(r => r.Readable).ToList();
        if (roots.Count == 0)
        {
            Console.WriteLine("No readable audit roots found.");
            return 0;
        }

        if (!yes)
        {
            Console.WriteLine("This will permanently delete captured audit data from:");
            foreach (ResolvedRoot r in roots)
            {
                Console.WriteLine($"  {r.Origin}: {r.Path}");
            }
            Console.Write("Continue? [y/N] ");
            string? answer = Console.ReadLine();
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }

        int totalEvents = 0;
        int totalScripts = 0;
        foreach (ResolvedRoot r in roots)
        {
            (int e, int s) = AuditStore.ClearRoot(r.Path);
            totalEvents += e;
            totalScripts += s;
            Console.WriteLine($"  {r.Origin}: cleared {e} event(s), {s} script(s)");
        }

        // Remove the viewer's SQLite index (rebuilt on next serve/list). Best-effort: a running
        // viewer may hold it open, in which case its own clear endpoint empties it instead.
        DeleteIndexFiles();

        Console.WriteLine($"Cleared {totalEvents} event(s) and {totalScripts} script(s).");
        return 0;
    }

    private static void DeleteIndexFiles()
    {
        string db = SqliteIndex.DefaultDbPath();
        foreach (string path in new[] { db, db + "-wal", db + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best-effort; likely held open by a running `serve`
            }
        }
    }
}
