using System;
using System.Collections.Generic;

namespace SSAFYPlayTime.Lobby
{
    public sealed class LobbyRoom
    {
        private readonly string _password;

        public LobbyRoom(string name, bool isPrivate, string password, string ownerNickname)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
            IsPrivate = isPrivate;
            _password = password ?? string.Empty;
            OwnerNickname = ownerNickname;
            Members = new List<string> { ownerNickname };
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsPrivate { get; }
        public string OwnerNickname { get; }
        public List<string> Members { get; }
        public int MemberCount => Members.Count;

        public bool ValidatePassword(string enteredPassword)
        {
            if (!IsPrivate)
            {
                return true;
            }

            return string.Equals(_password, enteredPassword ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
