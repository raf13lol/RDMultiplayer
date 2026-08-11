using System.Data;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer;

public class SteamExtraIntegration : Global
{
	public static bool AutoJoinCheckDone = false;
	public static bool AutoJoinSafety = false;
	public static bool PreventLeaveLobbyOnSceneSwap = false;

	[HarmonyPatch(typeof(SteamIntegration), nameof(SteamIntegration.Setup))]
	private class SetupPatch
    {
        public static void Postfix()
        {
            if (!SteamIntegration.initialized)
				return;

			SteamCallbacks.Initialise();
			Lobby.RegisterCallbacks();
		}
    }
	
	[HarmonyPatch(typeof(scnMenu), "Awake")]
	private class AutoJoinPatch
	{
		public static void Postfix()
		{
			if (AutoJoinCheckDone)
				return;
			Lobby.CheckForCommandLineJoin();
			AutoJoinCheckDone = true;
		}
	}

	[HarmonyPatch(typeof(scnBase), "Update")]
    private class UpdatePatch
    {
        public static void Postfix()
        {
			if (!SteamIntegration.initialized)
				return;

			Lobby.UpdateNetworking();
			SteamIntegration.CheckCallbacks();
        }
    } 

	[HarmonyPatch(typeof(SteamWorkshop), nameof(SteamWorkshop.OnToggleGameOverlay))]
	private class PreventOverlayErrorPatch
    {
        public static bool Prefix()
			=> scnCLS.instance != null;
    }

	[HarmonyPatch(typeof(scnBase), nameof(scnBase.GoToScene))]
	private class SceneChangePatch
    {
        public static void Postfix(string name)
		{
			if (AutoJoinSafety)
			{
				if (name == "scnGame")
					AutoJoinSafety = false;
				else
					return;
			}
			if (name == "scnGame" && PreventLeaveLobbyOnSceneSwap)
            {
				PreventLeaveLobbyOnSceneSwap = false;
                return;
            }
			if (name != "scnGame" && name != "scnCLS")
                CustomLevelSelectChanges.LastPlayedCLSLevel = null;
			Lobby.LeaveLobby();
		}
	}
}