﻿/*
 * Copyright 2020 Peter Han
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using Harmony;
using KMod;
using PeterHan.PLib;
using PeterHan.PLib.UI;
using Steamworks;
using System;
using System.Text;
using UnityEngine;

namespace PeterHan.ModUpdateDate {
	/// <summary>
	/// Adds an update button to the mod menu.
	/// </summary>
	public sealed class ModUpdateHandler {
		/// <summary>
		/// The singleton instance of this class.
		/// </summary>
		internal static ModUpdateHandler Instance { get; }

		/// <summary>
		/// The margin inside the update button around the icon.
		/// </summary>
		private static readonly RectOffset BUTTON_MARGIN = new RectOffset(6, 6, 6, 6);

		/// <summary>
		/// The background color if outdated.
		/// </summary>
		private static readonly ColorStyleSetting COLOR_OUTDATED;

		/// <summary>
		/// The background color if up to date.
		/// </summary>
		private static readonly ColorStyleSetting COLOR_UPDATED;

		/// <summary>
		/// The checked or warning icon size on the version.
		/// </summary>
		private static readonly Vector2 ICON_SIZE = new Vector2(16.0f, 16.0f);

		/// <summary>
		/// The number of minutes allowed before a mod is considered out of date.
		/// </summary>
		internal const double UPDATE_JITTER = 10.0;

		static ModUpdateHandler() {
			COLOR_OUTDATED = ScriptableObject.CreateInstance<ColorStyleSetting>();
			COLOR_OUTDATED.inactiveColor = new Color(0.753f, 0.0f, 0.0f);
			COLOR_OUTDATED.activeColor = new Color(1.0f, 0.0f, 0.0f);
			COLOR_OUTDATED.hoverColor = new Color(1.0f, 0.0f, 0.0f);
			// Should be unreachable
			COLOR_OUTDATED.disabledColor = COLOR_OUTDATED.disabledActiveColor =
				COLOR_OUTDATED.disabledhoverColor = new Color(0.706f, 0.549f, 0.549f);
			COLOR_UPDATED = ScriptableObject.CreateInstance<ColorStyleSetting>();
			COLOR_UPDATED.inactiveColor = new Color(0.0f, 0.753f, 0.0f);
			COLOR_UPDATED.activeColor = new Color(0.0f, 1.0f, 0.0f);
			COLOR_UPDATED.hoverColor = new Color(0.0f, 1.0f, 0.0f);
			COLOR_UPDATED.disabledColor = COLOR_UPDATED.disabledActiveColor =
				COLOR_UPDATED.disabledhoverColor = new Color(0.549f, 0.706f, 0.549f);
			Instance = new ModUpdateHandler();
		}

		/// <summary>
		/// Adds the mod update date to the mods menu.
		/// </summary>
		/// <param name="modEntry">The entry in the mod menu.</param>
		internal static void AddModUpdateButton(Traverse modEntry) {
			int index = modEntry.GetField<int>("mod_index");
			var rowInstance = modEntry.GetField<RectTransform>("rect_transform")?.gameObject;
			var mods = Global.Instance.modManager?.mods;
			if (rowInstance != null && mods != null && index >= 0 && index < mods.Count) {
				var mod = mods[index];
				var tooltip = new StringBuilder(128);
				var localDate = mod.GetLocalLastModified();
				var updated = ModStatus.Disabled;
				// A nice colorful button with a warning or checkmark icon
				var addButton = new PButton("Version") {
					Margin = BUTTON_MARGIN, SpriteSize = ICON_SIZE,
					MaintainSpriteAspect = true
				};
				if (mod.label.distribution_platform == Label.DistributionPlatform.Steam) {
					var exec = new ModUpdateExecutor(mod);
					if (exec.LastSteamUpdate > System.DateTime.MinValue) {
						// Generate tooltip for mod's current date and last Steam update
						updated = AddSteamTooltip(tooltip, exec, localDate);
						addButton.OnClick = exec.TryUpdateMod;
					} else {
						// Steam update could not be determined
						tooltip.AppendFormat(ModUpdateDateStrings.LOCAL_UPDATE, localDate);
						tooltip.Append("\n");
						tooltip.AppendFormat(ModUpdateDateStrings.STEAM_UPDATE_UNKNOWN);
					}
				} else
					tooltip.AppendFormat(ModUpdateDateStrings.LOCAL_UPDATE, localDate);
				// Icon, color, and tooltip
				addButton.Sprite = (updated == ModStatus.UpToDate || updated == ModStatus.
					Disabled) ? PUITuning.Images.Checked : PUITuning.Images.GetSpriteByName(
					"iconWarning");
				addButton.Color = (updated == ModStatus.Outdated) ? COLOR_OUTDATED :
					COLOR_UPDATED;
				addButton.ToolTip = tooltip.ToString();
				// Just before subscription button, and after the Options button
				PButton.SetButtonEnabled(addButton.AddTo(rowInstance, 3), updated != ModStatus.
					Disabled);
			}
		}

		/// <summary>
		/// Adds a tooltip to a Steam mod showing its update status.
		/// </summary>
		/// <param name="tooltip">The tooltip under construction.</param>
		/// <param name="exec">The mod update executor which can update this mod.</param>
		/// <param name="localDate">The local last update date.</param>
		/// <returns>The status of the Steam mod.</returns>
		private static ModStatus AddSteamTooltip(StringBuilder tooltip, ModUpdateExecutor exec,
				System.DateTime localDate) {
			var ours = ModUpdateInfo.FindModInConfig(exec.SteamID.m_PublishedFileId);
			var ourDate = System.DateTime.MinValue;
			var globalDate = exec.LastSteamUpdate;
			ModStatus updated;
			// Do we have a better estimate?
			if (ours != null)
				ourDate = new System.DateTime(ours.LastUpdated, DateTimeKind.Utc);
			// Allow some time for download delays etc
			if (localDate.AddMinutes(UPDATE_JITTER) >= globalDate) {
				tooltip.Append(ModUpdateDateStrings.MOD_UPDATED);
				updated = ModStatus.UpToDate;
			} else if (ourDate.AddMinutes(UPDATE_JITTER) >= globalDate) {
				tooltip.Append(ModUpdateDateStrings.MOD_UPDATED_BYUS);
				localDate = ourDate;
				updated = ModStatus.UpToDateLocal;
			} else {
				tooltip.Append(ModUpdateDateStrings.MOD_OUTDATED);
				updated = ModStatus.Outdated;
			}
			// AppendLine appends platform specific separator
			tooltip.Append("\n");
			tooltip.AppendFormat(ModUpdateDateStrings.LOCAL_UPDATE, localDate);
			tooltip.Append("\n");
			tooltip.AppendFormat(ModUpdateDateStrings.STEAM_UPDATE, globalDate);
			return updated;
		}

		/// <summary>
		/// True if an update is already in progress.
		/// </summary>
		public bool IsUpdating {
			get {
				return mod != null;
			}
		}

		/// <summary>
		/// The CallResult for handling the Steam API call to download mod data.
		/// </summary>
		private readonly CallResult<RemoteStorageDownloadUGCResult_t> caller;

		/// <summary>
		/// The path that is being downloaded.
		/// </summary>
		private string downloadPath;

		/// <summary>
		/// The mod information that is being updated.
		/// </summary>
		private Mod mod;
		
		/// <summary>
		/// The adjusted last update date of the mod.
		/// </summary>
		private System.DateTime updateTime;

		private ModUpdateHandler() {
			caller = new CallResult<RemoteStorageDownloadUGCResult_t>(OnDownloadComplete);
			downloadPath = "";
			mod = null;
			updateTime = System.DateTime.MinValue;
		}

		/// <summary>
		/// Attempts to back up the mod configs into the downloaded zip file.
		/// </summary>
		/// <param name="id">The Steam mod ID.</param>
		/// <param name="copied">The number of configuration files saved.</param>
		/// <returns>true if backup was OK, or false if it failed.</returns>
		private bool BackupConfigs(ulong id, out int copied) {
			// Attempt config backup
			var tempPath = ExtensionMethods.GetDownloadPath(id, true);
			var backup = new ConfigBackupUtility(mod, downloadPath, tempPath);
			bool success;
			copied = 0;
			try {
				success = backup.CreateMergedPackage(out copied);
				if (success)
					backup.CommitUpdate();
				else
					backup.RollbackUpdate();
			} catch {
				backup.RollbackUpdate();
				throw;
			}
			return success;
		}

		/// <summary>
		/// Called when a download completes.
		/// </summary>
		/// <param name="result">The downloaded mod information.</param>
		/// <param name="failed">Whether an I/O error occurred during download.</param>
		private void OnDownloadComplete(RemoteStorageDownloadUGCResult_t result, bool failed) {
			var status = result.m_eResult;
			if (mod != null) {
				string title = mod.label.title;
				ulong id = mod.GetSteamModID().m_PublishedFileId;
				if (failed || (status != EResult.k_EResultAdministratorOK && status != EResult.
						k_EResultOK)) {
					// Failed to update
					PUIElements.ShowMessageDialog(null, string.Format(ModUpdateDateStrings.
						UPDATE_ERROR, title, status));
					// Clean the trash
					ExtensionMethods.RemoveOldDownload(id);
				} else if (!string.IsNullOrEmpty(downloadPath)) {
					string text;
					// Try to salvage the configs
					if (BackupConfigs(id, out int copied))
						text = string.Format(ModUpdateDateStrings.UPDATE_OK, title, copied,
							(copied == 1) ? "" : "s");
					else
						text = string.Format(ModUpdateDateStrings.UPDATE_OK_NOBACKUP, title);
					// Mod has been updated
					mod.status = Mod.Status.ReinstallPending;
					mod.reinstall_path = downloadPath;
					Global.Instance.modManager?.Save();
					// Update the config
					if (updateTime > System.DateTime.MinValue)
						ModUpdateDetails.UpdateConfigFor(id, updateTime);
					// Tell the user
					PUIElements.ShowConfirmDialog(null, text, App.instance.Restart, null,
						STRINGS.UI.FRONTEND.MOD_DIALOGS.RESTART.OK,
						STRINGS.UI.FRONTEND.MOD_DIALOGS.RESTART.CANCEL);
				}
				mod = null;
				downloadPath = "";
				updateTime = System.DateTime.MinValue;
			}
		}

		/// <summary>
		/// Attempts to start a mod force update.
		/// </summary>
		/// <param name="mod">The mod to update.</param>
		/// <param name="details">The mod details to force update.</param>
		/// <param name="globalDate">The update date as reported by Steam.</param>
		internal void StartModUpdate(Mod mod, SteamUGCDetails_t details,
				System.DateTime globalDate) {
			if (mod == null)
				throw new ArgumentNullException("mod");
			var content = details.m_hFile;
			string error = null;
			if (IsUpdating)
				error = ModUpdateDateStrings.UPDATE_INPROGRESS;
			else if (content.Equals(UGCHandle_t.Invalid))
				error = ModUpdateDateStrings.UPDATE_NOFILE;
			else {
				ulong id = details.m_nPublishedFileId.m_PublishedFileId;
				downloadPath = ExtensionMethods.GetDownloadPath(id);
				ExtensionMethods.RemoveOldDownload(id);
				// The game should already raise an error if insufficient space / access
				// errors on the saves and mods folder
				var res = SteamRemoteStorage.UGCDownloadToLocation(content, downloadPath, 0U);
				if (res.Equals(SteamAPICall_t.Invalid))
					error = ModUpdateDateStrings.UPDATE_CANTSTART;
				else {
					caller.Set(res);
					PUtil.LogDebug("Start download of file {0:D} to {1}".F(content.m_UGCHandle,
						downloadPath));
					updateTime = globalDate;
					this.mod = mod;
				}
			}
			if (error != null)
				PUIElements.ShowMessageDialog(null, error);
		}

		/// <summary>
		/// Potential statuses in the mods menu.
		/// </summary>
		private enum ModStatus {
			Disabled, UpToDate, UpToDateLocal, Outdated
		}
	}
}