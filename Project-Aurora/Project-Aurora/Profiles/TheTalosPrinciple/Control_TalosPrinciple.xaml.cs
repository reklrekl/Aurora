﻿using Aurora.Controls;
using Aurora.Settings;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Aurora.Profiles.TheTalosPrinciple
{
    /// <summary>
    /// Interaction logic for Control_TalosPrinciple.xaml
    /// </summary>
    public partial class Control_TalosPrinciple : UserControl
    {
        private Application profile_manager;

        public Control_TalosPrinciple(Application profile)
        {
            InitializeComponent();

            profile_manager = profile;

            SetSettings();

            //Apply LightFX Wrapper, if needed.
            if (!(profile_manager.Settings as FirstTimeApplicationSettings).IsFirstTimeInstalled)
            {
                InstallWrapper();
                (profile_manager.Settings as FirstTimeApplicationSettings).IsFirstTimeInstalled = true;
            }

            profile_manager.ProfileChanged += Profile_manager_ProfileChanged;
        }

        private void Profile_manager_ProfileChanged(object? sender, EventArgs e)
        {
            SetSettings();
        }

        private void SetSettings()
        {
            this.game_enabled.IsChecked = profile_manager.Settings.IsEnabled;
        }

        private void patch_button_Click(object? sender, RoutedEventArgs e)
        {
            if (InstallWrapper())
                MessageBox.Show("Aurora LightFX Wrapper installed successfully.");
            else
                MessageBox.Show("Aurora LightFX Wrapper could not be installed.\r\nGame is not installed.");
        }

        private void unpatch_button_Click(object? sender, RoutedEventArgs e)
        {
            if (UninstallWrapper())
                MessageBox.Show("Aurora LightFX Wrapper uninstalled successfully.");
            else
                MessageBox.Show("Aurora LightFX Wrapper could not be uninstalled.\r\nGame is not installed.");
        }

        private void game_enabled_Checked(object? sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                profile_manager.Settings.IsEnabled = (this.game_enabled.IsChecked.HasValue) ? this.game_enabled.IsChecked.Value : false;
                profile_manager.SaveProfiles();
            }
        }

        private void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
        }

        private void UserControl_Unloaded(object? sender, RoutedEventArgs e)
        {
        }

        private bool InstallWrapper(string installpath = "")
        {
            if (String.IsNullOrWhiteSpace(installpath))
                installpath = Utils.SteamUtils.GetGamePath(257510);


            if (!String.IsNullOrWhiteSpace(installpath))
            {
                //86
                string path = System.IO.Path.Combine(installpath, "Bin", "LightFX.dll");

                if (!File.Exists(path))
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

                using (BinaryWriter lightfx_wrapper_86 = new BinaryWriter(new FileStream(path, FileMode.Create)))
                {
                    lightfx_wrapper_86.Write(Properties.Resources.Aurora_LightFXWrapper86);
                }

                //64
                string path64 = System.IO.Path.Combine(installpath, "Bin", "x64", "LightFX.dll");

                if (!File.Exists(path64))
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path64));

                using (BinaryWriter lightfx_wrapper_64 = new BinaryWriter(new FileStream(path64, FileMode.Create)))
                {
                    lightfx_wrapper_64.Write(Properties.Resources.Aurora_LightFXWrapper64);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private bool UninstallWrapper()
        {
            String installpath = Utils.SteamUtils.GetGamePath(257510);
            if (!String.IsNullOrWhiteSpace(installpath))
            {
                //86
                string path = System.IO.Path.Combine(installpath, "Bin", "LightFX.dll");

                if (File.Exists(path))
                    File.Delete(path);

                //64
                string path64 = System.IO.Path.Combine(installpath, "Bin", "x64", "LightFX.dll");

                if (File.Exists(path64))
                    File.Delete(path64);

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
