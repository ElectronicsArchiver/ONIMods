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
using PeterHan.PLib.Core;
using System.Collections.Generic;
using System.Reflection;

using RMI = ReachabilityMonitor.Instance;
using ReachabilityMonitorState = GameStateMachine<ReachabilityMonitor, ReachabilityMonitor.
	Instance, Workable, object>.State;
using TranspiledMethod = System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>;

namespace PeterHan.FastTrack.SensorPatches {
	/// <summary>
	/// A faster version of ReachabilityMonitor that only updates if changes to pathing occur
	/// in its general area.
	/// </summary>
	[SkipSaveFileSerialization]
	public sealed class FastReachabilityMonitor : KMonoBehaviour, ISim4000ms {
		/// <summary>
		/// The name of the layer used for the reachability scene partitioner.
		/// </summary>
		public const string REACHABILITY = nameof(FastReachabilityMonitor);

		/// <summary>
		/// Whether the cell change handler needs to be cleaned up.
		/// </summary>
		private bool cleanupCellChange;

		/// <summary>
		/// The last set of extents from which this item was reachable.
		/// </summary>
		private Extents lastExtents;

		/// <summary>
		/// Registers a scene partitioner entry for nav cells changing.
		/// </summary>
		private HandleVector<int>.Handle partitionerEntry;

		/// <summary>
		/// The existing reachability monitor used to check and trigger events.
		/// </summary>
		private RMI smi;

		internal FastReachabilityMonitor() {
			lastExtents = new Extents(int.MinValue, int.MinValue, 1, 1);
			partitionerEntry = HandleVector<int>.InvalidHandle;
		}

		public override void OnCleanUp() {
			if (partitionerEntry.IsValid())
				GameScenePartitioner.Instance.Free(ref partitionerEntry);
			if (cleanupCellChange)
				Unsubscribe((int)GameHashes.CellChanged);
			base.OnCleanUp();
		}

		/// <summary>
		/// Only updates reachability if nav grid cells update nearby.
		/// </summary>
		private void OnReachableChanged(object _) {
			FastGroupProber.Instance?.Enqueue(smi);
		}

		/// <summary>
		/// Updates the reachability immediately if the object moves.
		/// </summary>
		private void OnMoved(object _) {
			UpdateOffsets();
		}

		public override void OnSpawn() {
			var go = gameObject;
			base.OnSpawn();
			smi = go.GetSMI<RMI>();
			UpdateOffsets();
			cleanupCellChange = go.TryGetComponent(out KBatchedAnimController controller) &&
				controller.isMovable;
			if (cleanupCellChange)
				Subscribe((int)GameHashes.CellChanged, OnMoved);
			FastGroupProber.Instance.Enqueue(smi);
		}

		public void Sim4000ms(float _) {
			UpdateOffsets();
		}

		/// <summary>
		/// Updates the scene partitioner entry if the offsets change.
		/// </summary>
		public void UpdateOffsets() {
			var inst = FastGroupProber.Instance;
			if (inst != null && smi != null) {
				var offsets = smi.master.GetOffsets();
				var extents = new Extents(Grid.PosToCell(transform.position), offsets);
				// Only if the extents actually changed
				if (extents.x != lastExtents.x || extents.y != lastExtents.y || extents.
						width != lastExtents.width || extents.height != lastExtents.height) {
					var gsp = GameScenePartitioner.Instance;
					if (partitionerEntry.IsValid())
						gsp.UpdatePosition(partitionerEntry, extents);
					else
						partitionerEntry = gsp.Add("FastReachabilityMonitor.UpdateOffsets",
							this, extents, inst.mask, OnReachableChanged);
					// The offsets changed, fire a manual check if not the first time
					// (the smi constructor already updated it once)
					if (lastExtents.x >= 0)
						smi.UpdateReachability();
					lastExtents = extents;
				}
			} else
				PUtil.LogWarning("FastReachabilityMonitor scene partitioner is unavailable");
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to use our check for reachability instead of its own.
	/// </summary>
	[HarmonyPatch]
	public static class MinionGroupProber_IsAllReachable_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Targets two methods that are essentially identical to save a patch.
		/// </summary>
		internal static IEnumerable<MethodBase> TargetMethods() {
			yield return typeof(MinionGroupProber).GetMethodSafe(nameof(MinionGroupProber.
				IsAllReachable), false, typeof(int), typeof(CellOffset[]));
			yield return typeof(MinionGroupProber).GetMethodSafe(nameof(MinionGroupProber.
				IsReachable), false, typeof(int), typeof(CellOffset[]));
		}

		/// <summary>
		/// Applied before IsAllReachable runs.
		/// </summary>
		internal static bool Prefix(int cell, CellOffset[] offsets, ref bool __result) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				__result = Grid.IsValidCell(cell) && inst.IsReachable(cell, offsets);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to use our check for reachability instead of its own.
	/// </summary>
	[HarmonyPatch(typeof(MinionGroupProber), nameof(MinionGroupProber.IsReachable),
		typeof(int))]
	public static class MinionGroupProber_IsCellReachable_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Applied before IsReachable runs.
		/// </summary>
		internal static bool Prefix(int cell, ref bool __result) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				__result = Grid.IsValidCell(cell) && inst.IsReachable(cell);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to use our check for reachability instead of its own.
	/// </summary>
	[HarmonyPatch(typeof(MinionGroupProber), nameof(MinionGroupProber.IsReachable_AssumeLock))]
	public static class MinionGroupProber_IsReachableAssumeLock_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Applied before IsReachable_AssumeLock runs.
		/// </summary>
		internal static bool Prefix(int cell, ref bool __result) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				__result = Grid.IsValidCell(cell) && inst.IsReachable(cell);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to mark cells as dirty when their prober status changes.
	/// </summary>
	[HarmonyPatch(typeof(MinionGroupProber), nameof(MinionGroupProber.Occupy))]
	public static class MinionGroupProber_Occupy_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Applied before Occupy runs.
		/// </summary>
		internal static bool Prefix(object prober, IEnumerable<int> cells) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				inst.Occupy(prober, cells, false);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to deallocate references to destroyed path probers.
	/// </summary>
	[HarmonyPatch(typeof(MinionGroupProber), nameof(MinionGroupProber.ReleaseProber))]
	public static class MinionGroupProber_ReleaseProber_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Applied before ReleaseProber runs.
		/// </summary>
		internal static bool Prefix(object prober) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				inst.Remove(prober);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to MinionGroupProber to create an entry when a prober starts.
	/// </summary>
	[HarmonyPatch(typeof(MinionGroupProber), nameof(MinionGroupProber.SetValidSerialNos))]
	public static class MinionGroupProber_SetValidSerialNos_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Applied before SetValidSerialNos runs.
		/// </summary>
		internal static bool Prefix(object prober) {
			var inst = FastGroupProber.Instance;
			if (inst != null)
				inst.Allocate(prober);
			return inst == null;
		}
	}

