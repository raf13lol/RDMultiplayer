using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer;

public class RankscreenDisplay : Global
{
	public static bool MultiplayerRankscreen = false;
	public static bool WaitingForData = false;
	public static bool SentData = false;
	public static int IntendedRankSequence = 0;

	[HarmonyPatch(typeof(Rankscreen), nameof(Rankscreen.AdvanceGameover))]
	private class BlockAdvanceGameoverPatch
	{
		public static bool Prefix(Rankscreen __instance, ref float ___rankscreenShowReferenceTime, ref int ___trueGameover)
		{
			if (Lobby.InMultiplayer && !Lobby.IsHost)
				scnCLS.CachedData.levelFileData = null;
			if (IntendedRankSequence == 0 && !MultiplayerRankscreen)
				MultiplayerRankscreen = Lobby.InMultiplayer;

			if (!MultiplayerRankscreen)
				return true;
			__instance.game.currentLevel.skipRankText = false;

			if (!SentData)
            {
				MistakesManager mm = __instance.game.mistakesManager;
				bool isP1OrVersus = MultiplayerState.SelfPlayer == RDPlayer.P1 || InVersus;
				List<float> hitTimes = isP1OrVersus ? scnGame.p1HitTimes : scnGame.p2HitTimes;

                Lobby.SendPacket(new(PacketType.ShareRank)
				{
					Mistakes = isP1OrVersus ? mm.mistakesP1 : mm.mistakesP2,
					EarlyOffset = isP1OrVersus ? mm.earlyOffsetsSumP1 : mm.earlyOffsetsSumP2,
					LateOffset = isP1OrVersus ? mm.lateOffsetsSumP1 : mm.lateOffsetsSumP2,
					HitsInPlusZone = hitTimes.Count(h => h < 0.04f),
					HitsInNormalZone = hitTimes.Count(h => h >= 0.04f && h <= 0.08f),
					HitsInMinusZone = hitTimes.Count(h => h > 0.08f),
				});
				SentData = true;
            }

			if (MultiplayerState.GotOtherPlayerRankInformation)
			{
				if (WaitingForData)
				{
					if (IntendedRankSequence > 0)
						__instance.ShowHeaderRankText();
					if (IntendedRankSequence > 1)
						__instance.ShowAndSaveRank(false, false);
					if (IntendedRankSequence > 2)
						__instance.ShowRankDescription();

					___trueGameover = IntendedRankSequence;
					// IntendedRankSequence = 0;
					WaitingForData = false;
				}

				return true;
			}

			if (IntendedRankSequence++ == 0)
			{
				if (__instance.game.windowChoreographer is RealWindowChoreographer realWindowChoreographer)
				{
					realWindowChoreographer.Cancel();
					WindowChoreographer.playerWindow.ResetPosition(realWindowChoreographer);
				}
				___rankscreenShowReferenceTime = Time.unscaledTime;
			}

			WaitingForData = true;
			return false;
		}		
	}

	[HarmonyPatch(typeof(Rankscreen), nameof(Rankscreen.ShowAndSaveRank))]
	private class SaveRankPatch
	{
		public static void Postfix(Rankscreen __instance)
		{
			if (scnCLS.CachedData.levelFileData != null || scnGame.levelToLoadSource != LevelSource.ExternalPath)
				return;
			scnGame game = __instance.game;
			Rank rank = game.currentLevel.GetRankFromMistakes(); 
			
			Rank oldRank = Persistence.GetCustomLevelRank(MultiplayerState.Hash, scnGame.levelSpeed);
			if (Persistence.IsBetterRank(rank, oldRank))
				Persistence.SetCustomLevelRank(MultiplayerState.Hash, rank, scnGame.levelSpeed);

			if (game.currentLevel.useScore)
				Persistence.SetLevelScore(MultiplayerState.Hash, game.currentLevel.i1);
		}
	}

	public static int GetTrueGameover()
    {
        if (scnGame.instance == null)
			return 0;
		return (int)AccessTools.Field(typeof(Rankscreen), "trueGameover").GetValue(scnGame.instance.rankscreen);
    }

	public static bool GetExiting()
    {
        if (scnGame.instance == null)
			return false;
		return (bool)AccessTools.Field(typeof(Rankscreen), "exiting").GetValue(scnGame.instance.rankscreen);
    }
}