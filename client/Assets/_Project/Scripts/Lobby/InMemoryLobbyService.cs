using System;
using System.Collections.Generic;
using System.Linq;

namespace SSAFYPlayTime.Lobby
{
    public sealed class InMemoryLobbyService
    {
        private readonly List<LobbyRoom> _rooms = new();

        public IReadOnlyList<LobbyRoom> GetRooms()
        {
            return _rooms.OrderByDescending(r => r.MemberCount).ThenBy(r => r.Name).ToList();
        }

        public LobbyRoom CreateRoom(string roomName, bool isPrivate, string password, string ownerNickname)
        {
            var trimmedName = (roomName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                throw new ArgumentException("Room name is required.", nameof(roomName));
            }

            if (isPrivate && string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password is required for private room.", nameof(password));
            }

            var room = new LobbyRoom(trimmedName, isPrivate, password, ownerNickname);
            _rooms.Add(room);
            return room;
        }

        public bool TryJoinRoom(LobbyRoom room, string nickname, string password, out string error)
        {
            error = string.Empty;
            if (room == null)
            {
                error = "Room not found.";
                return false;
            }

            if (!room.ValidatePassword(password))
            {
                error = "Invalid password.";
                return false;
            }

            if (!room.Members.Contains(nickname))
            {
                room.Members.Add(nickname);
            }

            return true;
        }

        public void LeaveRoom(LobbyRoom room, string nickname)
        {
            if (room == null)
            {
                return;
            }

            room.Members.RemoveAll(m => string.Equals(m, nickname, StringComparison.Ordinal));
            if (room.Members.Count == 0)
            {
                _rooms.Remove(room);
            }
        }
    }
}