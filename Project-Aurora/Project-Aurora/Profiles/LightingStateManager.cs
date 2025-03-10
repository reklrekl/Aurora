using Aurora.Profiles.Desktop;
using Aurora.Profiles.Generic_Application;
using Aurora.Settings;
using Aurora.Settings.Layers;
using Aurora.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Aurora.Devices;
using Aurora.Modules.GameStateListen;
using Aurora.Modules.ProcessMonitor;

namespace Aurora.Profiles;

public sealed class LightingStateManager
{
    public Dictionary<string, ILightEvent> Events { get; } = new() { { "desktop", new Desktop.Desktop() } };

    private Desktop.Desktop DesktopProfile => (Desktop.Desktop)Events["desktop"];

    private readonly List<ILightEvent> _startedEvents = new();
    private readonly List<ILightEvent> _updatedEvents = new();

    private Dictionary<string, string> EventProcesses { get; } = new();

    private Dictionary<string, string> EventTitles { get; } = new();

    private Dictionary<string, string> EventAppIDs { get; } = new();

    public Dictionary<Type, LayerHandlerMeta> LayerHandlers { get; } = new();

    public event EventHandler? PreUpdate;
    public event EventHandler? PostUpdate;

    private readonly ActiveProcessMonitor _processMonitor;

    private readonly Func<ILightEvent, bool> _isOverlayActiveProfile;

    private readonly Task<PluginManager> _pluginManager;
    private readonly Task<IpcListener?> _ipcListener;
    private readonly Task<DeviceManager> _deviceManager;

    private bool Initialized { get; set; }

    public LightingStateManager(Task<PluginManager> pluginManager, Task<IpcListener?> ipcListener, Task<DeviceManager> deviceManager)
    {
        _pluginManager = pluginManager;
        _ipcListener = ipcListener;
        _deviceManager = deviceManager;

        _processMonitor = ActiveProcessMonitor.Instance;
        _isOverlayActiveProfile = evt =>
            evt.IsOverlayEnabled &&
            evt.Config.ProcessNames.Any(name => RunningProcessMonitor.Instance.IsProcessRunning(name));
    }

    public async Task Initialize()
    {
        if (Initialized)
            return;
        // Register all Application types in the assembly
        var profileTypes = from type in Assembly.GetExecutingAssembly().GetLoadableTypes()
            where type.BaseType == typeof(Application) && type != typeof(GenericApplication)
            let inst = (Application)Activator.CreateInstance(type)
            orderby inst.Config.Name
            select inst;
        foreach (var inst in profileTypes)
            RegisterEvent(inst);

        // Register all layer types that are in the Aurora.Settings.Layers namespace.
        // Do not register all that are inside the assembly since some are application-specific (e.g. minecraft health layer)
        var layerTypes = from type in Assembly.GetExecutingAssembly().GetLoadableTypes()
            where type.GetInterfaces().Contains(typeof(ILayerHandler))
            let name = type.Name.CamelCaseToSpaceCase()
            let meta = type.GetCustomAttribute<LayerHandlerMetaAttribute>()
            where !type.IsGenericType
            where meta is not { Exclude: true }
            select (type, meta);
        foreach (var (type, meta) in layerTypes)
            LayerHandlers.Add(type, new LayerHandlerMeta(type, meta));

        LoadSettings();
        await LoadPlugins();

        var additionalProfilesPath = Path.Combine(Global.AppDataDirectory, "AdditionalProfiles");
        if (Directory.Exists(additionalProfilesPath))
        {
            var additionals = new List<string>(Directory.EnumerateDirectories(additionalProfilesPath));
            foreach (var processName in from dir in additionals
                     where File.Exists(Path.Combine(dir, "settings.json"))
                     select Path.GetFileName(dir)
                    )
            {
                RegisterEvent(new GenericApplication(processName));
            }
        }

        foreach (var profile in Events)
        {
            profile.Value.Initialize();
        }

        // Listen for profile keybind triggers
        Global.InputEvents.KeyDown += CheckProfileKeybinds;

        Initialized = true;
    }

    private async Task LoadPlugins()
    {
        (await _pluginManager).ProcessManager(this);
    }

