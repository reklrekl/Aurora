using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aurora.Devices;
using Aurora.Settings.Keycaps;
using Aurora.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RazerSdkReader;
using Application = System.Windows.Application;
using Color = System.Drawing.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Label = System.Windows.Controls.Label;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Aurora.Settings;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public class KeyboardKey
{
    private DeviceKeys? _tag;
    private double? _marginLeft;
    private double? _marginTop;
    private double? _width;
    private double? _height;
    private double? _fontSize;
    private bool? _lineBreak;
    private bool? _absoluteLocation;

    [JsonProperty("visualName")]
    public string VisualName { get; private set; }

    public DeviceKeys Tag
    {
        get => _tag.GetValueOrDefault();
        set => _tag = value;
    }

    public bool LineBreak
    {
        get => _lineBreak.GetValueOrDefault();
        set => _lineBreak = value;
    }

    public double MarginLeft
    {
        get => _marginLeft.GetValueOrDefault();
        set => _marginLeft = value;
    }

    public double MarginTop
    {
        get => _marginTop.GetValueOrDefault();
        set => _marginTop = value;
    }

    public double Width
    {
        get => _width.GetValueOrDefault(30);
        set => _width = value;
    }

    public double Height
    {
        get => _height.GetValueOrDefault(30);
        set => _height = value;
    }

    public double FontSize
    {
        get => _fontSize.GetValueOrDefault(12);
        set => _fontSize = value;
    }

    public bool? Enabled { get; set; } = true;

    public bool AbsoluteLocation
    {
        get => _absoluteLocation.GetValueOrDefault();
        set => _absoluteLocation = value;
    }

    public string Image { get; set; } = "";
    public int ZIndex { get; set; }

    public void UpdateFromOtherKey(KeyboardKey otherKey)
    {
        if (otherKey == null) return;
        if (otherKey.VisualName != null) VisualName = otherKey.VisualName;
        if (otherKey._tag != null) Tag = otherKey.Tag;
        if (otherKey._lineBreak != null) LineBreak = otherKey.LineBreak;
        if (otherKey._width != null) Width = otherKey.Width;
        if (otherKey._height != null) Height = otherKey.Height;
        if (otherKey._fontSize != null) FontSize = otherKey.FontSize;
        if (otherKey._marginLeft != null) MarginLeft = otherKey.MarginLeft;
        if (otherKey._marginTop != null) MarginTop = otherKey.MarginTop;
        if (otherKey.Enabled != null) Enabled = otherKey.Enabled;
        if (otherKey._absoluteLocation != null) AbsoluteLocation = otherKey.AbsoluteLocation;
    }
}

