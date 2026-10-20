// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

namespace LibreLancer.Missions
{
    /// <summary>A change of a mission trigger that the runtime reports to an observer.</summary>
    public enum MissionTriggerChange
    {
        /// <summary>The trigger joined the list of active triggers.</summary>
        Started,

        /// <summary>The trigger ran: its actions start now.</summary>
        Ran,

        /// <summary>A stop marked an active trigger, so it leaves the list of active triggers.</summary>
        Stopped,
    }

    /// <summary>
    /// Gets the trigger changes of a mission runtime (OL-0070), for example for a live view of the triggers in an
    /// editor. The observer changes no rule of the runtime. The runtime calls it on the server thread, and some calls
    /// come while the runtime holds its lock. So an observer must only queue the data and return at once.
    /// </summary>
    public interface IMissionObserver
    {
        /// <summary>The server makes a new runtime for a mission. The trigger changes of that runtime follow.</summary>
        /// <param name="mission">The nickname of the story mission, or the type of a random mission.</param>
        void MissionLoaded(string mission);

        /// <summary>A trigger of the runtime changed.</summary>
        /// <param name="trigger">The nickname of the trigger.</param>
        void TriggerChanged(string trigger, MissionTriggerChange change);
    }
}
