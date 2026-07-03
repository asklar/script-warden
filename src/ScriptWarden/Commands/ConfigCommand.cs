using ScriptWarden.Core;

namespace ScriptWarden.Commands;

/// <summary>Views and edits the audit configuration (enable/disable, exclusions).</summary>
internal static class ConfigCommand
{
    private static readonly HashSet<string> ValueKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "exclude-parent", "remove-parent", "exclude-image", "remove-image",
    };

    public static int Run(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);
        string root = DataRoots.CurrentUserRoot();
        WardenConfig config = ConfigStore.Load(root);

        bool changed = false;

        if (opts.Has("enable"))
        {
            config.Enabled = true;
            changed = true;
        }
        if (opts.Has("disable"))
        {
            config.Enabled = false;
            changed = true;
        }

        changed |= AddUnique(config.ExcludedParents, opts.Get("exclude-parent"));
        changed |= Remove(config.ExcludedParents, opts.Get("remove-parent"));
        changed |= AddUnique(config.ExcludedImages, opts.Get("exclude-image"));
        changed |= Remove(config.ExcludedImages, opts.Get("remove-image"));

        if (changed)
        {
            ConfigStore.Save(root, config);
        }

        Console.WriteLine($"Config: {ConfigStore.ConfigPath(root)}");
        Console.WriteLine($"  enabled          : {config.Enabled}");
        Console.WriteLine($"  excluded parents : {Format(config.ExcludedParents)}");
        Console.WriteLine($"  excluded images  : {Format(config.ExcludedImages)}");
        if (changed)
        {
            Console.WriteLine("(saved)");
        }
        return 0;
    }

    private static bool AddUnique(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        value = value.Trim();
        if (list.Exists(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        list.Add(value);
        return true;
    }

    private static bool Remove(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        return list.RemoveAll(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
    }

    private static string Format(List<string> list) => list.Count == 0 ? "(none)" : string.Join(", ", list);
}