	/// <summary>
	/// Applied to ReachabilityMonitor to turn off periodic checks for reachability.
	/// </summary>
	[HarmonyPatch(typeof(ReachabilityMonitor), nameof(ReachabilityMonitor.InitializeStates))]
	public static class ReachabilityMonitor_InitializeStates_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Transpiles InitializeStates to remove the FastUpdate updater.
		/// </summary>
		internal static bool Prefix(ref StateMachine.BaseState default_state,
				ReachabilityMonitor __instance) {
			// Existing states
			ReachabilityMonitorState reachable = __instance.reachable, unreachable =
				__instance.unreachable;
			var param = __instance.isReachable;
			// New state: avoid an extra event on startup by only transitioning after the
			// first successful reachability update
			var tbd = __instance.CreateState("tbd");
			default_state = tbd;
			__instance.serializable = StateMachine.SerializeType.Never;
			tbd.ParamTransition(param, unreachable, ReachabilityMonitor.IsFalse).
				ParamTransition(param, reachable, ReachabilityMonitor.IsTrue);
			reachable.ToggleTag(GameTags.Reachable).
				Enter("TriggerEvent", TriggerEvent).
				ParamTransition(param, unreachable, ReachabilityMonitor.IsFalse);
			unreachable.Enter("TriggerEvent", TriggerEvent).
				ParamTransition(param, reachable, ReachabilityMonitor.IsTrue);
			return false;
		}

		/// <summary>
		/// Triggers an event when reachability changes.
		/// </summary>
		/// <param name="smi">The reachability monitor whose state changed.</param>
		private static void TriggerEvent(RMI smi) {
			smi.TriggerEvent();
		}
	}

	/// <summary>
	/// Applied to ReachabilityMonitor.Instance to add a fast reachability monitor on each
	/// object that needs reachability checks.
	/// </summary>
	[HarmonyPatch(typeof(RMI), MethodType.Constructor, typeof(Workable))]
	public static class ReachabilityMonitor_Instance_Constructor_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.FastReachability;

		/// <summary>
		/// Adds a fast reachability monitor to the target workable, but does not immediately
		/// check for reachability, instead queuing it for a check on the group prober.
		/// </summary>
		/// <param name="smi">The existing reachability monitor state.</param>
		private static void UpdateReachability(RMI smi) {
			var workable = smi.master;
			if (workable != null)
				workable.gameObject.AddOrGet<FastReachabilityMonitor>();
		}

		/// <summary>
		/// Transpiles the constructor to enqueue the item onto the reachability queue instead
		/// of checking immediately (where, on load, the result would be invalid).
		/// </summary>
		internal static TranspiledMethod Transpiler(TranspiledMethod instructions) {
			return PPatchTools.ReplaceMethodCallSafe(instructions, typeof(RMI).GetMethodSafe(
				nameof(RMI.UpdateReachability), false),
				typeof(ReachabilityMonitor_Instance_Constructor_Patch).GetMethodSafe(nameof(
				UpdateReachability), true, typeof(RMI)));
		}
	}
}
