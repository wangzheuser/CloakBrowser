using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CloakBrowser;
using Xunit;

namespace CloakBrowser.Tests;

/// <summary>
/// Diagnostics.Collect drives the `info` / `doctor` CLI. In quick mode it never
/// spawns the binary, so an isolated temp cache dir yields a deterministic
/// "free / not installed" result with no network. Env-serial because it pins
/// CLOAKBROWSER_CACHE_DIR / CLOAKBROWSER_LICENSE_KEY for the duration.
/// </summary>
[Collection("env-serial")]
public class DiagnosticsTests
{
    [Fact]
    public void Quick_skips_launch_and_reports_free_license()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmp);
        string? prevCache = Environment.GetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR");
        string? prevKey = Environment.GetEnvironmentVariable("CLOAKBROWSER_LICENSE_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR", tmp);
            Environment.SetEnvironmentVariable("CLOAKBROWSER_LICENSE_KEY", null);

            var diag = Diagnostics.Collect(quick: true);

            var env = Assert.IsType<Dictionary<string, object?>>(diag["environment"]);
            Assert.NotNull(env["dotnet"]);

            var launch = Assert.IsType<Dictionary<string, object?>>(diag["launch"]);
            Assert.Equal(false, launch["tested"]);
            Assert.Contains("--quick", (string)launch["reason"]!);

            var license = Assert.IsType<Dictionary<string, object?>>(diag["license"]);
            Assert.Equal("free", license["tier"]);

            Assert.True(diag.ContainsKey("binary"));
            Assert.True(diag.ContainsKey("geoip"));
            // modules section mirrors Python/JS for cross-language parity
            var modules = Assert.IsType<Dictionary<string, object?>>(diag["modules"]);
            Assert.True((bool)modules["playwright"]!);
            // fonts section is Linux-only
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), diag.ContainsKey("fonts"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR", prevCache);
            Environment.SetEnvironmentVariable("CLOAKBROWSER_LICENSE_KEY", prevKey);
            try { Directory.Delete(tmp, recursive: true); } catch { /* best-effort */ }
        }
    }
}
