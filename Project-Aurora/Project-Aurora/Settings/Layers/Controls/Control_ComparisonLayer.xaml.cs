﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aurora.Settings.Overrides.Logic;
using Aurora.Utils;
using Xceed.Wpf.Toolkit;

namespace Aurora.Settings.Layers.Controls {
    /// <summary>
    /// Interaction logic for Control_ComparisonLayer.xaml
    /// </summary>
    public partial class Control_ComparisonLayer : UserControl {

        private bool settingsset = false;

        public Control_ComparisonLayer() {
            InitializeComponent();

            // Setup the operator list
            foreach (var op in Enum.GetValues(typeof(ComparisonOperator)).Cast<ComparisonOperator>())
                @operator.Items.Add(new KeyValuePair<string, ComparisonOperator>(op.GetDescription(), op));
            @operator.DisplayMemberPath = "Key";
        }

        public Control_ComparisonLayer(ComparisonLayerHandler datacontext) : this() {
            this.DataContext = datacontext;
        }

        private ComparisonLayerHandler Context => (ComparisonLayerHandler)DataContext;
        private bool CanSet => IsLoaded && settingsset && DataContext is ComparisonLayerHandler;

        public void SetSettings() {
            if (DataContext is ComparisonLayerHandler && !settingsset) {
                operand1Path.Text = Context.Properties._Operand1Path;
                @operator.SelectedIndex = @operator.Items.SourceCollection.Cast<KeyValuePair<string, ComparisonOperator>>().Select((kvp, index) => new { kvp, index }).First(item => item.kvp.Value == Context.Properties.Operator).index;
                operand2Path.Text = Context.Properties._Operand2Path;
                trueColor.SelectedColor = ColorUtils.DrawingColorToMediaColor(Context.Properties._PrimaryColor ?? System.Drawing.Color.Empty);
                falseColor.SelectedColor = ColorUtils.DrawingColorToMediaColor(Context.Properties._SecondaryColor ?? System.Drawing.Color.Empty);
                keySequence.Sequence = Context.Properties._Sequence;

                settingsset = true;
            }
        }

        internal void SetProfile(Profiles.Application profile) {
            settingsset = false;
            this.SetSettings();
        }

        private void UserControl_Loaded(object? sender, RoutedEventArgs e) {
            SetSettings();
            this.Loaded -= UserControl_Loaded;
        }

        private void operand1Path_TextChanged(object? sender, TextChangedEventArgs e) {
            if (CanSet)
                Context.Properties._Operand1Path = (sender as ComboBox).Text;
        }

        private void operator_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (CanSet)
                Context.Properties._Operator = ((KeyValuePair<string, ComparisonOperator>)(sender as ComboBox).SelectedItem).Value;
        }

        private void operand2Path_TextChanged(object? sender, TextChangedEventArgs e) {
            if (CanSet)
                Context.Properties._Operand2Path = (sender as ComboBox).Text;
        }

        private void trueColor_SelectedColorChanged(object? sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (CanSet && (sender as ColorPicker).SelectedColor.HasValue)
                Context.Properties._PrimaryColor = Utils.ColorUtils.MediaColorToDrawingColor((sender as ColorPicker).SelectedColor.Value);
        }

        private void falseColor_SelectedColorChanged(object? sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (CanSet && (sender as ColorPicker).SelectedColor.HasValue)
                Context.Properties._SecondaryColor = Utils.ColorUtils.MediaColorToDrawingColor((sender as ColorPicker).SelectedColor.Value);
        }

        private void keySequence_SequenceUpdated(object? sender, EventArgs e) {
            if (CanSet)
                Context.Properties._Sequence = (sender as Aurora.Controls.KeySequence).Sequence;
        }
    }
}
