﻿using System.Windows;
using System.Windows.Controls;

namespace Aurora.Settings.Layers.Controls
{
    /// <summary>
    /// Interaction logic for Control_ScriptLayer.xaml
    /// </summary>
    public partial class Control_ScriptLayer : UserControl
    {
        private Profiles.Application Application { get; set; }

        public Control_ScriptLayer()
        {
            InitializeComponent();
        }

        public Control_ScriptLayer(ScriptLayerHandler layerHandler) : this()
        {
            this.DataContext = layerHandler;
            this.SetProfile(layerHandler.ProfileManager);
            this.UpdateScriptSettings();
        }

        public void SetProfile(Profiles.Application application)
        {
            this.cboScripts.ItemsSource = application.EffectScripts.Keys;
            this.cboScripts.IsEnabled = application.EffectScripts.Keys.Count > 0;
            this.Application = application;
        }

        private void cboScripts_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            ScriptLayerHandler handler = (ScriptLayerHandler)this.DataContext;
            handler.OnScriptChanged();
            this.UpdateScriptSettings();
        }

        private void UpdateScriptSettings()
        {
            ScriptLayerHandler handler = (ScriptLayerHandler)this.DataContext;
            this.ScriptPropertiesEditor.RegisteredVariables = handler.GetScriptPropertyRegistry();
            VariableRegistry varReg = this.ScriptPropertiesEditor.RegisteredVariables;
            ScriptPropertiesEditor.Visibility = varReg == null || varReg.Count == 0 ? Visibility.Hidden : Visibility.Visible;
            ScriptPropertiesEditor.VarRegistrySource = handler.IsScriptValid ? handler.Properties._ScriptProperties : null;
        }

        private void refreshScriptList_Click(object? sender, RoutedEventArgs e) {
            Application.ForceScriptReload();
            cboScripts.Items.Refresh();
            cboScripts.IsEnabled = Application.EffectScripts.Keys.Count > 0;
        }
    }
}
