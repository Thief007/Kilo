﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyboardLayoutMonitor
{
	public partial class MainForm : Form
	{
		private static Process hookerProcess32;
		private static Process hookerProcess64;
		private bool realClose;
		private Settings settings = new Settings();
		private const string SettingsFileName = "KlmSettings.dat";
		private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

		#region Initialization

		public MainForm()
		{
			LoadOrCreateDefaultSettings();

			if(Environment.OSVersion.Version.Major < 6)
			{
				const string errorMessage = "Операцинные системы без Windows Aero не поддерживаются";
				MessageBox.Show(errorMessage, "Ошибка при запуске", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Environment.FailFast(errorMessage);
				return;
			}

			try
			{
				StartLayoutMonitors();
			}
			catch(Exception ex)
			{
				string message = ex.ToString();
				MessageBox.Show(message);
				Environment.FailFast(message);
				throw;
			}

			Application.ApplicationExit += ApplicationOnApplicationExit;

			InitializeComponent();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			Hide();
		}

		#endregion

		private static void ApplicationOnApplicationExit(object sender, EventArgs args)
		{
			if(hookerProcess32 != null && !hookerProcess32.HasExited)
				hookerProcess32.Kill();
			if(hookerProcess64 != null && !hookerProcess64.HasExited)
				hookerProcess64.Kill();
		}

		public static bool StartLayoutMonitors()
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
											{
												FileName = @"x86\HookerWatcher.exe",
												CreateNoWindow = true,
												UseShellExecute = false
											};
			hookerProcess32 = Process.Start(startInfo);

			startInfo = new ProcessStartInfo
			{
				FileName = @"x64\HookerWatcher.exe",
				CreateNoWindow = true,
				UseShellExecute = false
			};

			bool result = true;

			try
			{
				hookerProcess64 = Process.Start(startInfo);
			}
			catch
			{
				result = false;
			}

			return result;
		}

		#region WndProc

		protected override void WndProc(ref Message msg)
		{
			switch(msg.Msg)
			{
				case KeyboardLayoutSwitchHooker.WM_LANGUAGE_CHANGED:
					if(!Visible)
						ColorSettingsController.SetColor(msg.LParam, settings);
					break;
			}
			base.WndProc(ref msg);
		}

		#endregion

		private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			OpenForm();
		}

		private void OpenForm()
		{
			Show();
			BringToFront();
			WindowState = FormWindowState.Normal;
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if(realClose) return;
			e.Cancel = true;
			Hide();
		}

		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			realClose = true;
			Close();
		}

		private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			OpenForm();
		}

		private void MainForm_Shown(object sender, EventArgs e)
		{
			comboBoxLanguages.Items.Clear();
			foreach(InputLanguage language in InputLanguage.InstalledInputLanguages)
			{
				comboBoxLanguages.Items.Add(language.LayoutName);
				if(settings.DefaultLayoutName == language.Culture.ThreeLetterWindowsLanguageName)
					comboBoxLanguages.SelectedItem = language.LayoutName;
			}
			GetAutoRunSettings();
		}

		private void comboBoxLanguages_SelectedIndexChanged(object sender, EventArgs e)
		{
			foreach(InputLanguage language in InputLanguage.InstalledInputLanguages)
			{
				if(language.LayoutName == comboBoxLanguages.SelectedText)
					settings.DefaultLayoutName = language.Culture.ThreeLetterWindowsLanguageName;
			}
		}

		private void buttonPickDefaultLayoutColor_Click(object sender, EventArgs e)
		{
			try
			{
				DwmApi.WDM_COLORIZATION_PARAMS colorizationParams;
				DwmApi.DwmGetColorizationParameters(out colorizationParams);
				settings.DefaultLayoutColorScheme = colorizationParams;
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.ToString(), "Не удалось получить текущую цветовую схему", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void buttonPickAlternativeLayoutColor_Click(object sender, EventArgs e)
		{
			try
			{
				DwmApi.WDM_COLORIZATION_PARAMS colorizationParams;
				DwmApi.DwmGetColorizationParameters(out colorizationParams);
				settings.AlternativeLayoutColorScheme = colorizationParams;
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.ToString(), "Не удалось получить текущую цветовую схему", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void buttonSave_Click(object sender, EventArgs e)
		{
			SaveCurrentSettings();
			SetAutoRun();
		}

		private void buttonCancel_Click(object sender, EventArgs e)
		{
			Hide();
		}

		private void GetAutoRunSettings()
		{
			try
			{
				RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
				if(key == null)
					throw new Exception("Не найден ключ реестра " + RegistryKey);
				try
				{
					string location = Assembly.GetExecutingAssembly().Location;
					if(location == null)
						throw new NullReferenceException("Assembly.Location is null");
					checkBoxAutoRun.Checked = location == (string)key.GetValue("JetFly", null);
				}
				finally
				{
					key.Close();
				}
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.ToString(), "Не удалось проверить настройку автозапуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
				throw;
			}
		}

		private void SetAutoRun()
		{
			try
			{
				RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
				if(key == null)
					throw new Exception("Не найден ключ реестра " + RegistryKey);
				try
				{
					string location = Assembly.GetExecutingAssembly().Location;
					if(location == null)
						throw new NullReferenceException("Assembly.Location is null");
					if (checkBoxAutoRun.Checked)
						key.SetValue("JetFly", location, RegistryValueKind.String);
					else
						key.DeleteValue("JetFly", false);
				}
				finally
				{
					key.Close();
				}
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.ToString(), "Не удалось изменить настройку автозапуска", MessageBoxButtons.OK,
								MessageBoxIcon.Error);
			}
		}

		#region Settings management

		private void SaveCurrentSettings()
		{
			string settingsFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsFileName);
			try
			{
				File.WriteAllBytes(settingsFileName, Settings.Serialize(settings));
				Hide();
			}
			catch(Exception ex)
			{
				MessageBox.Show(ex.ToString(), "Не удалось сохранить настройки", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void LoadOrCreateDefaultSettings()
		{
			string settingsFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsFileName);
			Settings newSettings;
			try
			{
				byte[] binary = File.ReadAllBytes(settingsFileName);
				newSettings = Settings.Deserialize(binary);
			}
			catch(Exception)
			{
				newSettings = Settings.CreateDefaultSettings();
				try
				{
					File.WriteAllBytes(settingsFileName, Settings.Serialize(newSettings));
				}
				catch(Exception ex)
				{
					MessageBox.Show(ex.ToString(), "Не удалось сохранить настройки по-умолчанию", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			settings = newSettings;
		}

		#endregion
	}
}