    private void LoadSettings()
    {
        foreach (var kvp in Events
                     .Where(kvp => !Global.Configuration.ProfileOrder.Contains(kvp.Key) && kvp.Value is Application))
        {
            Global.Configuration.ProfileOrder.Add(kvp.Key);
        }

        foreach (var key in Global.Configuration.ProfileOrder.ToList()
                     .Where(key => !Events.ContainsKey(key) || Events[key] is not Application))
        {
            Global.Configuration.ProfileOrder.Remove(key);
        }

        PutProfileTop("logitech");
        PutProfileTop("chroma");
        PutProfileTop("desktop");
    }

    private static void PutProfileTop(string profileId)
    {
        Global.Configuration.ProfileOrder.Remove(profileId);
        Global.Configuration.ProfileOrder.Insert(0, profileId);
    }

    private void SaveAll()
    {
        foreach (var profile in Events.Where(profile => profile.Value is Application))
        {
            ((Application)profile.Value).SaveAll();
        }
    }

    public void RegisterEvent(ILightEvent @event)
    {
        var profileId = @event.Config.ID;
        if (string.IsNullOrWhiteSpace(profileId) || Events.ContainsKey(profileId)) return;

        Events.Add(profileId, @event);

        foreach (var exe in @event.Config.ProcessNames)
        {
            EventProcesses[exe.ToLower()] = profileId;
        }

        @event.Config.ProcessNamesChanged += (_, _) =>
        {
            var keysToRemove = new List<string>();
            foreach (var (s, value) in EventProcesses)
            {
                if (value == profileId)
                {
                    keysToRemove.Add(s);
                }
            }

            foreach (var s in keysToRemove)
            {
                EventProcesses.Remove(s);
            }

            foreach (var exe in @event.Config.ProcessNames)
            {
                if (!exe.Equals(profileId))
                    EventProcesses.TryAdd(exe.ToLower(), profileId);
            }
        };

        if (@event.Config.ProcessTitles != null)
            foreach (var titleRx in @event.Config.ProcessTitles)
                EventTitles.Add(titleRx, profileId);

        if (!string.IsNullOrWhiteSpace(@event.Config.AppID))
            EventAppIDs.Add(@event.Config.AppID, profileId);

        if (@event is Application && !Global.Configuration.ProfileOrder.Contains(profileId))
        {
            Global.Configuration.ProfileOrder.Add(profileId);
        }

        if (Initialized)
            @event.Initialize();
    }

