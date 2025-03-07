﻿using Aurora.EffectsEngine;
using Aurora.EffectsEngine.Animations;
using Aurora.Profiles;
using Aurora.Settings.Overrides;
using Aurora.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Controls;
using Aurora.Settings.Layers.Controls;

namespace Aurora.Settings.Layers {

    public class TimerLayerHandlerProperties : LayerHandlerProperties2Color<TimerLayerHandlerProperties> {

        public TimerLayerHandlerProperties() : base() { }
        public TimerLayerHandlerProperties(bool assign_default) : base(assign_default) { }

        public Keybind[] _TriggerKeys { get; set; }
        [JsonIgnore]
        public Keybind[] TriggerKeys { get { return Logic._TriggerKeys ?? _TriggerKeys ?? new Keybind[] { }; } }

        [LogicOverridable("Duration")]
        public int? _Duration { get; set; }
        [JsonIgnore]
        public int Duration { get { return Logic._Duration ?? _Duration ?? 0; } }

        [LogicOverridable("Animation Type")]
        public TimerLayerAnimationType? _AnimationType { get; set; }
        [JsonIgnore]
        public TimerLayerAnimationType AnimationType { get { return Logic._AnimationType ?? _AnimationType ?? TimerLayerAnimationType.OnOff; } }

        [LogicOverridable("Repeat Action")]
        public TimerLayerRepeatPressAction? _RepeatAction { get; set; }
        [JsonIgnore]
        public TimerLayerRepeatPressAction RepeatAction { get { return Logic._RepeatAction ?? _RepeatAction ?? TimerLayerRepeatPressAction.Reset; } }

        public override void Default() {
            base.Default();
            _TriggerKeys = new Keybind[] { };
            _Duration = 5000;
            _AnimationType = TimerLayerAnimationType.OnOff;
            _RepeatAction = TimerLayerRepeatPressAction.Reset;
        }
    }

    public class TimerLayerHandler : LayerHandler<TimerLayerHandlerProperties> {

        private CustomTimer timer;
        private bool isActive = false;

        public TimerLayerHandler() : base() {
            timer = new CustomTimer();
            timer.Trigger += Timer_Elapsed;

            Global.InputEvents.KeyDown += InputEvents_KeyDown;
        }
        
        public override void Dispose() {
            base.Dispose();
            Global.InputEvents.KeyDown -= InputEvents_KeyDown;
        }

        protected override UserControl CreateControl() {
            return new Control_TimerLayer(this);
        }

        public override EffectLayer Render(IGameState gamestate) {
            EffectLayer layer = new EffectLayer("TimerLayer");
            if (isActive) {
                switch (Properties.AnimationType) {
                    case TimerLayerAnimationType.OnOff:
                        layer.Set(Properties.Sequence, Properties.SecondaryColor);
                        break;

                    case TimerLayerAnimationType.Fade:
                        layer.Set(Properties.Sequence, ColorUtils.BlendColors(Properties.SecondaryColor, Properties.PrimaryColor, timer.InterpolationValue));
                        break;

                    case TimerLayerAnimationType.Progress:
                    case TimerLayerAnimationType.ProgressGradual:
                        layer.PercentEffect(Properties.SecondaryColor, Properties.PrimaryColor, Properties.Sequence, timer.InterpolationValue, 1, Properties.AnimationType == TimerLayerAnimationType.Progress ? PercentEffectType.Progressive : PercentEffectType.Progressive_Gradual);
                        break;
                }
            } else
                layer.Set(Properties.Sequence, Properties.PrimaryColor);
            return layer;
        }

        private void Timer_Elapsed(object? sender) {
            isActive = false;
        }

        private void InputEvents_KeyDown(object? sender, EventArgs e) {
            foreach (var keybind in Properties.TriggerKeys) {
                if (keybind.IsPressed()) {
                    switch (Properties.RepeatAction) {
                        // Restart the timer from scratch
                        case TimerLayerRepeatPressAction.Reset:
                            timer.Reset(Properties.Duration);
                            isActive = true;
                            break;

                        case TimerLayerRepeatPressAction.Extend:
                            timer.Extend(Properties.Duration);
                            isActive = true;
                            break;

                        case TimerLayerRepeatPressAction.Cancel:
                            if (isActive)
                                timer.Stop();
                            else
                                timer.Reset(Properties.Duration);
                            isActive = !isActive;
                            break;
                        
                        case TimerLayerRepeatPressAction.Ignore:
                            if (!isActive) {
                                timer.Reset(Properties.Duration);
                                isActive = true;
                            }
                            break;
                    }
                    return;
                }
            }
        }
    }

    class CustomTimer {

        public delegate void TriggerHandler(object? sender);
        public event TriggerHandler Trigger;

        private Timer timer;
        private double max = 0;
        private DateTime startAt;

        public CustomTimer() {
            timer = new Timer();
            timer.AutoReset = false;
            timer.Elapsed += Timer_Elapsed;
        }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e) {
            Trigger?.Invoke(this);
            timer.Enabled = false;
        }

        /// <summary>Stops the timer and restarts it with the given time.</summary>
        private void SetTimer(double t) {
            timer.Stop();
            timer.Interval = t;
            timer.Start();
        }

        public void Reset(double t) {
            SetTimer(t);
            startAt = DateTime.Now;
            max = t;
        }

        public void Stop() {
            timer.Stop();
        }

        public void Extend(double t) {
            // If the timer's not running, behave like Reset
            if (!timer.Enabled)
                Reset(t);

            // If timer is running
            else {
                max += t; // extend max
                SetTimer(max - Current);
            }
        }

        /// <summary>Gets how many milliseconds has elapsed since starting timer.</summary>
        public int Current {
            get {
                return (int)(DateTime.Now - startAt).TotalMilliseconds;
            }
        }

        /// <summary>Gets how far through the timer is as a value between 0 and 1 (for use with the fade animation mode).</summary>
        public double InterpolationValue {
            get {
                return Current / max;
            }
        }

        public void Disponse() {
            timer.Dispose();
        }
    }
    
    public enum TimerLayerAnimationType {
        [Description("On/Off")] OnOff,
        Fade,
        Progress,
        [Description("Progress (Gradual)")] ProgressGradual
    }

    public enum TimerLayerRepeatPressAction {
        [Description("Restart")] Reset,
        [Description("Extend")] Extend,
        [Description("Cancel")] Cancel,
        [Description("Ignore")] Ignore
    }
}