public enum KeyboardRegion
{
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 3,
    BottomRight = 4
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public class VirtualGroupConfiguration
{
    public DeviceKeys[] KeysToRemove { get; set; } = Array.Empty<DeviceKeys>();

    public Dictionary<DeviceKeys, KeyboardKey> KeyModifications { get; set; } = new();

    public Dictionary<DeviceKeys, DeviceKeys> KeyConversion { get; set; } = new();

    /// <summary>
    /// A list of paths for each included group json
    /// </summary>
    public string[] IncludedFeatures { get; set; } = { };
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public class VirtualGroup
{
    public KeyboardRegion OriginRegion { get; set; }

    public List<KeyboardKey> GroupedKeys { get; set; } = new();

    private readonly Dictionary<DeviceKeys, string> _keyText = new();

    private RectangleF _region = new(0, 0, 0, 0);

    public RectangleF Region => _region;

    public Dictionary<DeviceKeys, DeviceKeys> KeyConversion { get; set; }

    public VirtualGroup()
    {
    }

    public VirtualGroup(KeyboardKey[] keys)
    {
        double layoutHeight = 0;
        double layoutWidth = 0;
        double currentHeight = 0;
        double currentWidth = 0;

        foreach (var key in keys)
        {
            GroupedKeys.Add(key);
            _keyText.Add(key.Tag, key.VisualName);

            if (key.Width + key.MarginLeft > 0)
                currentWidth += key.Width + key.MarginLeft;

            if (key.MarginTop > 0)
                currentHeight += key.MarginTop;

            if (layoutWidth < currentWidth)
                layoutWidth = currentWidth;

            if (key.LineBreak)
            {
                currentHeight += 37;
                currentWidth = 0;
            }

            if (layoutHeight < currentHeight)
                layoutHeight = currentHeight;
        }

        _region.Width = (float) layoutWidth;
        _region.Height = (float) layoutHeight;
    }

    public void AddFeature(KeyboardKey[] keys, KeyboardRegion insertionRegion = KeyboardRegion.TopLeft)
    {
        double locationX = 0.0D;
        double locationY = 0.0D;

        switch (insertionRegion)
        {
            case KeyboardRegion.TopRight:
                locationX = _region.Width;
                break;
            case KeyboardRegion.BottomLeft:
                locationY = _region.Height;
                break;
            case KeyboardRegion.BottomRight:
                locationX = _region.Width;
                locationY = _region.Height;
                break;
        }

        float addedWidth = 0.0f;
        float addedHeight = 0.0f;

        foreach (var key in keys)
        {
            key.MarginLeft += locationX;
            key.MarginTop += locationY;

            GroupedKeys.Add(key);
            if (_keyText.ContainsKey(key.Tag))
                _keyText.Remove(key.Tag);
            _keyText.Add(key.Tag, key.VisualName);

            if (key.Width + key.MarginLeft > _region.Width)
                _region.Width = (float) (key.Width + key.MarginLeft);
            else if (key.MarginLeft + addedWidth < 0)
            {
                addedWidth = -(float) key.MarginLeft;
                _region.Width -= (float) key.MarginLeft;
            }

            if (key.Height + key.MarginTop > _region.Height)
                _region.Height = (float) (key.Height + key.MarginTop);
            else if (key.MarginTop + addedHeight < 0)
            {
                addedHeight = -(float) key.MarginTop;
                _region.Height -= (float) key.MarginTop;
            }
        }

        NormalizeKeys();
    }

    private void NormalizeKeys()
    {
        double xCorrection = 0.0D;
        double yCorrection = 0.0D;

        foreach (var key in GroupedKeys.Where(key => key.AbsoluteLocation))
        {
            if (key.MarginLeft < xCorrection)
                xCorrection = key.MarginLeft;

            if (key.MarginTop < yCorrection)
                yCorrection = key.MarginTop;
        }

        if (GroupedKeys.Count <= 0) return;
        GroupedKeys[0].MarginTop -= yCorrection;

        bool previousLinebreak = true;
        foreach (var key in GroupedKeys)
        {
            if (key.AbsoluteLocation)
            {
                key.MarginTop -= yCorrection;
                key.MarginLeft -= xCorrection;
            }
            else
            {
                if (previousLinebreak && !key.LineBreak)
                {
                    key.MarginLeft -= xCorrection;
                }

                previousLinebreak = key.LineBreak;
            }
        }
    }

    internal void AdjustKeys(Dictionary<DeviceKeys, KeyboardKey> keys)
    {
        var applicableKeys = GroupedKeys.FindAll(key => keys.ContainsKey(key.Tag));

        foreach (var key in applicableKeys)
        {
            KeyboardKey otherKey = keys[key.Tag];
            if (key.Tag != otherKey.Tag)
                _keyText.Remove(key.Tag);
            key.UpdateFromOtherKey(otherKey);
            if (_keyText.ContainsKey(key.Tag))
                _keyText[key.Tag] = key.VisualName;
            else
                _keyText.Add(key.Tag, key.VisualName);
        }
    }

    internal void RemoveKeys(DeviceKeys[] keysToRemove)
    {
        GroupedKeys.RemoveAll(key => keysToRemove.Contains(key.Tag));

        double layoutHeight = 0;
        double layoutWidth = 0;
        double currentHeight = 0;
        double currentWidth = 0;

        foreach (var key in GroupedKeys)
        {
            if (key.Width + key.MarginLeft > 0)
                currentWidth += key.Width + key.MarginLeft;

            if (key.MarginTop > 0)
                currentHeight += key.MarginTop;


            if (layoutWidth < currentWidth)
                layoutWidth = currentWidth;

            if (key.LineBreak)
            {
                currentHeight += 37;
                currentWidth = 0;
            }

            if (layoutHeight < currentHeight)
                layoutHeight = currentHeight;

            _keyText.Remove(key.Tag);
        }

        _region.Width = (float) layoutWidth;
        _region.Height = (float) layoutHeight;
    }
}

public class KeyboardLayoutManager
{
    public Dictionary<DeviceKeys, DeviceKeys> LayoutKeyConversion { get; private set; } = new();

    private VirtualGroup _virtualKeyboardGroup;

    private readonly Dictionary<DeviceKeys, IKeycap> _virtualKeyboardMap = new();

    private bool _virtualKbInvalid = true;

    public Grid VirtualKeyboard { get; private set; } = new();

    public Grid AbstractVirtualKeyboard => CreateUserControl(true);

    private bool _bitmapMapInvalid = true;

    public delegate void LayoutUpdatedEventHandler(object? sender);

    public event LayoutUpdatedEventHandler KeyboardLayoutUpdated;

    private const string CulturesFolder = "kb_layouts";

    public PreferredKeyboardLocalization LoadedLocalization { get; private set; } = PreferredKeyboardLocalization.None;

    private readonly string _layoutsPath;

    private Task<ChromaReader?> _rzSdk;

    public KeyboardLayoutManager(Task<ChromaReader?> rzSdk)
    {
        _rzSdk = rzSdk;
        _layoutsPath = Path.Combine(Global.ExecutingDirectory, CulturesFolder);
        Global.Configuration.PropertyChanged += Configuration_PropertyChanged;
    }

    public async Task LoadBrandDefault()
    {
        await LoadBrand(
            Global.Configuration.KeyboardBrand,
            Global.Configuration.MousePreference,
            Global.Configuration.MousepadPreference,
            Global.Configuration.MouseOrientation,
            Global.Configuration.HeadsetPreference,
            Global.Configuration.ChromaLedsPreference
        );
    }

    private async Task LoadBrand(PreferredKeyboard keyboardPreference = PreferredKeyboard.None,
        PreferredMouse mousePreference = PreferredMouse.None,
        PreferredMousepad mousepadPreference = PreferredMousepad.None,
        MouseOrientationType mouseOrientation = MouseOrientationType.RightHanded,
        PreferredHeadset headsetPreference = PreferredHeadset.None,
        PreferredChromaLeds chromaLeds = PreferredChromaLeds.Automatic
    )
    {
#if !DEBUG
        try
        {
#endif

            //Load keyboard layout
            if (!Directory.Exists(_layoutsPath))
            {
                return;
            }

            var layout = Global.Configuration.KeyboardLocalization;

            var culture = layout switch
            {
                PreferredKeyboardLocalization.None => Thread.CurrentThread.CurrentCulture.Name,
                PreferredKeyboardLocalization.intl => "intl",
                PreferredKeyboardLocalization.us => "en-US",
                PreferredKeyboardLocalization.uk => "en-GB",
                PreferredKeyboardLocalization.ru => "ru-RU",
                PreferredKeyboardLocalization.fr => "fr-FR",
                PreferredKeyboardLocalization.de => "de-DE",
                PreferredKeyboardLocalization.jpn => "ja-JP",
                PreferredKeyboardLocalization.nordic => "nordic",
                PreferredKeyboardLocalization.tr => "tr-TR",
                PreferredKeyboardLocalization.swiss => "de-CH",
                PreferredKeyboardLocalization.abnt2 => "pt-BR",
                PreferredKeyboardLocalization.dvorak => "dvorak",
                PreferredKeyboardLocalization.dvorak_int => "dvorak_int",
                PreferredKeyboardLocalization.hu => "hu-HU",
                PreferredKeyboardLocalization.it => "it-IT",
                PreferredKeyboardLocalization.la => "es-AR",
                PreferredKeyboardLocalization.es => "es-ES",
                PreferredKeyboardLocalization.iso => "iso",
                PreferredKeyboardLocalization.ansi => "ansi",
                _ => Thread.CurrentThread.CurrentCulture.Name
            };

            switch (culture)
            {
                case "tr-TR":
                    LoadCulture("tr");
                    break;
                case "ja-JP":
                    LoadCulture("jpn");
                    break;
                case "de-DE":
                case "hsb-DE":
                case "dsb-DE":
                    LoadedLocalization = PreferredKeyboardLocalization.de;
                    LoadCulture("de");
                    break;
                case "fr-CH":
                case "de-CH":
                    LoadedLocalization = PreferredKeyboardLocalization.swiss;
                    LoadCulture("swiss");
                    break;
                case "fr-FR":
                case "br-FR":
                case "oc-FR":
                case "co-FR":
                case "gsw-FR":
                    LoadedLocalization = PreferredKeyboardLocalization.fr;
                    LoadCulture("fr");
                    break;
                case "cy-GB":
                case "gd-GB":
                case "en-GB":
                    LoadedLocalization = PreferredKeyboardLocalization.uk;
                    LoadCulture("uk");
                    break;
                case "ru-RU":
                case "tt-RU":
                case "ba-RU":
                case "sah-RU":
                    LoadedLocalization = PreferredKeyboardLocalization.ru;
                    LoadCulture("ru");
                    break;
                case "en-US":
                    LoadedLocalization = PreferredKeyboardLocalization.us;
                    LoadCulture("us");
                    break;
                case "da-DK":
                case "se-SE":
                case "nb-NO":
                case "nn-NO":
                case "nordic":
                    LoadedLocalization = PreferredKeyboardLocalization.nordic;
                    LoadCulture("nordic");
                    break;
                case "pt-BR":
                    LoadedLocalization = PreferredKeyboardLocalization.abnt2;
                    LoadCulture("abnt2");
                    break;
                case "dvorak":
                    LoadedLocalization = PreferredKeyboardLocalization.dvorak;
                    LoadCulture("dvorak");
                    break;
                case "dvorak_int":
                    LoadedLocalization = PreferredKeyboardLocalization.dvorak_int;
                    LoadCulture("dvorak_int");
                    break;
                case "hu-HU":
                    LoadedLocalization = PreferredKeyboardLocalization.hu;
                    LoadCulture("hu");
                    break;
                case "it-IT":
                    LoadedLocalization = PreferredKeyboardLocalization.it;
                    LoadCulture("it");
                    break;
                case "es-AR":
                case "es-BO":
                case "es-CL":
                case "es-CO":
                case "es-CR":
                case "es-EC":
                case "es-MX":
                case "es-PA":
                case "es-PY":
                case "es-PE":
                case "es-UY":
                case "es-VE":
                case "es-419":
                    LoadedLocalization = PreferredKeyboardLocalization.la;
                    LoadCulture("la");
                    break;
                case "es-ES":
                    LoadedLocalization = PreferredKeyboardLocalization.es;
                    LoadCulture("es");
                    break;
                case "iso":
                    LoadedLocalization = PreferredKeyboardLocalization.iso;
                    LoadCulture("iso");
                    break;
                case "ansi":
                    LoadedLocalization = PreferredKeyboardLocalization.ansi;
                    LoadCulture("ansi");
                    break;
                default:
                    LoadedLocalization = PreferredKeyboardLocalization.intl;
                    LoadCulture("intl");
                    break;
            }

            if (PeripheralLayoutMap.KeyboardLayoutMap.TryGetValue(keyboardPreference, out var keyboardLayoutFile))
            {
                var layoutConfigPath = Path.Combine(_layoutsPath, keyboardLayoutFile);
                LoadKeyboard(layoutConfigPath);
            }

            if (PeripheralLayoutMap.MouseLayoutMap.TryGetValue(mousePreference, out var mouseLayoutJsonFile))
            {
                var mouseFeaturePath = Path.Combine(_layoutsPath, "Extra Features", mouseLayoutJsonFile);

                LoadMouse(mouseOrientation, mouseFeaturePath);
            }

            if (PeripheralLayoutMap.MousepadLayoutMap.TryGetValue(mousepadPreference, out var mousepadLayoutJsonFile))
            {
                var mousepadFeaturePath = Path.Combine(_layoutsPath, "Extra Features", mousepadLayoutJsonFile);

                LoadGenericLayout(mousepadFeaturePath);
            }

            if (PeripheralLayoutMap.HeadsetLayoutMap.TryGetValue(headsetPreference, out var headsetLayoutJsonFile))
            {
                var headsetFeaturePath = Path.Combine(_layoutsPath, "Extra Features", headsetLayoutJsonFile);

                LoadGenericLayout(headsetFeaturePath);
            }

            if (chromaLeds == PreferredChromaLeds.Automatic && await _rzSdk is not null)
            {
                chromaLeds = PreferredChromaLeds.Suggested;
            }
            if (PeripheralLayoutMap.ChromaLayoutMap.TryGetValue(chromaLeds, out var chromaLayoutJsonFile))
            {
                var headsetFeaturePath = Path.Combine(_layoutsPath, "Extra Features", chromaLayoutJsonFile);

                LoadGenericLayout(headsetFeaturePath);
            }
#if !DEBUG
        }
        catch (Exception e)
        {
            Global.logger.Error(e, "Error loading layouts");
        }
#endif

        //Perform end of load functions
        _bitmapMapInvalid = true;
        _virtualKbInvalid = true;
        CalculateBitmap();

        Application.Current.Dispatcher.Invoke(() =>
        {
            CreateUserControl();
            KeyboardLayoutUpdated?.Invoke(this);
        });
    }

    private bool LoadLayout(string path, out VirtualGroup layout)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show( path + " could not be found", "Layout not found", MessageBoxButton.OK);
            layout = null;
            return false;
        }

        var featureContent = File.ReadAllText(path, Encoding.UTF8);
        layout = JsonConvert.DeserializeObject<VirtualGroup>(featureContent,
            new JsonSerializerSettings {ObjectCreationHandling = ObjectCreationHandling.Replace})!;
        return true;
    }