    public void RemoveGenericProfile(string key)
    {
        if (!Events.ContainsKey(key)) return;
        if (Events[key] is not GenericApplication)
            return;
        var profile = (GenericApplication)Events[key];
        Events.Remove(key);
        Global.Configuration.ProfileOrder.Remove(key);

        profile.Dispose();

        var path = profile.GetProfileFolderPath();
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    // Used to match a process's name and optional window title to a profile
    private ILightEvent? GetProfileFromProcessData(string processName, string processTitle)
    {
        var processNameProfile = GetProfileFromProcessName(processName);

        if (processNameProfile == null)
            return null;

        // Is title matching required?
        if (processNameProfile.Config.ProcessTitles != null)
        {
            var processTitleProfile = GetProfileFromProcessTitle(processTitle);

            if (processTitleProfile != null && processTitleProfile.Equals(processNameProfile))
            {
                return processTitleProfile;
            }
        }
        else
        {
            return processNameProfile;
        }

        return null;
    }

    private ILightEvent? GetProfileFromProcessName(string process)
    {
        if (EventProcesses.TryGetValue(process, out var eventId) &&
            Events.TryGetValue(eventId, out var res))
        {
            return res;
        }
 
        return Events.TryGetValue(process, out res) ? res : null;
    }

    /// <summary>
    /// Manually registers a layer. Only needed externally.
    /// </summary>
    public bool RegisterLayer<T>() where T : ILayerHandler
    {
        var t = typeof(T);
        if (LayerHandlers.ContainsKey(t)) return false;
        var meta = t.GetCustomAttribute<LayerHandlerMetaAttribute>();
        LayerHandlers.Add(t, new LayerHandlerMeta(t, meta));
        return true;
    }

    private ILightEvent? GetProfileFromProcessTitle(string title)
    {
        foreach (var value in EventTitles
                     .Where(entry => Regex.IsMatch(title, entry.Key, RegexOptions.IgnoreCase))
                     .Select(kv => kv.Value)
                )
        {
            if (!Events.ContainsKey(value))
                Global.logger.Warning("GetProfileFromProcess: The process with title '{Title}' matches an item in EventTitles" +
                                      " but subsequently '{Value}' does not in Events!", title, value);
            else
                return Events[value]; // added in an else so we keep searching for more valid regexes.
        }

        return null;
    }

    private ILightEvent? GetProfileFromAppId(string appid)
    {
        if (!EventAppIDs.ContainsKey(appid)) return Events.TryGetValue(appid, out var @event) ? @event : null;
        if (!Events.ContainsKey(EventAppIDs[appid]))
            Global.logger.Warning(
                "GetProfileFromAppID: The appid '{AppId}' exists in EventAppIDs but subsequently '{EventAppID}' does not in Events!",
                appid, EventAppIDs[appid]);
        return Events[EventAppIDs[appid]];
    }

    private Timer? _updateTimer;

    private long _nextProcessNameUpdate;
    private long _currentTick;
    private string _previewModeProfileKey = "";

    private readonly EventIdle _idleE = new();

    public string? PreviewProfileKey {
        get => _previewModeProfileKey;
        set => _previewModeProfileKey = value ?? string.Empty;
    }

    private readonly Stopwatch _watch = new();
        
    private readonly Semaphore _updateLock = new(1, 1);
    private bool _locked;

    public async Task InitUpdate()
    {
        await _deviceManager;
        _watch.Start();
        _updateTimer = new Timer(_ =>
        {
            TimerUpdate();
        }, null, 0, Timeout.Infinite);
    }

    private void TimerUpdate()
    {
        if (_locked)
        {
            return;
        }
        _updateLock.WaitOne();
        _locked = true;

        if (Global.isDebug)
            Update();
        else
        {
            try
            {
                Update();
            }
            catch (Exception exc)
            {
                Global.logger.Error(exc, "ProfilesManager.Update() Exception:");
                //TODO make below non-blocking
                MessageBox.Show("Error while updating light effects: " + exc.Message);
            }
        }
        _currentTick += _watch.ElapsedMilliseconds;
        _updateTimer?.Change(
            Math.Max(Global.Configuration.UpdateDelay - _watch.ElapsedMilliseconds, Global.Configuration.UpdateDelay), Timeout.Infinite);
        _watch.Reset();
        _locked = false;
        _updateLock.Release();
    }

    private void UpdateProcess()
    {
        if (Global.Configuration.DetectionMode != ApplicationDetectionMode.ForegroundApp ||
            _currentTick < _nextProcessNameUpdate) return;
        _processMonitor.UpdateActiveProcessPolling();
        _nextProcessNameUpdate = _currentTick + 1000L;
    }

    private void UpdateIdleEffects(EffectsEngine.EffectFrame newFrame)
    {
        var lastInput = new User32.tagLASTINPUTINFO();
        lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);
        lastInput.dwTime = 0;

        if (!User32.GetLastInputInfo(ref lastInput)) return;
        var idleTime = Environment.TickCount - lastInput.dwTime;

        if (idleTime < Global.Configuration.IdleDelay * 60 * 1000) return;
        if (Global.Configuration.TimeBasedDimmingEnabled &&
            Time.IsCurrentTimeBetween(Global.Configuration.TimeBasedDimmingStartHour,
                Global.Configuration.TimeBasedDimmingStartMinute,
                Global.Configuration.TimeBasedDimmingEndHour,
                Global.Configuration.TimeBasedDimmingEndMinute)) return;
        UpdateEvent(_idleE, newFrame);
    }

    private void UpdateEvent(ILightEvent @event, EffectsEngine.EffectFrame frame)
    {
        StartEvent(@event);
        @event.UpdateLights(frame);
    }

    private void StartEvent(ILightEvent @event)
    {
        _updatedEvents.Add(@event);

        // Skip if event was already started
        if (_startedEvents.Contains(@event)) return;

        _startedEvents.Add(@event);
        @event.OnStart();
    }

    private void StopUnUpdatedEvents()
    {
        // Skip if there are no started events or started events are the same since last update
        if (!_startedEvents.Any() || _startedEvents.SequenceEqual(_updatedEvents)) return;

        var eventsToStop = _startedEvents.Except(_updatedEvents).ToList();
        foreach (var eventToStop in eventsToStop)
            eventToStop.OnStop();

        _startedEvents.Clear();
        _startedEvents.AddRange(_updatedEvents);
    }

