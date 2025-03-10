using Aurora.EffectsEngine;
using Aurora.Profiles;
using Aurora.Settings.Overrides;
using Newtonsoft.Json;
using System;
using System.Drawing;
using System.Windows.Controls;
using FastMember;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using System.Windows;
using Aurora.Settings.Layers.Controls;
using JetBrains.Annotations;
using Lombok.NET;
using Application = Aurora.Profiles.Application;

namespace Aurora.Settings.Layers
{
    [UsedImplicitly(ImplicitUseKindFlags.InstantiatedWithFixedConstructorSignature, ImplicitUseTargetFlags.WithInheritors)]
    [NotifyPropertyChanged]
    public abstract partial class LayerHandlerProperties<TProperty> : IValueOverridable, IDisposable where TProperty : LayerHandlerProperties<TProperty>
    {
        private static readonly Lazy<TypeAccessor> Accessor = new(() => TypeAccessor.Create(typeof(TProperty)));

        [GameStateIgnore, JsonIgnore]
        public TProperty Logic { get; private set; }

        [JsonIgnore]
        private Color? _primaryColor;

        [LogicOverridable("Primary Color")]
        public Color? _PrimaryColor
        {
            get => _primaryColor;
            set
            {
                _primaryColor = value;
                PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(""));
            }
        }

        [JsonIgnore]
        public Color PrimaryColor => Logic?._PrimaryColor ?? _PrimaryColor ?? Color.Empty;

        [JsonIgnore]
        private KeySequence _sequence;

        [LogicOverridable("Affected Keys")]
        public virtual KeySequence _Sequence
        {
            get => _sequence;
            set => SetFieldAndRaisePropertyChanged(out _sequence, value);
        }

        [JsonIgnore]
        public KeySequence Sequence => Logic._Sequence ?? _Sequence;


        #region Override Special Properties
        // These properties are special in that they are designed only for use with the overrides system and
        // allows the overrides to access properties not actually present on the Layer.Handler.Properties.
        // Note that this is NOT the variable that is changed when the user changes one of these settings in the
        // UI (for example not changed when an item in the layer list is enabled/disabled with the checkbox).
        [LogicOverridable("Enabled")]
        public bool? _Enabled { get; set; }
        [JsonIgnore]
        public bool Enabled => Logic._Enabled ?? _Enabled ?? true;

        // Renamed to "Layer Opacity" so that if the layer properties needs an opacity for whatever reason, it's
        // less likely to have a name collision.
        [LogicOverridable("Opacity")]
        public float? _LayerOpacity { get; set; }
        [JsonIgnore]
        public float LayerOpacity => Logic._LayerOpacity ?? _LayerOpacity ?? 1f;

        [LogicOverridable("Excluded Keys")]
        public KeySequence _Exclusion { get; set; }
        [JsonIgnore]
        public KeySequence Exclusion => Logic._Exclusion ?? _Exclusion ?? new KeySequence();
        #endregion

        public LayerHandlerProperties() : this(false)
        {
            Default();
        }

        public LayerHandlerProperties(bool empty)
        {
            if (!empty)
                Default();
        }

        public virtual void Default()
        {
            if (Logic != null)
            {
                Logic.PropertyChanged -= OnPropertiesChanged;
            }
            Logic = (TProperty)Activator.CreateInstance(typeof(TProperty), new object[] { true })!;
            Logic.PropertyChanged += OnPropertiesChanged;
            _PrimaryColor = Utils.ColorUtils.GenerateRandomColor();
            if (_Sequence != null)
            {
                _Sequence.Freeform.ValuesChanged -= OnPropertiesChanged;
            }
            _Sequence = new KeySequence();
            _Sequence.Freeform.ValuesChanged += OnPropertiesChanged;
        }

        public object GetOverride(string propertyName) {
            try {
                return Accessor.Value[Logic, propertyName];
            } catch (ArgumentOutOfRangeException) {
                return null;
            }
        }

        public void SetOverride(string propertyName, object value) {
            try {
                if (Accessor.Value[Logic, propertyName] == value)
                {
                    return;
                }
                if (value == null || !value.Equals(Accessor.Value[Logic, propertyName]))
                {
                    Accessor.Value[Logic, propertyName] = value;
                }
            } catch (ArgumentOutOfRangeException) { }
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context)
        {
            OnPropertiesChanged(this, new PropertyChangedEventArgs(""));
        }

        public void OnPropertiesChanged(object? sender, object args = null)
        {
            PropertyChanged?.Invoke(sender, new PropertyChangedEventArgs(""));
        }

