﻿using System;
using System.Net;
using SocketLibrary;
using PhiClient;
using System.Collections.Generic;
using System.Collections;

namespace PhiServer
{
    public class Program
    {
        private Server server;
        private RealmData realmData;
        private Dictionary<ServerClient, User> connectedUsers = new Dictionary<ServerClient, User>();

        private object lockProcessPacket = new object();

        public void Start(IPAddress ipAddress, int port)
        {
            this.server = new Server(ipAddress, port);
            this.server.Start();
            Console.WriteLine("Launching server for " + ipAddress + " on port " + port);

            this.server.Connection += this.ConnectionCallback;
            this.server.Message += this.MessageCallback;
            this.server.Disconnection += this.DisconnectionCallback;

            this.realmData = new RealmData();
            this.realmData.PacketToClient += this.RealmPacketCallback;
        }

        private void ConnectionCallback(ServerClient client)
        {
            Console.WriteLine("Connection from " + client);
        }

        private void RealmPacketCallback(User user, Packet packet)
        {
            // We have to transmit a packet to a user, we find the connection
            // attached to this user and transmit the packet
            foreach (ServerClient client in this.connectedUsers.Keys)
            {
                User u;
                this.connectedUsers.TryGetValue(client, out u);
                if (u == user)
                {
                    this.SendPacket(client, user, packet);
                }
            }
        }

		private void SendPacket(ServerClient client, User user, Packet packet)
		{
            Console.WriteLine("Sending packet " + packet);
			client.Send(Packet.Serialize(packet, realmData, user));
        }

        private void DisconnectionCallback(ServerClient client)
        {
            User user;
            this.connectedUsers.TryGetValue(client, out user);
            if (user != null)
            {
                Console.WriteLine("Disconnection of " + user.name);
                this.connectedUsers.Remove(client);
                user.connected = false;
                this.realmData.BroadcastPacket(new UserConnectedPacket { user = user, connected = false });
            }
        }

        private void MessageCallback(ServerClient client, byte[] data)
        {
            lock (lockProcessPacket)
			{
				User user;
				this.connectedUsers.TryGetValue(client, out user);

				Packet packet = Packet.Deserialize(data, realmData, user);
                Console.WriteLine("Received packet " + packet);

                if (packet is AuthentificationPacket)
                {
                    // Special packets, (first sent from the client)
                    AuthentificationPacket authPacket = (AuthentificationPacket)packet;

                    // We first check if the version corresponds
                    if (authPacket.version != RealmData.VERSION)
                    {
                        this.SendPacket(client, user, new AuthentificationErrorPacket
                        {
                            error = "Server is version " + RealmData.VERSION + " but client is version " + authPacket.version
                        }
                        );
                        return;
                    }

                    // We check if an user already uses this key
                    user = this.realmData.users.FindLast(delegate (User u) { return u.hashedKey == authPacket.hashedKey; });
                    if (user == null)
                    {
                        user = this.realmData.ServerAddUser(authPacket.name, authPacket.hashedKey);
                        user.connected = true;

                        Console.WriteLine("Creating user \"" + user.name + "\" with ID " + user.getID());

                        // We send a notify to all users connected about the new user
                        this.realmData.BroadcastPacketExcept(new NewUserPacket { user = user }, user);
                    }
                    else
                    {
                        // We send a connect notification to all users
                        user.connected = true;
                        this.realmData.BroadcastPacketExcept(new UserConnectedPacket { user = user, connected = true }, user);
                    }

                    this.connectedUsers.Add(client, user);


                    // We respond with a StatePacket that contains all synchronisation data
                    this.SendPacket(client, user, new SynchronisationPacket { user = user, realmData = this.realmData });
                }
                else
                {
                    if (user == null)
                    {
                        // We ignore this package
                        Console.WriteLine("Ignore packet because unknown user");
                        return;
                    }

                    // Normal packets, we defer the execution
                    packet.Apply(user, this.realmData);
                }
            }
        }

        static void Main(string[] args)
        {
            Program program = new Program();

            program.Start(IPAddress.Any, 16180);

            Console.Read();
        }
    }
}