    private void LoadKeyboard(string layoutConfigPath)
    {
        if (!File.Exists(layoutConfigPath))
        {
            MessageBox.Show( layoutConfigPath + " could not be found", "Layout not found", MessageBoxButton.OK);
            return;
        }

        var content = File.ReadAllText(layoutConfigPath, Encoding.UTF8);
        var layoutConfig = JsonConvert.DeserializeObject<VirtualGroupConfiguration>(content,
            new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                NullValueHandling = NullValueHandling.Ignore
            })!;

        _virtualKeyboardGroup.AdjustKeys(layoutConfig.KeyModifications);
        _virtualKeyboardGroup.RemoveKeys(layoutConfig.KeysToRemove);

        foreach (var key in layoutConfig.KeyConversion.Where(key => !LayoutKeyConversion.ContainsKey(key.Key)))
        {
            LayoutKeyConversion.Add(key.Key, key.Value);
        }

        foreach (var feature in layoutConfig.IncludedFeatures)
        {
            var featurePath = Path.Combine(_layoutsPath, "Extra Features", feature);

            if (!File.Exists(featurePath)) continue;
            var featureContent = File.ReadAllText(featurePath, Encoding.UTF8);
            var featureConfig = JsonConvert.DeserializeObject<VirtualGroup>(featureContent,
                new JsonSerializerSettings {ObjectCreationHandling = ObjectCreationHandling.Replace})!;

            _virtualKeyboardGroup.AddFeature(featureConfig.GroupedKeys.ToArray(), featureConfig.OriginRegion);
            if (featureConfig.KeyConversion == null) continue;
            foreach (var key in featureConfig.KeyConversion)
            {
                if (!LayoutKeyConversion.ContainsKey(key.Key))
                    LayoutKeyConversion.Add(key.Key, key.Value);
            }
        }
    }

    private void LoadMouse(MouseOrientationType mouseOrientation, string mouseFeaturePath)
    {
        if (!LoadLayout(mouseFeaturePath, out var featureConfig))
        {
            return;
        }

        if (mouseOrientation == MouseOrientationType.LeftHanded)
        {
            if (featureConfig.OriginRegion == KeyboardRegion.TopRight)
                featureConfig.OriginRegion = KeyboardRegion.TopLeft;
            else if (featureConfig.OriginRegion == KeyboardRegion.BottomRight)
                featureConfig.OriginRegion = KeyboardRegion.BottomLeft;

            double outlineWidth = 0.0;

            foreach (var key in featureConfig.GroupedKeys)
            {
                if (outlineWidth == 0.0 && key.Tag == DeviceKeys.NONE)
                {
                    //We found outline (NOTE: Outline has to be first in the grouped keys)
                    outlineWidth = key.Width + 2 * key.MarginLeft;
                }

                key.MarginLeft -= outlineWidth;
            }
        }

        _virtualKeyboardGroup.AddFeature(featureConfig.GroupedKeys.ToArray(), featureConfig.OriginRegion);
    }

    private void LoadGenericLayout(string headsetFeaturePath)
    {
        if (!LoadLayout(headsetFeaturePath, out var featureConfig))
        {
            return;
        }

        _virtualKeyboardGroup.AddFeature(featureConfig.GroupedKeys.ToArray(), featureConfig.OriginRegion);
    }

    private Func<double, int> _pixelToByte = DefaultPixelToByte;

    private static int DefaultPixelToByte(double pixel)
    {
        return (int) Math.Round(pixel / (double) Global.Configuration.BitmapAccuracy);
    }

    private int PixelToByte(double pixel)
    {
        return _pixelToByte(pixel);
    }

    private void Configuration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is not {PropertyName: nameof(Configuration.BitmapAccuracy)}) return;
        _pixelToByte = DefaultPixelToByte;

        Global.LightingStateManager.PreUpdate += LightingStateManager_PostUpdate;
    }

    private async void LightingStateManager_PostUpdate(object? sender, EventArgs e)
    {
        await LoadBrandDefault();
        Global.LightingStateManager.PreUpdate -= LightingStateManager_PostUpdate;
    }

    private void CalculateBitmap()
    {
        if (!_bitmapMapInvalid) return;
        double curWidth = 0;
        double curHeight = 0;
        double widthMax = 1;
        double heightMax = 1;
        var bitmapMap = new Dictionary<DeviceKeys, BitmapRectangle>(Effects.MaxDeviceId, EnumHashGetter.Instance as IEqualityComparer<DeviceKeys>);

        foreach (var key in _virtualKeyboardGroup.GroupedKeys)
        {
            if (key.Tag.Equals(DeviceKeys.NONE))
                continue;

            var width = key.Width;
            var widthBit = PixelToByte(width);
            var height = key.Height;
            var heightBit = PixelToByte(height);
            var xOffset = key.MarginLeft;
            var yOffset = key.MarginTop;
            double brX, brY;

            if (key.AbsoluteLocation)
            {
                bitmapMap[key.Tag] =
                    new BitmapRectangle(PixelToByte(xOffset), PixelToByte(yOffset), widthBit, heightBit);
                brX = xOffset + width;
                brY = yOffset + height;
            }
            else
            {
                var x = xOffset + curWidth;
                var y = yOffset + curHeight;

                bitmapMap[key.Tag] = new BitmapRectangle(PixelToByte(x), PixelToByte(y), widthBit, heightBit);

                brX = x + width;
                brY = y + height;

                if (key.LineBreak)
                {
                    curHeight += 37;
                    curWidth = 0;
                }
                else
                {
                    curWidth = brX;
                    if (y > curHeight)
                        curHeight = y;
                }
            }

            if (brX > widthMax) widthMax = brX;
            if (brY > heightMax) heightMax = brY;
        }

        _bitmapMapInvalid = false;
        //+1 for rounding error, where the bitmap rectangle B(X)+B(Width) > B(X+Width)
        Global.effengine.SetCanvasSize(
            PixelToByte(_virtualKeyboardGroup.Region.Width) + 1,
            PixelToByte(_virtualKeyboardGroup.Region.Height) + 1);
        Global.effengine.SetBitmapping(bitmapMap);
    }

    private Grid CreateUserControl(bool abstractKeycaps = false)
    {
        if (_virtualKbInvalid && !abstractKeycaps)
            _virtualKeyboardMap.Clear();

        Grid newVirtualKeyboard = new Grid();

        double layoutHeight = 0;
        double layoutWidth = 0;

        double baselineX = 0.0;
        double baselineY = 0.0;
        double currentHeight = 0;
        double currentWidth = 0;

        string imagesPath = Path.Combine(_layoutsPath, "Extra Features", "images");

        foreach (KeyboardKey key in _virtualKeyboardGroup.GroupedKeys.OrderBy(a => a.ZIndex))
        {
            double keyMarginLeft = key.MarginLeft;
            double keyMarginTop = key.MarginTop;

            string imagePath = "";

            if (!string.IsNullOrWhiteSpace(key.Image))
                imagePath = Path.Combine(imagesPath, key.Image);

            UserControl keycap;

            //Ghost keycap is used for abstract representation of keys
            if (abstractKeycaps)
                keycap = new Control_GhostKeycap(key, imagePath);
            else
            {
                switch (Global.Configuration.VirtualkeyboardKeycapType)
                {
                    case KeycapType.Default_backglow:
                        keycap = new Control_DefaultKeycapBackglow(key, imagePath);
                        break;
                    case KeycapType.Default_backglow_only:
                        keycap = new Control_DefaultKeycapBackglowOnly(key, imagePath);
                        break;
                    case KeycapType.Colorized:
                        keycap = new Control_ColorizedKeycap(key, imagePath);
                        break;
                    case KeycapType.Colorized_blank:
                        keycap = new Control_ColorizedKeycapBlank(key, imagePath);
                        break;
                    default:
                        keycap = new Control_DefaultKeycap(key, imagePath);
                        break;
                }
            }

            newVirtualKeyboard.Children.Add(keycap);

            if (key.Tag != DeviceKeys.NONE && !_virtualKeyboardMap.ContainsKey(key.Tag) && keycap is IKeycap &&
                !abstractKeycaps)
                _virtualKeyboardMap.Add(key.Tag, keycap as IKeycap);

            if (key.AbsoluteLocation)
                keycap.Margin = new Thickness(key.MarginLeft, key.MarginTop, 0, 0);
            else
                keycap.Margin = new Thickness(currentWidth + key.MarginLeft, currentHeight + key.MarginTop, 0, 0);

            if (key.Tag == DeviceKeys.ESC)
            {
                baselineX = keycap.Margin.Left;
                baselineY = keycap.Margin.Top;
            }

            if (!key.AbsoluteLocation)
            {
                if (key.Width + keyMarginLeft > 0)
                    currentWidth += key.Width + keyMarginLeft;

                if (keyMarginTop > 0)
                    currentHeight += keyMarginTop;


                if (layoutWidth < currentWidth)
                    layoutWidth = currentWidth;

                if (key.LineBreak)
                {
                    currentHeight += 37;
                    currentWidth = 0;
                }

                if (layoutHeight < currentHeight)
                    layoutHeight = currentHeight;
            }
        }

        if (_virtualKeyboardGroup.GroupedKeys.Count == 0)
        {
            //No items, display error
            Label errorMessage = new Label();

            DockPanel infoPanel = new DockPanel();

            TextBlock infoMessage = new TextBlock
            {
                Text = "No keyboard selected\r\nPlease select your keyboard in the settings",
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 0, 0)),
            };

            DockPanel.SetDock(infoMessage, Dock.Top);
            infoPanel.Children.Add(infoMessage);

            DockPanel infoInstruction = new DockPanel();

            infoInstruction.Children.Add(new TextBlock
            {
                Text = "Press (",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center
            });

            infoInstruction.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(@"Resources/settings_icon.png", UriKind.Relative)),
                Stretch = Stretch.Uniform,
                Height = 40.0,
                VerticalAlignment = VerticalAlignment.Center
            });

            infoInstruction.Children.Add(new TextBlock
            {
                Text = ") and go into \"Devices & Wrappers\" tab",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center
            });

            DockPanel.SetDock(infoInstruction, Dock.Bottom);
            infoPanel.Children.Add(infoInstruction);

            errorMessage.Content = infoPanel;

            errorMessage.FontSize = 16.0;
            errorMessage.FontWeight = FontWeights.Bold;
            errorMessage.HorizontalContentAlignment = HorizontalAlignment.Center;
            errorMessage.VerticalContentAlignment = VerticalAlignment.Center;

            newVirtualKeyboard.Children.Add(errorMessage);

            //Update size
            newVirtualKeyboard.Width = 850;
            newVirtualKeyboard.Height = 200;
        }
        else
        {
            //Update size
            newVirtualKeyboard.Width = _virtualKeyboardGroup.Region.Width;
            newVirtualKeyboard.Height = _virtualKeyboardGroup.Region.Height;
        }

        if (_virtualKbInvalid && !abstractKeycaps)
        {
            VirtualKeyboard.Children.Clear();
            VirtualKeyboard = newVirtualKeyboard;

            Effects.GridBaselineX = (float) baselineX;
            Effects.GridBaselineY = (float) baselineY;
            Effects.GridHeight = (float) newVirtualKeyboard.Height;
            Effects.GridWidth = (float) newVirtualKeyboard.Width;

            _virtualKbInvalid = false;
        }

        return newVirtualKeyboard;
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    private sealed class KeyboardLayout
    {
        public Dictionary<DeviceKeys, DeviceKeys> KeyConversion;

        public KeyboardKey[] Keys;
    }

    private void LoadCulture(string culture)
    {
        var fileName = "Plain Keyboard\\layout." + culture + ".json";
        var layoutPath = Path.Combine(_layoutsPath, fileName);

        if (!File.Exists(layoutPath))
        {
            return;
        }

        var content = File.ReadAllText(layoutPath, Encoding.UTF8);
        var keyboard = JsonConvert.DeserializeObject<KeyboardLayout>(content,
            new JsonSerializerSettings {ObjectCreationHandling = ObjectCreationHandling.Replace})!;

        _virtualKeyboardGroup = new VirtualGroup(keyboard.Keys);

        LayoutKeyConversion = keyboard.KeyConversion ?? new Dictionary<DeviceKeys, DeviceKeys>();
    }

    public void SetKeyboardColors(Dictionary<DeviceKeys, Color> keyLights)
    {
        foreach (var (key, value) in _virtualKeyboardMap)
        {
            if (!keyLights.TryGetValue(key, out var keyColor)) continue;
            var drawingColor = Color.FromArgb(255, ColorUtils.MultiplyColorByScalar(keyColor, keyColor.A / 255.0D));
            value.SetColor(ColorUtils.DrawingColorToMediaColor(drawingColor));
        }
    }
}