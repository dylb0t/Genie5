using Genie4.Core.Config;
using Genie4.Core.Runtime;

var baseDir = Path.Combine(Path.GetTempPath(), "Genie5Test");
Directory.CreateDirectory(baseDir);

var local = new LocalDirectoryService("Genie5", baseDir);
var config = new GenieConfig(local);

config.SetSetting("scriptdir", "Scripts");
config.SetSetting("reconnect", "true");
config.SetSetting("muted", "true");
config.Save();

Console.WriteLine($"BasePath: {local.Current.BasePath}");
Console.WriteLine($"ScriptDir: {config.ScriptDir}");
Console.WriteLine($"Reconnect: {config.Reconnect}");
Console.WriteLine($"PlaySounds: {config.PlaySounds}");