        public void OnPropertiesChanged(object? sender)
        {
            PropertyChanged?.Invoke(sender, new PropertyChangedEventArgs(""));
        }

        public void Dispose()
        {
            _Sequence.Freeform.ValuesChanged -= OnPropertiesChanged;
        }
    }

    public class LayerHandlerProperties2Color<TProperty> : LayerHandlerProperties<TProperty> where TProperty : LayerHandlerProperties2Color<TProperty>
    {
        private Color? _secondaryColor;

        [LogicOverridable("Secondary Color")]
        public Color? _SecondaryColor
        {
            get => _secondaryColor;
            set
            {
                _secondaryColor = value;
                OnPropertiesChanged(null);
            }
        }

        [JsonIgnore]
        public Color SecondaryColor => Logic._SecondaryColor ?? _SecondaryColor ?? Color.Empty;

        public LayerHandlerProperties2Color(bool assignDefault = false) : base(assignDefault) { }

        public override void Default()
        {
            base.Default();
            _SecondaryColor = Utils.ColorUtils.GenerateRandomColor();
        }
    }

    public class LayerHandlerProperties : LayerHandlerProperties<LayerHandlerProperties>
    {
        public LayerHandlerProperties()
        { }

        public LayerHandlerProperties(bool assignDefault = false) : base(assignDefault) { }
    }

    public interface ILayerHandler: IDisposable
    {
        UserControl Control { get; }

        object Properties { get; set; }

        bool EnableSmoothing { get; set; }

        bool EnableExclusionMask { get; }
        bool? _EnableExclusionMask { get; set; }

        KeySequence ExclusionMask { get; }
        KeySequence _ExclusionMask { get; set; }

        float Opacity { get; }
        float? _Opacity { get; set; }

        EffectLayer Render(IGameState gamestate);

        EffectLayer PostRenderFX(EffectLayer layerRender);

        void SetApplication(Application profile);
        void SetGameState(IGameState gamestate);
        void Dispose();
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public abstract class LayerHandler<TProperty> : ILayerHandler where TProperty : LayerHandlerProperties<TProperty>
    {
        [JsonIgnore]
        public Application Application { get; protected set; }

        [JsonIgnore]
        protected UserControl _Control;

        [JsonIgnore]
        public UserControl Control => _Control ??= CreateControl();

        private TProperty _properties = Activator.CreateInstance<TProperty>();
        public TProperty Properties
        {
            get => _properties;
            set
            {
                _properties.Sequence.Freeform.ValuesChanged -= PropertiesChanged;
                _properties.PropertyChanged -= PropertiesChanged;
                _properties = value;
                value.PropertyChanged += PropertiesChanged;
                value.Sequence.Freeform.ValuesChanged += PropertiesChanged;
                value.OnPropertiesChanged(this);
            }
        }

        object ILayerHandler.Properties {
            get => Properties;
            set => Properties = value as TProperty;
        }
        
        public bool EnableSmoothing { get; set; }

        // Always return true if the user is overriding the exclusion zone (so that we don't have to present the user with another
        // option in the overrides asking if they want to enabled/disable it), otherwise if there isn't an overriden value for
        // exclusion, simply return the value of the settings checkbox (as normal)
        [JsonIgnore]
        public bool EnableExclusionMask => Properties.Logic._Exclusion != null || (_EnableExclusionMask ?? false);
        public bool? _EnableExclusionMask { get; set; }

        [JsonIgnore]
        public KeySequence ExclusionMask => Properties.Exclusion;
        public KeySequence _ExclusionMask {
            get => Properties._Exclusion;
            set => Properties._Exclusion = value;
        }

        public float Opacity => Properties.LayerOpacity;
        public float? _Opacity {
            get => Properties._LayerOpacity;
            set => Properties._LayerOpacity = value;
        }

        [JsonIgnore]
        private TextureBrush _previousRender = EffectLayer.EmptyLayer.TextureBrush; //Previous layer

        [JsonIgnore]
        private TextureBrush _previousSecondRender = EffectLayer.EmptyLayer.TextureBrush; //Layer before previous

        [JsonIgnore]
        private readonly Lazy<EffectLayer> _effectLayer;

        private static PropertyChangedEventArgs ConstPropertyChangedEventArgs = new("");
        protected EffectLayer EffectLayer
        {
            get
            {
                if (!_effectLayer.IsValueCreated)
                {
                    var _ = _effectLayer.Value;
                    PropertiesChanged(this, ConstPropertyChangedEventArgs);
                }
                return _effectLayer.Value;
            }
        }

        protected LayerHandler(): this("Unoptimized Layer"){}

        protected LayerHandler(string name)
        {
            var colorMatrix1 = new ColorMatrix
            {
                Matrix33 = 0.6f
            };
            _prevImageAttributes = new();
            _prevImageAttributes.SetColorMatrix(colorMatrix1);
            var colorMatrix2 = new ColorMatrix
            {
                Matrix33 = 0.4f
            };
            _secondPrevImageAttributes = new();
            _secondPrevImageAttributes.SetColorMatrix(colorMatrix2);

            _effectLayer = new(() => new EffectLayer(name, true));
            _ExclusionMask = new KeySequence();
            Properties.PropertyChanged += PropertiesChanged;
            WeakEventManager<Effects, EventArgs>.AddHandler(null, nameof(Effects.CanvasChanged), PropertiesChanged);
        }

        public virtual EffectLayer Render(IGameState gamestate)
        {
            return EffectLayer.EmptyLayer;
        }

        public virtual void SetGameState(IGameState gamestate)
        {

        }

        private readonly Lazy<EffectLayer> _postfxLayer = new(() => new EffectLayer("PostFXLayer", true));
        private readonly ImageAttributes _prevImageAttributes;
        private readonly ImageAttributes _secondPrevImageAttributes;

        public EffectLayer PostRenderFX(EffectLayer renderedLayer)
        {
            if (EnableSmoothing)
            {
                SmoothLayer(renderedLayer);
            }

            //Last PostFX is exclusion
            renderedLayer.Exclude(EnableExclusionMask ? ExclusionMask : KeySequence.Empty);

            renderedLayer *= Properties.LayerOpacity;

            return renderedLayer;
        }

        private void SmoothLayer(EffectLayer renderedLayer)
        {
            var returnLayer = _postfxLayer.Value;
            returnLayer.Clear();

            using (var g = returnLayer.GetGraphics())
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.Low;

                g.DrawImage(renderedLayer.TextureBrush.Image,
                    renderedLayer.Dimension, renderedLayer.Dimension,
                    GraphicsUnit.Pixel
                );
                g.FillRectangle(_previousRender, renderedLayer.Dimension);
                g.FillRectangle(_previousSecondRender, renderedLayer.Dimension);
            }

            try
            {
                //Update previous layers
                _previousSecondRender = new TextureBrush(_previousRender.Image, renderedLayer.Dimension, _secondPrevImageAttributes);
                _previousRender = new TextureBrush(renderedLayer.TextureBrush.Image, renderedLayer.Dimension, _prevImageAttributes);
            }
            catch (Exception e) //canvas changes
            {
                _previousSecondRender = EffectLayer.EmptyLayer.TextureBrush;
                _previousRender = EffectLayer.EmptyLayer.TextureBrush;
            }
        }

        public virtual void SetApplication(Application profile)
        {
            Application = profile;
        }

        protected virtual UserControl CreateControl()
        {
            return new Control_DefaultLayer();
        }

        [OnDeserialized]
        [UsedImplicitly]
        private void OnDeserialized(StreamingContext context)
        {
            PropertiesChanged(this, new PropertyChangedEventArgs(""));
        }
        
        protected virtual void PropertiesChanged(object? sender, PropertyChangedEventArgs args)
        {
            
        }

        private void PropertiesChanged(object? sender, EventArgs e)
        {
            PropertiesChanged(sender, ConstPropertyChangedEventArgs);
        }

        private void PropertiesChanged(object? sender, FreeFormChangedEventArgs e)
        {
            PropertiesChanged(sender, ConstPropertyChangedEventArgs);
        }

        public virtual void Dispose()
        {
            if (_effectLayer.IsValueCreated)
            {
                _effectLayer.Value.Dispose();
            }
            Properties.PropertyChanged -= PropertiesChanged;
            WeakEventManager<Effects, EventArgs>.RemoveHandler(null, nameof(Effects.CanvasChanged), PropertiesChanged);
        }
    }

    [LayerHandlerMeta(Exclude = true)]
    public class LayerHandler : LayerHandler<LayerHandlerProperties>
    {
        public LayerHandler(string name) : base(name)
        {
        }
    }


    public interface IValueOverridable {
        /// <summary>
        /// Gets the overriden value of the speicifed property.
        /// </summary>
        object GetOverride(string propertyName);

        /// <summary>
        /// Sets the overriden value of the speicifed property to the given value.
        /// </summary>
        void SetOverride(string propertyName, object value);
    }
}
