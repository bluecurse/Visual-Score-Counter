using UnityEngine;
using static BeatmapSaveDataVersion3.BeatmapSaveData;

namespace VisualScoreCounter.VSCounter
{
    internal class VSCounterTweenHelper : MonoBehaviour
    {
        public VSCounterTweenHelper()
        {
            animationTime = 0.20f;
            easeType = EaseType.InExpo;
        }

        public float animationTime { get; set; }
        public EaseType easeType { get; set; }
    }
}
