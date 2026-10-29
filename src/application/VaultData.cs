using System;
using System.Collections.Generic;

namespace CodexAccountDesk;

internal sealed class VaultData
{
    public int Version { get; set; } = 1;
    public List<AccountProfile> Accounts { get; set; } = new();
    public string? PreviousDesktopAuthJson { get; set; }
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "en";
    public int TrendHours { get; set; } = 24;
    public List<DrivePreference> Drives { get; set; } = DefaultDrives();
    public AccountSwitchPlan? PendingSwitch { get; set; }
    public bool AutomaticSwitchEnabled { get; set; }
    public bool AllowCreditUse { get; set; } = true;
    public int AutomaticSwitchThreshold { get; set; } = 10;
    public bool AutomaticSwitchUsePriority { get; set; }
    public List<string> AutomaticSwitchPriority { get; set; } = new();
    public DateTimeOffset? LastAutomaticSwitchAttempt { get; set; }
    private static List<DrivePreference> DefaultDrives()
    {
        if (OperatingSystem.IsMacOS()) return new() { new() { Root = "/", Label = "Macintosh HD" } };
        if (OperatingSystem.IsLinux()) return new() { new() { Root = "/", Label = "/" } };
        if (OperatingSystem.IsWindows()) return new() { new() { Root = "C:\\", Label = "C:" }, new() { Root = "D:\\", Label = "D:" } };
        throw new PlatformNotSupportedException("Account vault defaults require a supported platform provider.");
    }
}

