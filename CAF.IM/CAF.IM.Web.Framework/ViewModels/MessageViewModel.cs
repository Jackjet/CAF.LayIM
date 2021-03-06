﻿using CAF.IM.Core.Domain;
using CAF.IM.Services;
using System;


namespace CAF.IM.Web.Framework.ViewModels
{
    public class MessageViewModel
    {
        public MessageViewModel(ChatMessage message)
        {
            Id = message.Id;
            Content = message.Content;
            HtmlContent = message.HtmlContent;
            User = new UserViewModel(message.User);
            UserRoomPresence = ChatService.GetUserRoomPresence(message.User, message.Room);
            When = message.When;
            HtmlEncoded = message.HtmlEncoded;
            MessageType = message.MessageType;
            Source = message.Source;
            ImageUrl = message.ImageUrl;
        }

        public bool HtmlEncoded { get; set; }
        public string Id { get; set; }
        public string Content { get; set; }
        public string HtmlContent { get; set; }
        public DateTimeOffset When { get; set; }
        public UserViewModel User { get; set; }
        public int MessageType { get; set; }
        public string UserRoomPresence { get; set; }

        public string ImageUrl { get; set; }
        public string Source { get; set; }
    }
}