﻿using CAF.IM.Core.Infrastructure;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CAF.IM.Core.Domain
{
    [BsonIgnoreExtraElements]
    public class ChatUser : BaseEntity
    {
        
        public string Id { get; set; }
        public string Name { get; set; }
        // MD5 email hash for gravatar
        public string Hash { get; set; }
        public string Salt { get; set; }
        public string HashedPassword { get; set; }
        public DateTime LastActivity { get; set; }
        public DateTime? LastNudged { get; set; }
        public int Status { get; set; }


        public string Note { get; set; }

    
        public string AfkNote { get; set; }

        public bool IsAfk { get; set; }

   
        public string Flag { get; set; }

        // TODO: Migrate everyone off identity and email
        public string Identity { get; set; }

        public string Email { get; set; }

        public bool IsAdmin { get; set; }
        public bool IsBanned { get; set; }

        // Request password reset token
        public string RequestPasswordResetId { get; set; }
        public DateTimeOffset? RequestPasswordResetValidThrough { get; set; }

        public string RawPreferences { get; set; }

        // List of clients that are currently connected for this user
        public virtual ICollection<ChatUserIdentity> Identities { get; set; }
        public virtual ICollection<ChatClient> ConnectedClients { get; set; }
        public virtual ICollection<ChatRoom> OwnedRooms { get; set; }
        public virtual ICollection<ChatRoom> Rooms { get; set; }

        public virtual ICollection<Attachment> Attachments { get; set; }
        public virtual ICollection<Notification> Notifications { get; set; }

        // Private rooms this user is allowed to go into
        public virtual ICollection<ChatRoom> AllowedRooms { get; set; }

        public ChatUser()
        {
            Identities = new SafeCollection<ChatUserIdentity>();
            ConnectedClients = new SafeCollection<ChatClient>();
            OwnedRooms = new SafeCollection<ChatRoom>();
            Rooms = new SafeCollection<ChatRoom>();
            AllowedRooms = new SafeCollection<ChatRoom>();
            Attachments = new SafeCollection<Attachment>();
            Notifications = new SafeCollection<Notification>();
        }

        public bool HasUserNameAndPasswordCredentials()
        {
            return !String.IsNullOrEmpty(HashedPassword) && !String.IsNullOrEmpty(Name);
        }


        public ChatUserPreferences Preferences
        {
            get
            {
                return ChatUserPreferences.GetPreferences(this);
            }

            set
            {
                value.Serialize(this);
            }
        }
    }
}
