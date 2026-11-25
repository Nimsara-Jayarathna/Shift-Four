using UnityEngine;

namespace ShiftFour
{
    // The outdoor menu loads the one team-owned asset from Resources on successful extraction.
    public sealed class OutdoorCreditsConfig : ScriptableObject
    {
        [System.Serializable]
        internal struct Member
        {
            public string name, role, assignment;
        }

        [SerializeField, Min(1)] private int groupNumber;
        [SerializeField] private Member[] team;
        [SerializeField] private AudioClip music;

        internal string GroupLine => $"DEVELOPED BY GROUP - {groupNumber}";
        internal Member[] Team => team;
        internal AudioClip Music => music;
    }
}
