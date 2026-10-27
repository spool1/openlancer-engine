using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server;

public class FormationTools
{
    public static void EnterFormation(GameObject self, GameObject tgt, Vector3 offset)
    {
        if (self.Formation != null && self.Formation != tgt.Formation)
            self.Formation.Remove(self);

        if (tgt.Formation == null)
        {
            tgt.Formation = new ShipFormation(tgt, self);
        }
        else
        {
            if (!tgt.Formation.Contains(self))
                tgt.Formation.Add(self);
        }
        self.Formation = tgt.Formation;
        if (offset != Vector3.Zero)
            tgt.Formation.SetShipOffset(self, offset);
        if (self.TryGetComponent<AutopilotComponent>(out var ap))
        {
            ap.StartFormation();
        }
    }

    public static void MakeNewFormation(GameObject obj, GameWorld world, string formation, List<string?> others)
    {
        // TODO: Gross
        var formDef = world.Server!.Server.GameData.Items.GetFormation(formation);
        if (formDef == null)
        {
            FLLog.Warning("Mission", $"Formation definition `{formation}` was not found. Falling back to a simple group.");
        }

        var ships = new List<GameObject>();
        foreach (var tgt in others)
        {
            if (tgt != null && world.GetObject(tgt) is { } o)
            {
                ships.Add(o);
            }
        }

        MakeNewFormation(obj, formDef, ships);
    }

    // OL-0073: a second FollowPlayer list of the player crashed the game. The player that led a formation became a follower
    // of its own new formation. The next list then left the player in the old formation, its disband cleared the
    // formation of the player, and the last check read a null formation. The lead is now never its own follower, a ship
    // that is already in the new formation stays, and the last check reads the new formation.
    internal static void MakeNewFormation(GameObject obj, FormationDef? formDef, IReadOnlyList<GameObject> others)
    {
        GameObject? player = null;
        bool playerLead = false;
        // Preserve player (required)
        if (obj.Formation != null)
        {
            if (obj.Formation.LeadShip.TryGetComponent<SPlayerComponent>(out _))
            {
                player = obj.Formation.LeadShip;
                playerLead = true;
            }
            else
            {
                foreach (var x in obj.Formation.Followers)
                {
                    if (x.TryGetComponent<SPlayerComponent>(out _))
                    {
                        player = x;
                        break;
                    }
                }
            }
        }

        obj.Formation?.Remove(obj);
        ShipFormation form;
        if (formDef != null)
        {
            form = new ShipFormation(playerLead ? player! : obj, formDef);
        }
        else
        {
            form = new ShipFormation(playerLead ? player! : obj);
        }
        if (playerLead && player != null && player != obj)
        {
            form.Add(obj);
        }

        obj.Formation = form;
        foreach (var o in others)
        {
            if (form.Contains(o))
            {
                continue;
            }

            o.Formation?.Remove(o);
            form.Add(o);
            o.Formation = form;

            if (o.TryGetComponent<AutopilotComponent>(out var ap))
            {
                ap.StartFormation();
            }
        }

        if (player != null && !form.Contains(player))
        {
            form.Add(player);
            player.Formation = form;
        }
    }
}
