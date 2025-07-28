using System;
using System.Collections.Immutable;
using static System.Environment;

// `Config` keeps a synced immutable dictionary and configuration file in INI 
// format. The file is always read and written at once. The in-memory cache can 
// be refreshed from the file and also persisted on demand. The file is created 
// upon first read, if it doesn't exist.

// Note `Config` is not allowed to add or remove keys from a file in normal 
// operation (i.e. when a config file exists).

public class ConfigException : Exception {
    public ConfigException(string message) : base(message) {}
}

public class Config {
    public string Path { get; }
    public TextWriter? Dbg { get; init; }
    
    public Config(string path) {
        Path = path;
    }
    
    // Data updates modify `config` with an updated dictionary
    
    ImmutableDictionary<string, ImmutableDictionary<string, string>> config;
    
    public int SectionCount {
        get {
            if (config == null) throw new ConfigException(
                "Configuration was not read");
            return config.Keys.Count();
        }
    }
    
    public int KeyCount {
        get {
            if (config == null) throw new ConfigException(
                "Configuration was not read");
            return config.Sum(c => c.Value.Count);
        }
    }
    
    public void Read() => config = ReadInternal();
    
    // Read configuration from disk. If no file found, create one (assumes 
    // directory structure already created by the main program). A file create 
    // error should be handled by the caller.
    
    ImmutableDictionary<string, ImmutableDictionary<string, string>> 
    ReadInternal() {
        Dbg?.WriteLine("ReadInternal()");
        if (!File.Exists(Path)) {
            Dbg?.WriteLine("    No file, creating");
            WriteFull(fileCreation: true);
        }
        using var sr = new StreamReader(Path);
        string ln;
        int lnEqIdx = -1;
        string? sectionName = null;
        Dictionary<string, string> section = new();
        var cfg = new Dictionary<string, ImmutableDictionary<string, string>>();
        void PersistOpenSection() {
            if (cfg.ContainsKey(sectionName)) throw new ConfigException(
                $"Configuration contains duplicate '{sectionName}' section");
            cfg.Add(sectionName, section.ToImmutableDictionary());
        }
        while ((ln = sr.ReadLine()) != null) {
            ln = ln.Trim();
            if (ln.StartsWith('[') && ln.EndsWith(']')) {
                // Persist open (previous) section
                if (sectionName != null) PersistOpenSection();
                // Initiate new section
                sectionName = ln[1..^1].Trim();
                Dbg?.WriteLine($"   [{sectionName}]");
                section.Clear();
            }
            else if ((lnEqIdx = ln.IndexOf('=')) != -1) {
                string key = ln[..lnEqIdx];
                string vlue = ln[(lnEqIdx+1)..];
                Dbg?.WriteLine($"        '{key}' = '{vlue}'");
                if (section.ContainsKey(key)) throw new ConfigException(
                    $"Configuration section '{sectionName
                        }' contains duplicate key '{key}'");
                section.Add(key, vlue);
            }
            else {
                Dbg?.WriteLine($"        **IGNORED: '{ln}'");
            }
        }
        if (sectionName != null) PersistOpenSection();
        return cfg.ToImmutableDictionary();
    }
    
    // Read a value from the in-memory configuration
    
    public string? GetValue(string section, string key) {
        Dbg?.WriteLine("GetValue()");
        if (config == null) throw new ConfigException(
            "Configuration was not read");
        if (!config.ContainsKey(section)) {
            Dbg?.WriteLine($"Configuration section not found: '{section}'");
            return null;
        }
        if (!config[section].TryGetValue(key, out string v)) {
            Dbg?.WriteLine($"Configuration key not found: '{key}' in '{
                section}'");
            return null;
        }
        return v;
    }
    
    public T? GetValue<T>(string section, string key) 
        where T : IParsable<T> 
    {
        string? v = GetValue(section, key);
        if (v == null) return default;
        return T.Parse(v, null);
    }
    
    // To be set, the section and key must exist. The in-memory config is 1:1 
    // with the configuration file. However, `fileCreation` indicates whether 
    // the configuration file is being created because it doesn't exist 
    // (triggers some alternate logic). In this case exceptionally, sections and 
    // keys are created.
    
    public void SetValue(
        string section, string key, string vlue, bool dontWrite = false, 
        bool fileCreation = false
    ) {
        Dbg?.WriteLine($"SetValue(), '{section}/{key}' to '{vlue
            }', don't write: {dontWrite}, file creation: {fileCreation}");
        if (config == null) {
            if (!fileCreation) throw new ConfigException(
                "Configuration was not read");
            else 
                // Create the dictionary
                config = ImmutableDictionary<
                    string, ImmutableDictionary<string, string>
                >.Empty;
        }
        if (!config.ContainsKey(section)) {
            if (!fileCreation) throw new ConfigException(
                $"Section '{section}' doesn't exist");
            else config = config.Add(section, 
                ImmutableDictionary<string, string>.Empty);
        }
        if (config[section] == null) throw new InvalidOperationException(
            "Missing data structure");
        if (!config[section].ContainsKey(key)) {
            if (!fileCreation) throw new ConfigException(
                "Key '{key}' doesn't exist");
            else config = config.SetItem(
                section, config[section].Add(key, default));
        }
        if (config[section][key] == vlue) {
            Dbg?.WriteLine($"    No change performed.");
            return;
        }
        config = config.SetItem(section, config[section].SetItem(key, vlue));
        
        // Write to file
        if (!dontWrite) WriteFull();
    }
    
    // Write the full config to the file. `fileCreation` indicates whether the 
    // file should be created because it doesn't exist (triggers some alternate 
    // logic that specially initializes the in-memory data structures).
    
    public void WriteFull(bool fileCreation = false) {
        Dbg?.WriteLine("WriteFull(), fileCreation: {fileCreation}");
        
        if ((config == null) != fileCreation) throw new ConfigException(
            "Configuration was not read or incorrect file creation");
        
        if (fileCreation) {
            // Set default values
            string defaultInitialDir = GetFolderPath(SpecialFolder.MyMusic);
            string defaultVlcDir = System.IO.Path.Combine(
                GetFolderPath(SpecialFolder.ProgramFiles), "VideoLAN", "VLC");
            SetValue("general", "InitialDir", defaultInitialDir, 
                dontWrite: true, fileCreation: true);
            SetValue("general", "VlcDir", defaultVlcDir, dontWrite: true, 
                fileCreation: true);
        }
        
        if (fileCreation == File.Exists(Path)) throw new ConfigException(
            "Configuration file missing or incorrect file creation");
        
        if (!fileCreation) {
            string configBakPth = Path + ".bak";
            
            if (File.Exists(configBakPth)) {
                Dbg?.WriteLine($"    Remove `{configBakPth}`");
                File.Delete(configBakPth);
            }
            Dbg?.WriteLine($"""
                    Move `{Path}` 
                        to `{configBakPth}`
                """);
            File.Move(Path, configBakPth);
        }
        
        // Write the file. If creating the file, assumes the directory structure 
        // is created.
        
        using var sw = new StreamWriter(Path, append: false);
        
        int lnc = 0;
        foreach (string ln in config.SelectMany(c =>
            c.Value.Select(v => $"{v.Key}={v.Value}")
                .Prepend($"[{c.Key}]")
            )) 
        {
            sw.WriteLine(ln);
            lnc++;
        }
        Dbg?.WriteLine($"    {lnc} lines written to `{Path}`");
    }
}