using Genie4.Core.Runtime;
using Genie4.Core.Utility;

namespace Genie4.Core.Config;

public sealed class GenieConfig
{
    private readonly LocalDirectoryService _localDirectory;

    public GenieConfig(LocalDirectoryService localDirectory)
    {
        _localDirectory = localDirectory;
    }

    public event Action<ConfigFieldUpdated>? ConfigChanged;

    public char ScriptChar { get; set; } = '.';
    public char SeparatorChar { get; set; } = ';';
    public char CommandChar { get; set; } = '#';
    public char MyCommandChar { get; set; } = '/';
    public bool TriggerOnInput { get; set; } = true;
    public int BufferLineSize { get; set; } = 5;
    public bool ShowSpellTimer { get; set; } = true;
    public bool AutoLog { get; set; } = true;
    public bool ClassicConnect { get; set; } = true;
    public string Editor { get; set; } = "notepad.exe";
    public string Prompt { get; set; } = "> ";
    public bool PromptBreak { get; set; } = true;
    public bool PromptForce { get; set; } = true;
    public bool Condensed { get; set; }
    public string IgnoreMonsterList { get; set; } = "appears dead|(dead)";
    public int ScriptTimeout { get; set; } = 5000;
    public int MaxGoSubDepth { get; set; } = 50;
    public bool Reconnect { get; set; } = true;
    public bool IgnoreCloseAlert { get; set; }
    public bool PlaySounds { get; set; } = true;
    public bool KeepInput { get; set; }
    public bool AbortDupeScript { get; set; } = true;
    public bool ParseGameOnly { get; set; }
    public bool AutoMapper { get; set; } = true;
    public int AutoMapperAlpha { get; set; } = 255;
    public int ServerActivityTimeout { get; set; } = 180;
    public string ServerActivityCommand { get; set; } = "fatigue";
    public int UserActivityTimeout { get; set; } = 300;
    public string UserActivityCommand { get; set; } = "quit";
    public double RoundTimeOffset { get; set; }
    public bool ShowLinks { get; set; }
    public bool ShowImages { get; set; } = true;
    public bool WebLinkSafety { get; set; } = true;
    public bool SizeInputToGame { get; set; }
    public bool UpdateMapperScripts { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoUpdate { get; set; }
    public bool AutoUpdateLamp { get; set; } = true;
    public string ScriptExtension { get; set; } = "cmd";
    public string ConnectScript { get; set; } = string.Empty;

    public string ScriptDirRaw { get; set; } = "Scripts";
    public string SoundDirRaw { get; set; } = "Sounds";
    public string PluginDirRaw { get; set; } = "Plugins";
    public string MapDirRaw { get; set; } = "Maps";
    public string ConfigDirRaw { get; set; } = "Config";
    public string ProfileConfigDirRaw { get; set; } = "Config";
    public string LogDirRaw { get; set; } = "Logs";
    public string ArtDirRaw { get; set; } = "Art";
    public string ScriptRepo { get; set; } = string.Empty;
    public string ArtRepo { get; set; } = string.Empty;
    public string MapRepo { get; set; } = string.Empty;
    public string PluginRepo { get; set; } = string.Empty;

    public string ScriptDir => _localDirectory.Current.ResolvePath(ScriptDirRaw);
    public string SoundDir => _localDirectory.Current.ResolvePath(SoundDirRaw);
    public string PluginDir => _localDirectory.Current.ResolvePath(PluginDirRaw);
    public string MapDir => _localDirectory.Current.ResolvePath(MapDirRaw);
    public string ConfigDir => _localDirectory.Current.ResolvePath(ConfigDirRaw);
    public string ConfigProfileDir => _localDirectory.Current.ResolvePath(ProfileConfigDirRaw);
    public string LogDir => _localDirectory.Current.ResolvePath(LogDirRaw);
    public string ArtDir => _localDirectory.Current.ResolvePath(ArtDirRaw);

    public bool Save(string fileName = "settings.cfg")
    {
        try
        {
            var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(ConfigDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var lines = new[]
            {
                $"#config {{alwaysontop}} {{{AlwaysOnTop}}}",
                $"#config {{classicconnect}} {{{ClassicConnect}}}",
                $"#config {{scriptchar}} {{{ScriptChar}}}",
                $"#config {{separatorchar}} {{{SeparatorChar}}}",
                $"#config {{commandchar}} {{{CommandChar}}}",
                $"#config {{mycommandchar}} {{{MyCommandChar}}}",
                $"#config {{triggeroninput}} {{{TriggerOnInput}}}",
                $"#config {{maxrowbuffer}} {{{BufferLineSize}}}",
                $"#config {{spelltimer}} {{{ShowSpellTimer}}}",
                $"#config {{autolog}} {{{AutoLog}}}",
                $"#config {{automapper}} {{{AutoMapper}}}",
                $"#config {{automapperalpha}} {{{AutoMapperAlpha}}}",
                $"#config {{editor}} {{{Editor}}}",
                $"#config {{prompt}} {{{Prompt}}}",
                $"#config {{promptbreak}} {{{PromptBreak}}}",
                $"#config {{promptforce}} {{{PromptForce}}}",
                $"#config {{condensed}} {{{Condensed}}}",
                $"#config {{monstercountignorelist}} {{{IgnoreMonsterList}}}",
                $"#config {{scripttimeout}} {{{ScriptTimeout}}}",
                $"#config {{maxgosubdepth}} {{{MaxGoSubDepth}}}",
                $"#config {{roundtimeoffset}} {{{RoundTimeOffset}}}",
                $"#config {{artdir}} {{{ArtDirRaw}}}",
                $"#config {{artrepo}} {{{ArtRepo}}}",
                $"#config {{scriptdir}} {{{ScriptDirRaw}}}",
                $"#config {{scriptrepo}} {{{ScriptRepo}}}",
                $"#config {{sounddir}} {{{SoundDirRaw}}}",
                $"#config {{mapdir}} {{{MapDirRaw}}}",
                $"#config {{maprepo}} {{{MapRepo}}}",
                $"#config {{plugindir}} {{{PluginDirRaw}}}",
                $"#config {{pluginrepo}} {{{PluginRepo}}}",
                $"#config {{configdir}} {{{ConfigDirRaw}}}",
                $"#config {{logdir}} {{{LogDirRaw}}}",
                $"#config {{updatemapperscripts}} {{{UpdateMapperScripts}}}",
                $"#config {{reconnect}} {{{Reconnect}}}",
                $"#config {{ignoreclosealert}} {{{IgnoreCloseAlert}}}",
                $"#config {{keepinputtext}} {{{KeepInput}}}",
                $"#config {{sizeinputtogame}} {{{SizeInputToGame}}}",
                $"#config {{muted}} {{{!PlaySounds}}}",
                $"#config {{abortdupescript}} {{{AbortDupeScript}}}",
                $"#config {{parsegameonly}} {{{ParseGameOnly}}}",
                $"#config {{servertimeout}} {{{ServerActivityTimeout}}}",
                $"#config {{servertimeoutcommand}} {{{ServerActivityCommand}}}",
                $"#config {{usertimeout}} {{{UserActivityTimeout}}}",
                $"#config {{usertimeoutcommand}} {{{UserActivityCommand}}}",
                $"#config {{showlinks}} {{{ShowLinks}}}",
                $"#config {{showimages}} {{{ShowImages}}}",
                $"#config {{weblinksafety}} {{{WebLinkSafety}}}",
                $"#config {{connectscript}} {{{ConnectScript}}}",
                $"#config {{autoupdate}} {{{AutoUpdate}}}",
                $"#config {{autoupdatelamp}} {{{AutoUpdateLamp}}}",
                $"#config {{checkforupdates}} {{{CheckForUpdates}}}",
                $"#config {{scriptextension}} {{{ScriptExtension}}}",
            };

            File.WriteAllLines(path, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Load(string fileName = "settings.cfg")
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(ConfigDir, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        foreach (var line in File.ReadLines(path))
        {
            var parts = Parsing.ArgumentParser.ParseArgs(line);
            if (parts.Count == 3)
            {
                SetSetting(parts[1], parts[2], showException: false);
            }
        }

        return true;
    }

    public IReadOnlyList<string> SetSetting(string key, string value = "", bool showException = true)
    {
        var messages = new List<string>();
        try
        {
            switch (key.ToLowerInvariant())
            {
                case "scriptchar": ScriptChar = FirstCharOrDefault(value, ScriptChar); break;
                case "separatorchar": SeparatorChar = FirstCharOrDefault(value, SeparatorChar); break;
                case "commandchar": CommandChar = FirstCharOrDefault(value, CommandChar); break;
                case "mycommandchar": MyCommandChar = FirstCharOrDefault(value, MyCommandChar); break;
                case "triggeroninput": TriggerOnInput = ToBool(value); break;
                case "maxrowbuffer": BufferLineSize = UtilityCore.StringToInteger(value); break;
                case "spelltimer": ShowSpellTimer = ToBool(value); break;
                case "autolog": AutoLog = ToBool(value); Notify(ConfigFieldUpdated.Autolog); break;
                case "classicconnect": ClassicConnect = ToBool(value); Notify(ConfigFieldUpdated.ClassicConnect); break;
                case "editor": Editor = value; break;
                case "prompt": Prompt = NormalizePrompt(value); break;
                case "promptbreak": PromptBreak = ToBool(value); break;
                case "promptforce": PromptForce = ToBool(value); break;
                case "condensed": Condensed = ToBool(value); break;
                case "monstercountignorelist": IgnoreMonsterList = value; break;
                case "scripttimeout": ScriptTimeout = (int)UtilityCore.StringToDouble(value); break;
                case "maxgosubdepth": MaxGoSubDepth = int.TryParse(value, out var mgd) ? mgd : MaxGoSubDepth; break;
                case "roundtimeoffset": RoundTimeOffset = UtilityCore.StringToDouble(value); break;
                case "artdir": ArtDirRaw = SetDir(value); break;
                case "artrepo": ArtRepo = value; break;
                case "scriptdir": ScriptDirRaw = SetDir(value); break;
                case "scriptrepo": ScriptRepo = value; break;
                case "sounddir": SoundDirRaw = SetDir(value); break;
                case "mapdir": MapDirRaw = SetDir(value); break;
                case "maprepo": MapRepo = value; break;
                case "plugindir": PluginDirRaw = SetDir(value); break;
                case "pluginrepo": PluginRepo = value; break;
                case "configdir": ConfigDirRaw = SetDir(value); break;
                case "logdir": LogDirRaw = SetDir(value); Notify(ConfigFieldUpdated.LogDir); break;
                case "reconnect": Reconnect = ToBool(value); Notify(ConfigFieldUpdated.Reconnect); break;
                case "ignoreclosealert": IgnoreCloseAlert = ToBool(value); break;
                case "muted": PlaySounds = !ToBool(value); Notify(ConfigFieldUpdated.Muted); break;
                case "keepinputtext": KeepInput = ToBool(value); Notify(ConfigFieldUpdated.KeepInput); break;
                case "abortdupescript": AbortDupeScript = ToBool(value); break;
                case "parsegameonly": ParseGameOnly = ToBool(value); break;
                case "automapper": AutoMapper = ToBool(value); Notify(ConfigFieldUpdated.AutoMapper); break;
                case "automapperalpha": AutoMapperAlpha = ClampAlpha(value); Notify(ConfigFieldUpdated.AutoMapper); break;
                case "showlinks": ShowLinks = ToBool(value); break;
                case "showimages": ShowImages = ToBool(value); Notify(ConfigFieldUpdated.ImagesEnabled); break;
                case "sizeinputtogame": SizeInputToGame = ToBool(value); Notify(ConfigFieldUpdated.SizeInputToGame); break;
                case "updatemapperscripts": UpdateMapperScripts = ToBool(value); Notify(ConfigFieldUpdated.UpdateMapperScripts); break;
                case "alwaysontop": AlwaysOnTop = ToBool(value); Notify(ConfigFieldUpdated.AlwaysOnTop); break;
                case "weblinksafety": WebLinkSafety = ToBool(value); break;
                case "servertimeout": ServerActivityTimeout = (int)UtilityCore.StringToDouble(value); break;
                case "usertimeout": UserActivityTimeout = (int)UtilityCore.StringToDouble(value); break;
                case "servertimeoutcommand": ServerActivityCommand = value; break;
                case "usertimeoutcommand": UserActivityCommand = value; break;
                case "connectscript": ConnectScript = value; break;
                case "autoupdate": AutoUpdate = ToBool(value); Notify(ConfigFieldUpdated.AutoUpdate); break;
                case "autoupdatelamp": AutoUpdateLamp = ToBool(value); Notify(ConfigFieldUpdated.AutoUpdateLamp); break;
                case "checkforupdates": CheckForUpdates = ToBool(value); Notify(ConfigFieldUpdated.CheckForUpdates); break;
                case "scriptextension": ScriptExtension = string.IsNullOrWhiteSpace(value) ? "cmd" : value; break;
                default:
                    if (showException)
                    {
                        throw new InvalidOperationException($"Config {key} was not recognized.");
                    }
                    break;
            }

            messages.Add($"Set {key.ToLowerInvariant()}: {value}");
            messages.Add("Don't forget to persist settings after edits.");
            return messages;
        }
        catch
        {
            if (showException)
            {
                throw;
            }

            return messages;
        }
    }

    private string SetDir(string value)
    {
        _localDirectory.ValidateDirectory(value);
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void Notify(ConfigFieldUpdated field) => ConfigChanged?.Invoke(field);

    private static bool ToBool(string value) => value.ToLowerInvariant() switch
    {
        "on" or "true" or "1" => true,
        _ => false
    };

    private static char FirstCharOrDefault(string value, char defaultValue) =>
        string.IsNullOrEmpty(value) ? defaultValue : value[0];

    private static string NormalizePrompt(string value) => value.ToLowerInvariant() switch
    {
        "on" or "true" or "1" => "> ",
        "off" or "false" or "0" => string.Empty,
        _ => value
    };

    private static int ClampAlpha(string value)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return 255;
        }

        return Math.Clamp(parsed, 0, 255);
    }
}
