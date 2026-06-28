using System.Text.Json;
using CloakBrowser;

// CLI for cloakbrowser - download and manage the stealth Chromium binary.
// Direct port of Python cloakbrowser/__main__.py.
//
// Usage:
//   cloakbrowser install      # Download binary (with progress)
//   cloakbrowser info         # Environment + binary diagnostics (--quick, --json)
//   cloakbrowser doctor       # Alias for info
//   cloakbrowser update       # Check for and download newer binary
//   cloakbrowser clear-cache  # Remove cached binaries

const string UpgradeHint =
    "→ Add a license key for the latest Pro binary: https://cloakbrowser.dev";

// Route CloakBrowser logs to stderr at Info level (clean output).
CloakLog.MinLevel = CloakLogLevel.Info;

string? command = args.Length > 0 ? args[0] : null;
string[] rest = args.Length > 1 ? args[1..] : Array.Empty<string>();

if (string.IsNullOrEmpty(command) || command is "-h" or "--help" or "help")
{
    PrintHelp();
    return string.IsNullOrEmpty(command) ? 2 : 0;
}

try
{
    switch (command)
    {
        case "install":
            await CmdInstall();
            break;
        case "info":
        case "doctor":
            CmdInfo(rest);
            break;
        case "update":
            await CmdUpdate();
            break;
        case "clear-cache":
            CmdClearCache();
            break;
        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintHelp();
            return 2;
    }
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 1;
}

return 0;

static async Task CmdInstall()
{
    string path = await Download.EnsureBinaryAsync().ConfigureAwait(false);
    Console.WriteLine(path);
}

static void CmdInfo(string[] flags)
{
    bool quick = flags.Contains("--quick") || flags.Contains("--no-launch");
    bool asJson = flags.Contains("--json");

    var diag = Diagnostics.Collect(quick);

    if (asJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        PrintDiagnostics(diag);
    }
}

static void PrintDiagnostics(Dictionary<string, object?> diag)
{
    var env = (Dictionary<string, object?>)diag["environment"]!;
    Console.WriteLine("CloakBrowser diagnostics");
    Console.WriteLine($".NET:      {env["dotnet"]}");
    Console.WriteLine($"OS:        {env["os"]} {env["arch"]}");
    Console.WriteLine($"Platform:  {env.GetValueOrDefault("platform_tag") ?? "unknown"}");

    var binary = (Dictionary<string, object?>)diag["binary"]!;
    if (binary.ContainsKey("error"))
    {
        Console.WriteLine($"Binary:    unavailable ({binary["error"]})");
    }
    else
    {
        if ((string)binary["tier"]! == "override")
            Console.WriteLine("Version:   set via CLOAKBROWSER_BINARY_PATH (see Launch line)");
        else
            Console.WriteLine($"Version:   {binary["version"]} ({binary["tier"]})");
        Console.WriteLine($"Binary:    {binary["path"]}");
        Console.WriteLine($"Installed: {binary["installed"]}");
        if (binary["cache_dir"] is string cd && !string.IsNullOrEmpty(cd))
            Console.WriteLine($"Cache:     {cd}");
        if (binary["override"] is string ov && !string.IsNullOrEmpty(ov))
            Console.WriteLine($"Override:  {ov} (CLOAKBROWSER_BINARY_PATH)");
    }

    var launch = (Dictionary<string, object?>)diag["launch"]!;
    if (launch["tested"] is not true)
    {
        Console.WriteLine($"Launch:    {launch["reason"]}");
    }
    else if (launch["ok"] is true)
    {
        Console.WriteLine($"Launch:    ✓ {launch["version"]}");
    }
    else
    {
        Console.WriteLine($"Launch:    ✗ failed — {launch["error"]}");
        var libs = launch.GetValueOrDefault("missing_libs") as List<string> ?? new();
        foreach (var lib in libs)
            Console.WriteLine($"           missing: {lib}");
        if (libs.Count > 0)
            Console.WriteLine("           → install the missing system libraries (e.g. apt-get install)");
    }

    if (diag.TryGetValue("fonts", out var fontsObj) && fontsObj is Dictionary<string, object?> fonts)
    {
        string winFonts = (string)fonts["windows_fonts"]!;
        Console.WriteLine($"Win fonts: {winFonts}");
        if (winFonts == "missing")
            Console.WriteLine("           → spoofing Windows on Linux without Windows fonts; install msttcorefonts");
    }

    var lic = (Dictionary<string, object?>)diag["license"]!;
    string tier = (string)lic["tier"]!;
    if (tier == "free")
    {
        Console.WriteLine("License:   Free");
        Console.WriteLine($"           {UpgradeHint}");
    }
    else if (lic.ContainsKey("error"))
    {
        Console.WriteLine($"License:   {tier} ({lic["error"]})");
    }
    else
    {
        Console.WriteLine($"License:   {tier}");
    }

    var geoip = (Dictionary<string, object?>)diag["geoip"]!;
    Console.WriteLine($"GeoIP DB:  {(geoip["db_present"] is true ? "present" : "not downloaded (optional)")}");

    if (diag.TryGetValue("modules", out var modulesObj) && modulesObj is Dictionary<string, object?> modules)
    {
        Console.WriteLine("Modules:");
        foreach (var kv in modules)
            Console.WriteLine($"  {kv.Key}: {(kv.Value is true ? "ok" : "missing")}");
    }
}

static async Task CmdUpdate()
{
    CloakLog.Info("Checking for updates...");
    string? newVersion = await Download.CheckForUpdateAsync().ConfigureAwait(false);
    Console.WriteLine(newVersion != null
        ? $"Updated to Chromium {newVersion}"
        : "Already up to date.");
}

static void CmdClearCache()
{
    if (!Directory.Exists(Config.GetCacheDir()))
    {
        Console.WriteLine("No cache to clear.");
        return;
    }
    Download.ClearCache();
    Console.WriteLine("Cache cleared.");
}

static void PrintHelp()
{
    Console.WriteLine("usage: cloakbrowser <command>");
    Console.WriteLine();
    Console.WriteLine("Manage the CloakBrowser stealth Chromium binary.");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  install      Download the Chromium binary");
    Console.WriteLine("  info         Environment + binary diagnostics (--quick, --json)");
    Console.WriteLine("  doctor       Alias for info");
    Console.WriteLine("  update       Check for and download a newer binary");
    Console.WriteLine("  clear-cache  Remove all cached binaries");
}
