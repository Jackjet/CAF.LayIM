﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.AspNet.SignalR.Transports;
using Newtonsoft.Json;
using Ninject;
using CAF.IM.Core.Infrastructure;
using CAF.IM.Core.Domain;
using CAF.IM.Services.ViewModels;
using CAF.IM.Core.Data;

namespace CAF.IM.Services
{
    public class PresenceMonitor
    {
        private volatile bool _running;
        private Timer _timer;
        private readonly TimeSpan _presenceCheckInterval = TimeSpan.FromMinutes(1);

        private readonly IKernel _kernel;
        private readonly IHubContext _hubContext;
        private readonly ITransportHeartbeat _heartbeat;

        public PresenceMonitor(IKernel kernel,
                               IConnectionManager connectionManager,
                               ITransportHeartbeat heartbeat)
        {
            _kernel = kernel;
            _hubContext = connectionManager.GetHubContext<Chat>();
            _heartbeat = heartbeat;
        }

        public void Start()
        {
            // Start the timer
            _timer = new Timer(_ =>
            {
                Check();
            },
            null,
            TimeSpan.Zero,
            _presenceCheckInterval);
        }

        private void Check()
        {
            if (_running)
            {
                return;
            }

            _running = true;

            ILogger logger = null;

            try
            {
                logger = _kernel.Get<ILogger>();

                logger.Log("Checking user presence");

                var repo = _kernel.Get<IRepository<ChatUser>>();
                var repoChatClient = _kernel.Get<IRepository<ChatClient>>();
                // Update the connection presence
                UpdatePresence(logger, repo, repoChatClient);

                // Remove zombie connections
                RemoveZombies(logger, repoChatClient);

                // Remove users with no connections
                RemoveOfflineUsers(logger, repo);

                // Check the user status
                CheckUserStatus(logger, repo);

            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.Log(ex);
                }
            }
            finally
            {
                _running = false;
            }
        }

        private void UpdatePresence(ILogger logger, IRepository<ChatUser> repo, IRepository<ChatClient> repoChatClient)
        {
            // Get all connections on this node and update the activity
            foreach (var connection in _heartbeat.GetConnections())
            {
                if (!connection.IsAlive)
                {
                    continue;
                }

                ChatClient client = repoChatClient.GetById(connection.ConnectionId);

                if (client != null)
                {
                    client.LastActivity = DateTimeOffset.UtcNow;
                }
                else
                {
                    EnsureClientConnected(logger, repo, repoChatClient, connection);
                }
                repoChatClient.Update(client);
            }


        }

        // This is an uber hack to make sure the db is in sync with SignalR
        private void EnsureClientConnected(ILogger logger, IRepository<ChatUser> repo, IRepository<ChatClient> repoChatClient, ITrackingConnection connection)
        {
            var contextField = connection.GetType().GetField("_context",
                                          BindingFlags.NonPublic | BindingFlags.Instance);
            if (contextField == null)
            {
                return;
            }

            var context = contextField.GetValue(connection) as HostContext;

            if (context == null)
            {
                return;
            }

            string connectionData = context.Request.QueryString["connectionData"];

            if (String.IsNullOrEmpty(connectionData))
            {
                return;
            }

            var hubs = JsonConvert.DeserializeObject<HubConnectionData[]>(connectionData);

            if (hubs.Length != 1)
            {
                return;
            }

            // We only care about the chat hub
            if (!hubs[0].Name.Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            logger.Log("Connection {0} exists but isn't tracked.", connection.ConnectionId);

            string userId = context.Request.User.GetUserId();

            ChatUser user = repo.GetById(userId);
            if (user == null)
            {
                logger.Log("Unable to find user with id {0}", userId);
                return;
            }

            var client = new ChatClient
            {
                Id = connection.ConnectionId,
                User = user,
                UserAgent = context.Request.Headers["User-Agent"],
                LastActivity = DateTimeOffset.UtcNow,
                LastClientActivity = user.LastActivity
            };

            repoChatClient.Insert(client);

        }

        private static void RemoveZombies(ILogger logger, IRepository<ChatClient> repo)
        {
            // Remove all zombie clients 
            var zombies = repo.Table.Where(c => (DateTime.UtcNow - c.LastActivity).Minutes > 3);

            // We're doing to list since there's no MARS support on azure
            foreach (var client in zombies.ToList())
            {
                logger.Log("Removed zombie connection {0}", client.Id);

                repo.Delete(client);
            }
        }

        private void RemoveOfflineUsers(ILogger logger, IRepository<ChatUser> repo)
        {
            var offlineUsers = new List<ChatUser>();
            IQueryable<ChatUser> users = repo.Table.Online();

            foreach (var user in users.ToList())
            {
                if (user.ConnectedClients.Count == 0)
                {
                    logger.Log("{0} has no clients. Marking as offline", user.Name);

                    // Fix users that are marked as inactive but have no clients
                    user.Status = (int)UserStatus.Offline;
                    offlineUsers.Add(user);
                    repo.Update(user);
                }
            }

            if (offlineUsers.Count > 0)
            {
                PerformRoomAction(offlineUsers, async roomGroup =>
                {
                    foreach (var user in roomGroup.Users)
                    {
                        await _hubContext.Clients.Group(roomGroup.Room.Name).leave(user, roomGroup.Room.Name);
                    }
                });


            }
        }

        private void CheckUserStatus(ILogger logger, IRepository<ChatUser> repo)
        {
            var inactiveUsers = new List<ChatUser>();

            IQueryable<ChatUser> users = repo.Table.Online().Where(u => (DateTime.UtcNow - u.LastActivity).Minutes > 5);

            foreach (var user in users.ToList())
            {
                user.Status = (int)UserStatus.Inactive;
                inactiveUsers.Add(user);
                repo.Update(user);
            }

            if (inactiveUsers.Count > 0)
            {
                PerformRoomAction(inactiveUsers, async roomGroup =>
                {
                    await _hubContext.Clients.Group(roomGroup.Room.Name).markInactive(roomGroup.Users);
                });


            }
        }

        private static async void PerformRoomAction(List<ChatUser> users, Func<RoomGroup, Task> callback)
        {
            var roomGroups = from u in users
                             from r in u.Rooms
                             select new { User = u, Room = r } into tuple
                             group tuple by tuple.Room into g
                             select new RoomGroup
                             {
                                 Room = g.Key,
                                 Users = g.Select(t => new UserViewModel(t.User))
                             };

            foreach (var roomGroup in roomGroups)
            {
                try
                {
                    await callback(roomGroup);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error occurred: " + ex);
                }
            }
        }

        private class RoomGroup
        {
            public ChatRoom Room { get; set; }
            public IEnumerable<UserViewModel> Users { get; set; }
        }

        private class HubConnectionData
        {
            public string Name { get; set; }
        }
    }
}