    private bool _profilesDisabled;
    private void Update()
    {
        var debugTimer = new Stopwatch();
        PreUpdate?.Invoke(this, EventArgs.Empty);
        _updatedEvents.Clear();

        //Blackout. TODO: Cleanup this a bit. Maybe push blank effect frame to keyboard incase it has existing stuff displayed
        var dimmingStartTime = new TimeSpan(Global.Configuration.TimeBasedDimmingStartHour,
            Global.Configuration.TimeBasedDimmingStartMinute, 0);
        var dimmingEndTime = new TimeSpan(Global.Configuration.TimeBasedDimmingEndHour, 
            Global.Configuration.TimeBasedDimmingEndMinute, 0);
        if (Global.Configuration.TimeBasedDimmingEnabled &&
            Time.IsCurrentTimeBetween(dimmingStartTime, dimmingEndTime))
        {
            var blackFrame = new EffectsEngine.EffectFrame();
            Global.effengine.PushFrame(blackFrame);
            StopUnUpdatedEvents();
            return;
        }

        var rawProcessName = _processMonitor.ProcessName;

        UpdateProcess();
        var newFrame = new EffectsEngine.EffectFrame();
        debugTimer.Restart();

        var profile = GetCurrentProfile(out var preview);

        // If the current foreground process is excluded from Aurora, disable the lighting manager
        if ((profile is Desktop.Desktop && !profile.IsEnabled) || Global.Configuration.ExcludedPrograms.Contains(rawProcessName))
        {
            if (!_profilesDisabled)
            {
                StopUnUpdatedEvents();
                lock (Effects.CanvasChangedLock)
                {
                    Global.effengine.PushFrame(newFrame);
                }
                _deviceManager.Result.ShutdownDevices();
            }

            _profilesDisabled = true;
            return;
        }

        if (_profilesDisabled)
        {
            _deviceManager.Result.InitializeDevices();
            _profilesDisabled = false;
        }
        debugTimer.Restart();

        //Need to do another check in case Desktop is disabled or the selected preview is disabled
        if (profile.IsEnabled)
            UpdateEvent(profile, newFrame);

        // Overlay layers
        if (!preview || Global.Configuration.OverlaysInPreview)
        {
            if (DesktopProfile.IsOverlayEnabled)
            {
                DesktopProfile.UpdateOverlayLights(newFrame);
            }
            
            foreach (var @event in GetOverlayActiveProfiles())
                @event.UpdateOverlayLights(newFrame);

            //Add the Light event that we're previewing to be rendered as an overlay (assuming it's not already active)
            if (preview && Global.Configuration.OverlaysInPreview && !GetOverlayActiveProfiles().Contains(profile))
                profile.UpdateOverlayLights(newFrame);

            if (Global.Configuration.IdleType != IdleEffects.None)
            {
                UpdateIdleEffects(newFrame);
            }
        }

        lock (Effects.CanvasChangedLock)
        {
            Global.effengine.PushFrame(newFrame);
        }

        StopUnUpdatedEvents();
        PostUpdate?.Invoke(this, EventArgs.Empty);
        debugTimer.Restart();
    }

    /// <summary>Gets the current application.</summary>
    /// <param name="preview">Boolean indicating whether the application is selected because it is previewing (true)
    /// or because the process is open (false).</param>
    private ILightEvent GetCurrentProfile(out bool preview)
    {
        var processName = _processMonitor.ProcessName.ToLower();
        var processTitle = _processMonitor.ProcessTitle;
        ILightEvent? profile = null;
        ILightEvent? tempProfile;
        preview = false;

        //TODO: GetProfile that checks based on event type
        if ((tempProfile = GetProfileFromProcessData(processName, processTitle)) != null && tempProfile.IsEnabled)
            profile = tempProfile;
        //Don't check for it being Enabled as a preview should always end-up with the previewed profile regardless of it being disabled
        else if ((tempProfile = GetProfileFromProcessName(_previewModeProfileKey)) != null)
        {
            profile = tempProfile;
            preview = true;
        }
        else if (Global.Configuration.AllowWrappersInBackground
                 && _ipcListener.Result is {IsWrapperConnected: true} 
                 && (tempProfile = GetProfileFromProcessName(_ipcListener.Result!.WrappedProcess)) != null 
                 && tempProfile.IsEnabled)
            profile = tempProfile;

        profile ??= DesktopProfile;

        return profile;
    }

