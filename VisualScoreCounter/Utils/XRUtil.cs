using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VisualScoreCounter.Utils
{
    internal static class XRUtil
    {
        public static bool IsPresent()
        {
            var xrDisplaySubsystems = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(xrDisplaySubsystems);
            foreach (var xrDisplay in xrDisplaySubsystems)
            {
                if (xrDisplay.running)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
