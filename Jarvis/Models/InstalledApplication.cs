using System;
using System.Collections.Generic;
using System.Text;

namespace Jarvis.Models;

public class InstalledApplication
{
    public string? DisplayName { get; set; }
    public string? DisplayVersion { get; set; }
    public string? Publisher { get; set; }
    public string? InstallLocation { get; set; }
    public string? DisplayIcon { get; set; }
    public string? UninstallString { get; set; }
    public string? ExecutablePath { get; set; }
}