    /// <summary>Gets the current application.</summary>
    private ILightEvent GetCurrentProfile() => GetCurrentProfile(out _);
    /// <summary>
    /// Returns a list of all profiles that should have their overlays active. This will include processes that running but not in the foreground.
    /// </summary>
    /// <returns></returns>
    private IEnumerable<ILightEvent> GetOverlayActiveProfiles()
    {
        return Events.Values.Where(_isOverlayActiveProfile);
    }

    /// <summary>KeyDown handler that checks the current application's profiles for keybinds.
    /// In the case of multiple profiles matching the keybind, it will pick the next one as specified in the Application.Profile order.</summary>
    private void CheckProfileKeybinds(object? sender, EventArgs e)
    {
        var profile = GetCurrentProfile();

        // Check profile is valid and do not switch profiles if the user is trying to enter a keybind
        if (profile is not Application application || Controls.Control_Keybind._ActiveKeybind != null) return;
        // Find all profiles that have their keybinds pressed
        var possibleProfiles = application.Profiles
            .Where(prof => prof.TriggerKeybind.IsPressed())
            .ToList();

        // If atleast one profile has it's key pressed
        if (possibleProfiles.Count <= 0) return;
        // The target profile is the NEXT valid profile after the currently selected one
        // (or the first valid one if the currently selected one doesn't share this keybind)
        var trg = (possibleProfiles.IndexOf(application.Profile) + 1) % possibleProfiles.Count;
        application.SwitchToProfile(possibleProfiles[trg]);
    }

    public void GameStateUpdate(object? sender, IGameState gs)
    {
#if !DEBUG
        try
        {
#endif
            ILightEvent profile;

            var provider = JObject.Parse(gs.GetNode("provider"));
            var appid = provider.GetValue("appid").ToString();
            var name = provider.GetValue("name").ToString().ToLowerInvariant();

            if ((profile = GetProfileFromAppId(appid)) == null &&
                (profile = GetProfileFromProcessName(name)) == null) return;
            var gameState = gs;
            if (profile.Config.GameStateType != null)
                gameState = (IGameState)Activator.CreateInstance(profile.Config.GameStateType, gs.Json);
            profile.SetGameState(gameState);
#if !DEBUG
        }
        catch (Exception e)
        {
            Global.logger.Warning(e, "Exception during GameStateUpdate(), error: ");
        }
#endif
    }

    public void ResetGameState(object? sender, string process)
    {
        var profile = GetProfileFromProcessName(process);
        profile?.ResetGameState();
    }

    public void Dispose()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
        SaveAll();
        foreach (var app in Events)
            app.Value.Dispose();
    }
}

/// <summary>
/// POCO that stores data about a type of layer.
/// </summary>
public class LayerHandlerMeta
{

    /// <summary>Creates a new LayerHandlerMeta object from the given meta attribute and type.</summary>
    public LayerHandlerMeta(Type type, LayerHandlerMetaAttribute? attribute)
    {
        Name = attribute?.Name ?? type.Name.CamelCaseToSpaceCase().TrimEndStr(" Layer Handler");
        Type = type;
        // if the layer is in the Aurora.Settings.Layers namespace, make the IsDefault true unless otherwise specified.
        // If it is in another namespace, it's probably a custom application layer and so make IsDefault false unless otherwise specified
        IsDefault = attribute?.IsDefault ?? type.Namespace == "Aurora.Settings.Layers";
        Order = attribute?.Order ?? 0;
    }

    public string Name { get; }
    public Type Type { get; }
    public bool IsDefault { get; }
    public int Order { get; }
}


/// <summary>
/// Attribute to provide additional meta data about layers for them to be registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class LayerHandlerMetaAttribute : Attribute
{
    /// <summary>A different name for the layer. If not specified, will automatically take it from the layer's class name.</summary>
    public string Name { get; set; }

    /// <summary>If true, this layer will be excluded from automatic registration. Default false.</summary>
    public bool Exclude { get; set; }

    /// <summary>If true, this layer will be registered as a 'default' layer for all applications. Default true.</summary>
    public bool IsDefault { get; set; }

    /// <summary>A number used when ordering the layer entry in the list.
    /// Only to be used for layers that need to appear at the top/bottom of the list.</summary>
    public int Order { get; set; }
}