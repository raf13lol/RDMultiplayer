using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using RDLevelEditor;
using Steamworks;

namespace Multiplayer;

public class ClientPackets : Global
{
	public static void CheckPacket(Packet packet)
	{
		switch (packet.Type)
		{
			case PacketType.Handshake:
				AcceptHandshake(packet);
				break;
		}
	}

	static void AcceptHandshake(Packet packet)
    {
		RandomSeeding.Seed = packet.Seed;
        MultiplayerState.OtherReady = false;
		MultiplayerState.CanPauseInVersus = packet.CanPauseInVersus;
		MultiplayerState.SharePause = packet.SharePause;
		MultiplayerState.Versus = !packet.TwoPlayer;

		bool hasLevel;
		Level storyModeLevel = Level.None;

		string customLevelPath = "";
		string customLevelFolder = Path.Combine(LevelValidation.CustomLevelsPath, packet.Level);
		bool isLegacyLevel = false;
		RDLevelData levelData = new();

		if (!packet.IsStoryMode)
		{
			if (!Directory.Exists(customLevelFolder))
				customLevelFolder += ".rdzip"; // ???????????????????????????????
			
			if (!Directory.Exists(customLevelFolder))
			{
				customLevelFolder = string.Empty;

				// Check on steam
				if (ulong.TryParse(packet.Level, out ulong ugcID)
				&& SteamUGC.GetItemInstallInfo(new(ugcID), out ulong _, out string folder, 1000u, out uint _)
				&& !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
					customLevelFolder = folder;
			}

			customLevelPath = string.IsNullOrEmpty(customLevelFolder) ? string.Empty : RDPackageInstaller.FindRDLevel(customLevelFolder);
			hasLevel = !string.IsNullOrEmpty(customLevelPath) && RDFile.Exists(customLevelPath);

			if (hasLevel)
			{
				isLegacyLevel = Path.GetFileNameWithoutExtension(customLevelPath) != Path.GetFileNameWithoutExtension("main.rdlevel");

				Dictionary<string, object> rdlevel = Json.Deserialize(RDFile.ReadAllText(customLevelPath)) as Dictionary<string, object>;
				levelData = new(rdlevel, true, true);

				if (packet.TwoPlayer && !isLegacyLevel && !string.IsNullOrEmpty(levelData.settings.separate2PLevelFilename))
					customLevelPath = Path.Combine(customLevelFolder, levelData.settings.separate2PLevelFilename);
			}
		}
		else if (hasLevel = Enum.TryParse(packet.Level.Trim(), out Level level))
			storyModeLevel = level;

		if (!hasLevel)
		{
			Log.LogMessage("Player doesn't have requested level.");
			Lobby.LeaveLobby();
			return;
		}
		Lobby.SendPacket(new(PacketType.Acknowledge));
		
		SteamExtraIntegration.PreventLeaveLobbyOnSceneSwap = true;
		if (packet.IsStoryMode)
        {
            Persistence.SetLastPlayedLevel(storyModeLevel);
			scnBase.currentLevelSelect = null;
        }
		else
		{
			CustomLevelSelectChanges.LastPlayedCLSLevel = customLevelFolder;
			scnBase.currentLevelSelect = "scnCLS";
			string hash = RDUtils.GetHash(new DirectoryInfo(Path.GetDirectoryName(customLevelPath)).Name);
			if (!isLegacyLevel)
				hash = RDUtils.GetHash(levelData.settings.author, levelData.settings.artist, levelData.settings.song);
			
			Rank customLevelRank = Persistence.GetCustomLevelRank(hash);
			if (customLevelRank == Rank.NeverSelected || customLevelRank == Rank.NotAvailable)
				Persistence.SetCustomLevelRank(hash, Rank.NotFinished);
			MultiplayerState.Hash = hash;
			scnCLS.CachedData.levelFileData = null;
		}

		if (packet.IsStoryMode)
			scnBase.GoToLevelWithEnum(storyModeLevel);
		else
			scnBase.GoToLevelWithExternalPath(customLevelPath);
			
		GC.twoPlayerMode = packet.TwoPlayer;
		scnGame.levelSpeed = packet.LevelSpeed;
		scnGame.attemptToLoadTutorial = false;
		scnGame.loadDogMode = packet.DogMode;
    }

	async static Task CheckIfOnRDCafe(string level)
    {
        HttpClient client = new();
		HttpResponseMessage response = await client.GetAsync("https://codex.rhythm.cafe/" + level);
		if (response.StatusCode == HttpStatusCode.NotFound)
			return;
		CustomLevelSelectChanges.ToDownload = "https://codex.rhythm.cafe/" + level;
		scnBase.GoToCustomLevelSelect();
    }
}

