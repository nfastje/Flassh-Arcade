using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>What a message is about, and so what the player can do about it.</summary>
    public enum MessageKind
    {
        /// <summary>Just words.</summary>
        Note = 0,
        /// <summary>A tribe invites the player. A = the tribe. Accept or decline.</summary>
        Invitation = 1,
        /// <summary>A tribe offers the player's tribe a pact. A = the tribe, B = the <see cref="RelationKind"/>. Accept or decline.</summary>
        PactOffer = 2,
        /// <summary>A tribe mate's village is under attack. A = the village. Send support.</summary>
        SupportRequest = 3,
        /// <summary>The tribe leader names a target. A = the village. Attack it.</summary>
        Order = 4,
        /// <summary>A faction asks the human's tribe to join it. A = its leading tribe. Accept or decline.</summary>
        FactionInvite = 5,
        /// <summary>A core lord asks for one of the human's villages. A = the village, B = the lord. Accept or decline.</summary>
        FeedRequest = 6,
        /// <summary>A satellite lord offers the human a village. A = the village. Send noblemen.</summary>
        FeedOffer = 7,
    }

    /// <summary>A message to the player from a lord or a tribe (lords never write to each other).</summary>
    [Serializable]
    public class Message
    {
        public int Id;
        public double Time;
        public MessageKind Kind;
        /// <summary>Who wrote it (-1: nobody in particular), and on behalf of which tribe (-1: none).</summary>
        public int FromPlayerId = -1, TribeId = -1;
        public string From, Subject, Body;
        public int A, B;
        public bool Read;
        /// <summary>For messages that ask something: whether the player has answered (or it no longer applies).</summary>
        public bool Answered;
    }

    public partial class World
    {
        /// <summary>Oldest messages are dropped beyond this many.</summary>
        public const int MaxMessages = 200;

        public List<Message> Messages = new List<Message>();
        public int NextMessageId = 1;

        public int UnreadMessages => Messages.FindAll(m => !m.Read).Count;

        public Message FindMessage(int id) => Messages.Find(m => m.Id == id);

        /// <summary>Delivers a message to the human player.</summary>
        Message Write(MessageKind kind, Player from, string subject, string body, int a = 0, int b = 0, int tribeId = -1)
        {
            var message = new Message
            {
                Id = NextMessageId++, Time = Now, Kind = kind, FromPlayerId = from?.Id ?? -1, TribeId = tribeId,
                From = from != null ? NameWithTag(from) : "", Subject = subject, Body = body, A = a, B = b,
            };
            Messages.Add(message);
            if (Messages.Count > MaxMessages) Messages.RemoveRange(0, Messages.Count - MaxMessages);
            return message;
        }

        public void MarkAllMessagesRead()
        {
            foreach (var m in Messages) m.Read = true;
        }

        public bool DeleteMessage(int id) => Messages.RemoveAll(m => m.Id == id) > 0;

        /// <summary>A few repeatable words picked from a list (so messages don't all read the same).</summary>
        string Pick(int salt, params string[] options) => options[(int)(Terrain.Hash(Settings.Seed ^ 0x27D4EB2F, NextMessageId * 31 + salt, (int)(Now / 60)) * options.Length) % options.Length];
    }
}
