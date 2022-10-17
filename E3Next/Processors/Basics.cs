﻿using E3Core.Data;
using E3Core.Settings;
using E3Core.Utility;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace E3Core.Processors
{
    public static class Basics
    {

        public static bool _following = false;
        public static Int32 _followTargetID = 0;
        public static string _followTargetName = String.Empty;

        public static Logging _log = E3._log;
        private static IMQ MQ = E3.MQ;
        private static ISpawns _spawns = E3._spawns;
        public static bool _isPaused = false;
        public static List<Int32> _groupMembers = new List<int>();
        private static Int64 _nextGroupCheck = 0;
        private static Int64 _nextGroupCheckInterval = 1000;

        private static Int64 _nextResourceCheck = 0;
        private static Int64 _nextResourceCheckInterval = 1000;
        public static void Init()
        {
            RegisterEventsCasting();
        }
        static void RegisterEventsCasting()
        {
           
            EventProcessor.RegisterEvent("InviteToGroup", "(.+) invites you to join a group.", (x) => {

                MQ.Cmd("/invite");
                MQ.Delay(300);

            });
            EventProcessor.RegisterEvent("InviteToRaid", "(.+) invites you to join a raid.", (x) => {
               
                MQ.Delay(500);
                MQ.Cmd("/raidaccept");

            });

            EventProcessor.RegisterEvent("InviteToDZ", "(.+) tells you, 'dzadd'", (x) => {
                if(x.match.Groups.Count>1)
                {
                    MQ.Cmd($"/dzadd {x.match.Groups[1].Value}");
                }
            });
            EventProcessor.RegisterEvent("InviteToDZ", "(.+) tells you, 'raidadd'", (x) => {
                if (x.match.Groups.Count > 1)
                {
                    MQ.Cmd($"/raidinvite {x.match.Groups[1].Value}");
                }
            });

            EventProcessor.RegisterCommand("/clickit", (x) =>
            {
                MQ.Cmd("/multiline ; /doortarget ; /timed 5 /click left door ");
                  //we are telling people to follow us
                E3._bots.BroadcastCommandToGroup("/clickit");

                MQ.Delay(1000);
                
            });
            EventProcessor.RegisterCommand("/followoff", (x) =>
            {
                RemoveFollow();
                if (x.args.Count == 0)
                {
                    //we are telling people to follow us
                    E3._bots.BroadcastCommandToGroup("/followoff all");
                }
            });

            EventProcessor.RegisterCommand("/e3p", (x) =>
            {
                //swap them
                 _isPaused = _isPaused?false:true;
                if(_isPaused) MQ.Write("\arPAUSING E3!");
                if (!_isPaused) MQ.Write("\agRunning E3 again!");

            });
            EventProcessor.RegisterCommand("/followme", (x) =>
            {
                string user = string.Empty;
                if(x.args.Count>0)
                {
                    user = x.args[0];
                    //we have someone to follow.
                    _followTargetID = MQ.Query<Int32>($"${{Spawn[{user}].ID}}");
                    if(_followTargetID > 0)
                    {
                        _followTargetName = user;
                        _following = true;
                        Assist.AssistOff();
                        AcquireFollow();
                    }
                }
                else
                {
                    //we are telling people to follow us
                    E3._bots.BroadcastCommandToGroup("/followme " + E3._characterSettings._characterName);
                }
            });
           

        }


        public static void RefreshGroupMembers()
        {
            if (!e3util.ShouldCheck(ref _nextGroupCheck, _nextGroupCheckInterval)) return;

            Int32 groupCount = MQ.Query<Int32>("${Group}");
            groupCount++;
            if (groupCount != _groupMembers.Count)
            {
                _groupMembers.Clear();
                //refresh group members.
                //see if any  of our members have it.
                for (Int32 i = 0; i < groupCount; i++)
                {
                    Int32 id = MQ.Query<Int32>($"${{Group.Member[{i}].ID}}");
                    _groupMembers.Add(id);
                }
            }
        }
        public static void RemoveFollow()
        {
            _followTargetID = 0;
            _followTargetName = string.Empty;
            MQ.Cmd("/squelch /afollow off");
            MQ.Cmd("/squelch /stick off");
           
        }

        public static void AcquireFollow()
        {

            Int32 instanceCount = MQ.Query<Int32>($"${{SpawnCount[id {_followTargetID} radius 250]}}");

            if (instanceCount > 0)
            {
                //they are in range
                if (MQ.Query<bool>($"${{Spawn[{_followTargetName}].LineOfSight}}"))
                {
                    Casting.TrueTarget(_followTargetID);
                    //if a bot, use afollow, else use stick
                    if (E3._bots.InZone(_followTargetName))
                    {
                        MQ.Cmd("/afollow on");
                    }
                    else
                    {
                        MQ.Cmd("/squelch /stick hold 20 uw");
                    }
                }
            }
           
        }
        public static bool AmIDead()
        {
            //scan through our inventory looking for a container.
            for (Int32 i = 1; i <= 10; i++)
            {
                bool SlotExists = MQ.Query<bool>($"${{Me.Inventory[pack{i}]}}");
                if (SlotExists)
                {
                    return false;
                }
            }
            return true;
        }
        public static bool InCombat()
        {
            bool inCombat = MQ.Query<bool>("${Me.Combat}") || MQ.Query<bool>("${Me.CombatState.Equal[Combat]}") || Assist._isAssisting;
            return inCombat;
        }
        public static void Check_Resources()
        {
            if (!e3util.ShouldCheck(ref _nextResourceCheck, _nextResourceCheckInterval)) return;

            if (E3._isInvis) return;

            bool pok = MQ.Query<bool>("${Zone.ShortName.Equal[poknowledge]}");
            if (pok) return;

            Int32 minMana = 35;
            Int32 minHP = 60;
            Int32 maxMana = 65;
            Int32 maxLoop = 10;

            Int32 totalClicksToTry = 40;
            Int32 minManaToTryAndHeal = 1000;

            if (!InCombat())
            {
                minMana = 70;
                maxMana = 95;
            }

            Int32 pctMana = MQ.Query<Int32>("${Me.PctMana}");
            Int32 currentHps = MQ.Query<Int32>("${Me.CurrentHPs}");
            if (pctMana > minMana) return;

            if(E3._currentClass== Data.Class.Enchanter)
            {
                bool manaDrawBuff = MQ.Query<bool>("${Bool[${Me.Buff[Mana Draw]}]}") || MQ.Query<bool>("${Bool[${Me.Song[Mana Draw]}]}");
                if(manaDrawBuff)
                {
                    if(pctMana>50)
                    {
                        return;
                    }
                }
            }

            if(E3._currentClass== Data.Class.Necromancer)
            {
                bool deathBloom = MQ.Query<bool>("${Bool[${Me.Buff[Death Bloom]}]}") || MQ.Query<bool>("${Bool[${Me.Song[Death Bloom]}]}");
                if(deathBloom)
                {
                    return;
                }
            }

            if (E3._currentClass == Data.Class.Shaman)
            {
                bool canniReady = MQ.Query<bool>("${Me.AltAbilityReady[Cannibalization]}");
                if (canniReady)
                {
                    Spell s;
                    if (!Spell._loadedSpellsByName.TryGetValue("Cannibalization", out s))
                    {
                        s = new Spell("Cannibalization");
                    }
                    if (s.CastType != CastType.None)
                    {
                        Casting.Cast(0, s);
                        return;
                    }
                }
            }

            if (MQ.Query<bool>("${Me.ItemReady[Summoned: Large Modulation Shard]}"))
            {
                if (MQ.Query<Int32>("${Math.Calc[${Me.MaxMana} - ${Me.CurrentMana}]") > 3500 && currentHps > 6000)
                {
                    Spell s;
                    if (!Spell._loadedSpellsByName.TryGetValue("Summoned: Large Modulation Shard", out s))
                    {
                        s = new Spell("Summoned: Large Modulation Shard");
                    }
                    if (s.CastType != CastType.None)
                    {
                        Casting.Cast(0, s);
                        return;
                    }

                }
            }
            if (MQ.Query<bool>("${Me.ItemReady[Azure Mind Crystal III]}"))
            {
                if (MQ.Query<Int32>("${Math.Calc[${Me.MaxMana} - ${Me.CurrentMana}]") > 3500)
                {
                    Spell s;
                    if (!Spell._loadedSpellsByName.TryGetValue("Azure Mind Crystal III", out s))
                    {
                        s = new Spell("Azure Mind Crystal III");
                    }
                    if (s.CastType != CastType.None)
                    {
                        Casting.Cast(0, s);
                        return;
                    }

                }
            }

            if (E3._currentClass == Data.Class.Necromancer)
            {
                bool deathBloomReady = MQ.Query<bool>("${Me.AltAbilityReady[Death Bloom]}") && !AmIDead();
                if (deathBloomReady)
                {
                    Spell s;
                    if (!Spell._loadedSpellsByName.TryGetValue("Death Bloom", out s))
                    {
                        s = new Spell("Death Bloom");
                    }
                    if (s.CastType != CastType.None)
                    {
                        Casting.Cast(0, s);
                        return;
                    }
                }
            }
            if (E3._currentClass == Data.Class.Enchanter)
            {
                bool manaDrawReady = MQ.Query<bool>("${Me.AltAbilityReady[Mana Draw]}") && !AmIDead();
                if (manaDrawReady)
                {
                    Spell s;
                    if (!Spell._loadedSpellsByName.TryGetValue("Mana Draw", out s))
                    {
                        s = new Spell("Mana Draw");
                    }
                    if (s.CastType != CastType.None)
                    {
                        Casting.Cast(0, s);
                        return;
                    }
                }
            }

            bool hasManaStone = MQ.Query<bool>("${Bool[${FindItem[=Manastone]}]}");

            if(hasManaStone)
            {

                MQ.Write("\agUsing Manastone...");
                Int32 pctHps = MQ.Query<Int32>("${Me.PctHPs}");
                pctMana = MQ.Query<Int32>("${Me.PctMana}");
                Int32 currentLoop = 0;
                while(pctHps>minHP && pctMana < maxMana)
                {
                    currentLoop++;
                    Int32 currentMana = MQ.Query<Int32>("${Me.CurrentMana}");

                    for(Int32 i =0;i<totalClicksToTry;i++)
                    {
                        MQ.Cmd("/useitem \"Manastone\"");
                    }
                    if((E3._currentClass & Class.Priest)==E3._currentClass)
                    {
                        if (Heals.SomeoneNeedsHealing(currentMana, pctMana))
                        {
                            return;
                        }
                    }
                    MQ.Delay(50);
                    if (Basics.InCombat())
                    {
                        if(currentLoop>maxLoop)
                        {
                            return;
                        }
                    }
                    pctHps = MQ.Query<Int32>("${Me.PctHPs}");
                    pctMana = MQ.Query<Int32>("${Me.PctMana}");
                }

            }

        }
    }
}
