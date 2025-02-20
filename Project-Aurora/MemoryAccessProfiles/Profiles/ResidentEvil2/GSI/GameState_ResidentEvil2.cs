﻿using Aurora.Profiles;
using MemoryAccessProfiles.Profiles.ResidentEvil2.GSI.Nodes;

namespace MemoryAccessProfiles.Profiles.ResidentEvil2.GSI;

/// <summary>
/// A class representing various information relating to Resident Evil 2
/// </summary>
public class GameState_ResidentEvil2 : GameState
{
    private Player_ResidentEvil2 player;

    /// <summary>
    /// Information about the local player
    /// </summary>
    public Player_ResidentEvil2 Player
    {
        get
        {
            if (player == null)
                player = new Player_ResidentEvil2("");

            return player;
        }
    }

    /// <summary>
    /// Creates a default GameState_ResidentEvil2 instance.
    /// </summary>
    public GameState_ResidentEvil2() : base()
    {
    }

    /// <summary>
    /// Creates a GameState instance based on the passed json data.
    /// </summary>
    /// <param name="json_data">The passed json data</param>
    public GameState_ResidentEvil2(string json_data) : base(json_data)
    {
    }
}