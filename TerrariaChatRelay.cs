using On.Terraria.GameContent.NetModules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Net;
using Terraria.UI.Chat;
using TerrariaChatRelay;
using Microsoft.Xna.Framework;
using TerrariaChatRelay.Helpers;
using System.Threading.Tasks;

namespace TerrariaChatRelay
{
	public class TerrariaChatRelay : Mod
	{
		public Version LatestVersion = new Version("0.0.0.0");

		public TerrariaChatRelay()
		{
		}

		public override void Load()
		{
			base.Load();
						
			ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

			Global.Config = (TCRConfig)new TCRConfig().GetOrCreateConfiguration();

			// Intercept DeserializeAsServer method
			NetTextModule.DeserializeAsServer += NetTextModule_DeserializeAsServer;
			On.Terraria.NetMessage.BroadcastChatMessage += NetMessage_BroadcastChatMessage;
			On.Terraria.IO.WorldFile.LoadWorld_Version2 += OnWorldLoadStart;
			On.Terraria.Netplay.StopListening += OnServerStop;

			// Add subscribers to list
			EventManager.Initialize();

			// Clients auto subscribe to list.
			//foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			//{
			//	var OnLoadConfigAssemblies = asm.GetTypes()
			//		.Where(type => !type.IsAbstract  && type.IsSubclassOf(typeof(TCRPlugin)));

			//	if (OnLoadConfigAssemblies.Count() > 0)
			//	{
			//		foreach (Type type in OnLoadConfigAssemblies)
			//		{
			//			// Get the constructor and create an instance of Config
			//			ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);
			//			TCRPlugin plugin = (TCRPlugin)constructor.Invoke(new object[] { });
			//			plugin.Init(EventManager.Subscribers);
			//		}
			//	}
			//}
			new DiscordChatRelay.Main();

			EventManager.ConnectClients();

			if (Global.Config.CheckForLatestVersion)
				Task.Run(GetLatestVersionNumber);
		}

		/// <summary>
		/// <para>Loads the build.txt from GitHub to check if there is a newer version of TCR available. </para>
		/// If there is, a message will be displayed on the console and prepare a message for sending when the world is loading.
		/// </summary>
		public async Task GetLatestVersionNumber()
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
			var http = HttpWebRequest.CreateHttp("https://raw.githubusercontent.com/xPanini/TCR-TerrariaChatRelay/master/build.txt");

			WebResponse res = null;
			try { 
				res = await http.GetResponseAsync();
			}
			catch (Exception e)
			{
				return;
			}

			using (StreamReader sr = new StreamReader(res.GetResponseStream()))
			{
				string buildtxt = sr.ReadToEnd();
				buildtxt = buildtxt.ToLower();

				string line = "";
				using (StringReader stringreader = new StringReader(buildtxt))
				{
					do
					{
						line = stringreader.ReadLine();
						if (line.Contains("version"))
						{
							line = line.Replace(" ", "");
							line = line.Replace("version=", "");

							LatestVersion = new Version(line);
							if (LatestVersion > Version)
								PrettyPrint.Log($"A new version of TCR is available: V.{LatestVersion.ToString()}");

							line = null;
						}
					}
					while (line != null);
				}
			}
		}

		/// <summary>
		/// Handle disconnect for all clients, remove events, and finally dispose of config.
		/// </summary>
		public override void Unload()
		{
			EventManager.DisconnectClients();
			NetTextModule.DeserializeAsServer -= NetTextModule_DeserializeAsServer;
			On.Terraria.NetMessage.BroadcastChatMessage -= NetMessage_BroadcastChatMessage;
			Global.Config = null;
		}

		/// <summary>
		/// Hooks onto the World Load method to send a message when the server is starting.
		/// </summary>
		private int OnWorldLoadStart(On.Terraria.IO.WorldFile.orig_LoadWorld_Version2 orig, BinaryReader reader)
		{
			if (!Netplay.disconnect)
			{
				if (Global.Config.ShowServerStartMessage)
					EventManager.RaiseTerrariaMessageReceived(this, -1, "The server is starting!");

				if(LatestVersion > Version)
					EventManager.RaiseTerrariaMessageReceived(this, -1, $"A new version of TCR is available: V.{LatestVersion.ToString()}");
			}

			return orig(reader);
		}

		/// <summary>
		/// Hooks onto the StopListening method to send a message when the server is stopping.
		/// </summary>
		private void OnServerStop(On.Terraria.Netplay.orig_StopListening orig)
		{
			if (Global.Config.ShowServerStopMessage)
				EventManager.RaiseTerrariaMessageReceived(this, -1, "The server is stopping!");

			orig();
		}

		/// <summary>
		/// Intercept all other messages from Terraria. E.g. blood moon, death notifications, and player join/leaves.
		/// </summary>
		private void NetMessage_BroadcastChatMessage(On.Terraria.NetMessage.orig_BroadcastChatMessage orig, NetworkText text, Color color, int excludedPlayer)
		{
			if (Global.Config.ShowGameEvents)
				EventManager.RaiseTerrariaMessageReceived(this, -1, text.ToString());

			orig(text, color, excludedPlayer);
		}

		/// <summary>
		/// Intercept chat messages sent from players.
		/// </summary>
		private bool NetTextModule_DeserializeAsServer(NetTextModule.orig_DeserializeAsServer orig, Terraria.GameContent.NetModules.NetTextModule self, BinaryReader reader, int senderPlayerId)
		{
			long savedPosition = reader.BaseStream.Position;
			ChatMessage message = ChatMessage.Deserialize(reader);

			if (Global.Config.ShowChatMessages)
				EventManager.RaiseTerrariaMessageReceived(this, senderPlayerId, message.Text);

			reader.BaseStream.Position = savedPosition;
			return orig(self, reader, senderPlayerId);
		}

		public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
		{
			if (messageType == 12)
			{
				NetPacket packet = Terraria.GameContent.NetModules.NetTextModule.SerializeServerMessage(NetworkText.FromLiteral("This chat is powered by TerrariaChatRelay"), Color.LawnGreen, byte.MaxValue);
				NetManager.Instance.SendToClient(packet, playerNumber);
			}

			return false;
		}
	}
}
