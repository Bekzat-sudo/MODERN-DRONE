using System;
using System.Collections.Generic;

namespace ModernDrone.Drone
{
    /// <summary>Where an OSD text element sits on the video frame (Betaflight-style fixed slots).</summary>
    public enum OsdSlot
    {
        Off,
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    public enum OsdItem
    {
        FlightMode,
        InputSource,
        Link,
        Timer,
        Heading,
        Voltage,
        Current,
        UsedMah,
        Throttle,
        Speed,
        Altitude,
        VerticalSpeed,
        HomeDistance,
        CraftName
    }

    [Serializable]
    public sealed class OsdEntry
    {
        public OsdItem item;
        public OsdSlot slot;

        public OsdEntry() { }
        public OsdEntry(OsdItem item, OsdSlot slot) { this.item = item; this.slot = slot; }
    }

    /// <summary>
    /// Pilot-configurable OSD layout: a slot per text element plus the graphic elements. Two presets: the full
    /// Betaflight-style layout and a minimal one as seen on strike-drone feeds (flight mode top centre, artificial
    /// horizon, crosshair, throttle at the right, craft name at the bottom).
    /// </summary>
    [Serializable]
    public sealed class OsdSettings
    {
        public List<OsdEntry> entries = Minimal();
        public bool crosshair = true;
        public bool horizon = true;
        [UnityEngine.Tooltip("Betaflight horizon sidebars: short vertical scales left and right of the centre.")]
        public bool sidebars;
        public bool chargeAxis = true;
        public bool warnings = true;
        public string craftName = "MODERN DRONE";

        public OsdSlot SlotOf(OsdItem item)
        {
            foreach (var e in entries)
                if (e.item == item) return e.slot;
            return OsdSlot.Off;
        }

        public void SetSlot(OsdItem item, OsdSlot slot)
        {
            foreach (var e in entries)
                if (e.item == item) { e.slot = slot; return; }
            entries.Add(new OsdEntry(item, slot));
        }

        public static List<OsdEntry> Minimal() => new List<OsdEntry>
        {
            new OsdEntry(OsdItem.FlightMode, OsdSlot.TopCenter),
            new OsdEntry(OsdItem.Throttle, OsdSlot.MiddleRight),
            new OsdEntry(OsdItem.CraftName, OsdSlot.BottomCenter),
            new OsdEntry(OsdItem.Link, OsdSlot.Off),
            new OsdEntry(OsdItem.Voltage, OsdSlot.Off),
        };

        public static List<OsdEntry> Full() => new List<OsdEntry>
        {
            new OsdEntry(OsdItem.FlightMode, OsdSlot.TopLeft),
            new OsdEntry(OsdItem.InputSource, OsdSlot.TopLeft),
            new OsdEntry(OsdItem.Link, OsdSlot.TopLeft),
            new OsdEntry(OsdItem.Heading, OsdSlot.TopCenter),
            new OsdEntry(OsdItem.Timer, OsdSlot.TopRight),
            new OsdEntry(OsdItem.Speed, OsdSlot.MiddleLeft),
            new OsdEntry(OsdItem.Altitude, OsdSlot.MiddleRight),
            new OsdEntry(OsdItem.VerticalSpeed, OsdSlot.MiddleRight),
            new OsdEntry(OsdItem.Voltage, OsdSlot.BottomLeft),
            new OsdEntry(OsdItem.Current, OsdSlot.BottomLeft),
            new OsdEntry(OsdItem.HomeDistance, OsdSlot.BottomCenter),
            new OsdEntry(OsdItem.UsedMah, OsdSlot.BottomRight),
            new OsdEntry(OsdItem.Throttle, OsdSlot.BottomRight),
            new OsdEntry(OsdItem.CraftName, OsdSlot.Off),
        };
    }
}
