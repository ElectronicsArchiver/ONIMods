﻿/*
 * Copyright 2022 Peter Han
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

using HarmonyLib;
using System;
using System.Runtime.CompilerServices;

namespace PeterHan.FastTrack.SensorPatches {
	/// <summary>
	/// Applied to MingleChore.StatesInstance to force update the target cell while the
	/// chore is actually active.
	/// </summary>
	[HarmonyPatch(typeof(MingleChore.StatesInstance), nameof(MingleChore.StatesInstance.
		GetMingleCell))]
	public static class MingleChore_StatesInstance_GetMingleCell_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SensorOpts;

		/// <summary>
		/// Applied before GetMingleCell runs.
		/// </summary>
		internal static void Prefix(MingleCellSensor ___mingleCellSensor) {
			if (___mingleCellSensor != null)
				MingleCellSensorUpdater.Update(___mingleCellSensor);
		}
	}

	/// <summary>
	/// Applied to MingleCellSensor.Update to extract the original method body.
	/// </summary>
	[HarmonyPatch(typeof(MingleCellSensor), nameof(MingleCellSensor.Update))]
	internal static class MingleCellSensorUpdater {
		internal static bool Prepare() => FastTrackOptions.Instance.SensorOpts;

		/// <summary>
		/// Applied before Update runs.
		/// </summary>
		internal static bool Prefix() {
			return false;
		}

		[HarmonyReversePatch(HarmonyReversePatchType.Original)]
		[HarmonyPatch(nameof(MingleCellSensor.Update))]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void Update(MingleCellSensor _) {
			// Dummy code to ensure no inlining
			while (System.DateTime.Now.Ticks > 0L)
				throw new NotImplementedException("Reverse patch stub");
		}
	}
}