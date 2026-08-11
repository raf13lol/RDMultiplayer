using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace Multiplayer;

public class Lobby : Global
{
	public const int NetworkingChannelID = 0;

	public static CSteamID CurrentLobbySteamID = new(0u);
	public static bool InLobby { get => CurrentLobbySteamID.m_SteamID != 0u; }
	public static bool InMultiplayer { get => InLobby && HasOtherUser && OtherUserReady; }

	public static bool IsHost = true;
	public static bool HasOtherUser = false;
	public static bool OtherUserReady = false;
	public static SteamNetworkingIdentity OtherUser;
	public static IntPtr[] MessagePointers = new IntPtr[2];

	// Callbacks
	public static CallResult<LobbyCreated_t> OnLobbyCreatedResult;
	public static Callback<LobbyEnter_t> OnLobbyJoinedCallback;
	public static Callback<LobbyChatUpdate_t> OnLobbyUpdateCallback;

	public static Callback<SteamNetworkingMessagesSessionRequest_t> OnSessionRequestCallback;
	public static Callback<GameLobbyJoinRequested_t> OnLobbyJoinRequestCallback;

	public static Action<Packet> OnPacketReceived;
	public static Action OnEnterLobby;
	public static Action OnOtherEnterLobby;
	public static Action OnOtherLeaveLobby;


	public static void CheckForCommandLineJoin()
	{
		if (!SteamIntegration.initialized)
			return;
			
		string[] args = Environment.GetCommandLineArgs();
		int index = args.IndexOf("+connect_lobby");
		if (index > -1 && ++index != args.Length)
		{
			SteamExtraIntegration.AutoJoinSafety = true;
			JoinLobby(ulong.Parse(args[index]));
			return;
		}
	}

	public static void CreateLobby(bool privateLobby = false)
	{
		if (!SteamIntegration.initialized)
			return;
		if (InLobby)
			return;

		// Steam doesn't let me invite people on this type of lobby? what the fuck?
		SteamAPICall_t handle = SteamMatchmaking.CreateLobby(privateLobby ? ELobbyType.k_ELobbyTypePrivate : ELobbyType.k_ELobbyTypeFriendsOnly, 2);
		OnLobbyCreatedResult.Set(handle);
	}

	public static void JoinLobby(ulong lobbyID)
	{
		if (!SteamIntegration.initialized)
			return;
		if (InLobby)
			return;

		SteamMatchmaking.JoinLobby(new(lobbyID));
	}

	public static void LeaveLobby()
	{
		if (!InLobby)
			return;

		SteamMatchmaking.LeaveLobby(CurrentLobbySteamID);
		CurrentLobbySteamID.m_SteamID = 0u;
		HasOtherUser = OtherUserReady = false;
	}


	public static void UpdateNetworking()
	{
		foreach (Packet packet in ReadData())
			OnPacketReceived?.Invoke(packet);
	}

	public static Packet[] ReadData()
	{
		if (!HasOtherUser)
			return [];

		int messageCount = SteamNetworkingMessages.ReceiveMessagesOnChannel(NetworkingChannelID, MessagePointers, MessagePointers.Length);
		if (messageCount == 0)
			return [];

		Packet[] packets = new Packet[messageCount];
		int index = 0;
		while (index < messageCount)
		{
			IntPtr messagePointer = MessagePointers[index];
			SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(messagePointer);

			byte[] buffer = new byte[message.m_cbSize];
			Marshal.Copy(message.m_pData, buffer, 0, message.m_cbSize);

			Packet packet = Packet.Decode(buffer);
			packets[index++] = packet;
			if (packet.Type == PacketType.Acknowledge)
				OtherUserReady = true;

			SteamNetworkingMessage_t.Release(messagePointer);
		}
		return packets;
	}

	public static void SendPacket(Packet packet, MessageFlags messageFlags = MessageFlags.Reliable)
	{
		if (!HasOtherUser)
			return;

		byte[] buffer = packet.Encode();
		unsafe
		{
			fixed (byte* pointer = buffer)
				SteamNetworkingMessages.SendMessageToUser(ref OtherUser, (IntPtr)pointer, (uint)buffer.Length, (int)messageFlags, NetworkingChannelID);
		}
	}


	public static void UpdateLobbyInformation()
	{
		if (!InLobby)
			return;

		HasOtherUser = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbySteamID) > 1;
		if (HasOtherUser)
		{
			CSteamID playerSteamID = SteamUser.GetSteamID();
			IsHost = SteamMatchmaking.GetLobbyOwner(CurrentLobbySteamID) == playerSteamID;

			OtherUser = new();
			int otherUserIndex = 1;
			if (SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbySteamID, 1) == playerSteamID)
				otherUserIndex = 0;

			OtherUser.SetSteamID(SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbySteamID, otherUserIndex));
		}
		else
			IsHost = true;

		if (!IsHost)
			OtherUserReady = true;
	}


	public static void RegisterCallbacks()
	{
		if (!SteamIntegration.initialized)
			return;
		OnLobbyCreatedResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
		OnLobbyJoinedCallback = Callback<LobbyEnter_t>.Create(OnLobbyJoined);
		OnLobbyUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyUpdate);

		OnSessionRequestCallback = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
		OnLobbyJoinRequestCallback = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequest);
	}

	public static void OnLobbyCreated(LobbyCreated_t result, bool error)
	{
		if (error)
		{
			Log.LogError("Creating a lobby failed: " + result.m_eResult);
			return;
		}

		CurrentLobbySteamID = new(result.m_ulSteamIDLobby);
		IsHost = true;
		HasOtherUser = OtherUserReady = false;
		SteamMatchmaking.SetLobbyJoinable(CurrentLobbySteamID, true);
	}

	public static void OnLobbyJoined(LobbyEnter_t message)
	{
		CurrentLobbySteamID = new(message.m_ulSteamIDLobby);
		UpdateLobbyInformation();
		
		OnEnterLobby?.Invoke();
	}

	public static void OnLobbyUpdate(LobbyChatUpdate_t message)
	{
		if ((message.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) > 0x00)
		{
			HasOtherUser = true;
			OtherUserReady = false;

			OtherUser = new();
			OtherUser.SetSteamID(new(message.m_ulSteamIDUserChanged));
			OnOtherEnterLobby?.Invoke();
		}
		else if ((message.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft) > 0x00)
		{
			IsHost = true;
			OnOtherLeaveLobby?.Invoke();
			HasOtherUser = OtherUserReady = false;
			SteamNetworkingMessages.CloseSessionWithUser(ref OtherUser);
		}
	}


	public static void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t message)
	{
		SteamNetworkingMessages.AcceptSessionWithUser(ref message.m_identityRemote);
		HasOtherUser = true;
		OtherUser = message.m_identityRemote;
	}

	public static void OnLobbyJoinRequest(GameLobbyJoinRequested_t message)
	{
		if (InLobby)
			LeaveLobby();
		JoinLobby(message.m_steamIDLobby.m_SteamID);
	}
}