using System;
using System.Collections.Generic;

namespace SSAFYPlayTime.Lobby
{
    // 로컬 로비에서 사용하는 방 데이터 모델.
    public sealed class LobbyRoom
    {
        // 비공개 방 검증에 사용하는 내부 비밀번호.
        private readonly string _password;

        // 방 기본 정보와 초기 멤버(방장)를 설정한다.
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

        // 공개 방은 항상 통과, 비공개 방은 문자열 일치로 검증한다.
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
