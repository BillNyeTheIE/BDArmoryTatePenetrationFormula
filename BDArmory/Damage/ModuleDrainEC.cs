using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace BDArmory.Damage
{
    public class ModuleDrainEC : PartModule
    {
        public float incomingDamage = 0; //damage from EMP source
        public float EMPDamage = 0; //total EMP buildup accrued
        float EMPThreshold = 100; //craft get temporarily disabled
        float BrickThreshold = 1000; //craft get permanently bricked
        public bool softEMP = true; //can EMPdamage exceed EMPthreshold?
        private bool disabled = false; //prevent further EMP buildup while rebooting
        public bool bricked = false; //He's dead, jeb
        public bool isMissile = false;
        private float rebootTimer = 15;
        private bool initialAIState = false; //if for whatever reason players are manually firing EMPs at targets with AI/WM disabled, don't enable them when vessel reboots
        private bool initialWMState = false;
        int totalEMPBuildupTiers = 8;
        public int currentEMPBuildup = 0;
        float EMPTierThreshold = 10;
        /// <summary>
        /// So. basic idea is EMP base threshold determined by seat count - more command seats, more flight comps, more redundancy.
        /// Probe cores look at SASServiceLevel, since that's a decent measure of how 'advanced' the probe is/what sort of electronics it'd have.
        /// EMP Damage is then modified based on part mass and armor/hull materials (incl. that of the command part). 
        /// </summary>
        void Start()
        {
            foreach (var moduleCommand in VesselModuleRegistry.GetModuleCommands(vessel))
            {
                if (moduleCommand.part.CrewCapacity > 0) EMPThreshold += moduleCommand.part.CrewCapacity * 100; //cockpits worth 100 per seat
                if (moduleCommand.minimumCrew == 0)
                {
                    var CPULevel = moduleCommand.part.FindModuleImplementing<ModuleSAS>();
                    EMPThreshold += 10;
                    if (CPULevel != null) EMPThreshold += CPULevel.SASServiceLevel * 20; //drones worth 10-70, depending on capability
                }
            }
            var MB = vessel.rootPart.FindModuleImplementing<MissileBase>();
            if (MB != null)
            {
                isMissile = true;
                EMPThreshold = 5;
            }
            BrickThreshold = EMPThreshold * 5;
            EMPTierThreshold = EMPThreshold / totalEMPBuildupTiers;
            //EMPThreshold = (100 * (seatCount - ((1 - (vessel.GetTotalMass() / seatCount)) / 2));     
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDArmorySetup.GameIsPaused) return;
            if (BDArmorySettings.PAINTBALL_MODE && !isMissile)
            {
                EMPDamage = 0; 
                incomingDamage = 0;
                if (disabled) EnableVessel(6);
                return;
                
            }
            if (!bricked)
            {
                if (EMPDamage > 0 || incomingDamage > 0)
                {
                    UpdateEMPLevel();
                }
            }

        }

        private int lastTierTriggered = 0;
        void UpdateEMPLevel()
        {
            if ((!disabled || (disabled && !softEMP)) && incomingDamage > 0)
            {
                EMPDamage += incomingDamage; //only accumulate EMP damage if it's hard EMP or craft isn't disabled
                incomingDamage = 0; //reset incoming damage amount
                if (disabled && !softEMP)
                {
                    if (rebootTimer > 0)
                    {
                        rebootTimer += incomingDamage / 100; //if getting hit by new sources of hard EMP, add to reboot timer
                    }
                }
            }
            if (disabled)
            {
                //EMPDamage = Mathf.Clamp(EMPDamage - 5 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity); //speed EMP cooldown, if electrolaser'd takes about ~10 sec to reboot. may need to be reduced further
                //fatal if fast+low alt, but higher alt or good glide ratio is survivable
                if (rebootTimer > 0)
                {
                    rebootTimer -= 1 * TimeWarp.fixedDeltaTime;
                }
                else
                {
                    EMPDamage = 0;
                }
            }
            else
            {
                EMPDamage = Mathf.Clamp(EMPDamage - 5 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity); //have EMP buildup dissipate over time
            }
            if (isMissile && EMPDamage > 10)
            {
                foreach (Part p in vessel.parts)
                {
                    var MB = p.FindModuleImplementing<MissileBase>();
                    if (MB != null)
                    {
                        MB.guidanceActive = false;
                    }
                }
                bricked = true;
                return;
            }
            //if (EMPDamage > EMPThreshold && !bricked && !disabled) //does the damage exceed the soft cap, but not the hard cap?
            if (!bricked && !disabled && EMPDamage > 0) //does the damage exceed the soft cap, but not the hard cap?
            {
                currentEMPBuildup = Mathf.FloorToInt(EMPDamage / EMPTierThreshold);
                //Debug.Log($"[BDArmory.ModuleDrainEC]: currentEMPBuildup Tier on {vessel.GetName()}: {currentEMPBuildup}. last Tier Triggered: {lastTierTriggered}");
                if (currentEMPBuildup > lastTierTriggered)
                {                    
                    //Debug.Log($"[BDArmory.ModuleDrainEC]: Applying EMP Threshold {currentEMPBuildup} to {vessel.GetName()}");
                    DisableVessel(currentEMPBuildup);
                    lastTierTriggered = currentEMPBuildup;
                }
                if (currentEMPBuildup < lastTierTriggered)
                {                    
                    //Debug.Log($"[BDArmory.ModuleDrainEC]: Removing EMP Threshold {currentEMPBuildup} from {vessel.GetName()}");
                    EnableVessel(currentEMPBuildup);
                    lastTierTriggered = currentEMPBuildup;
                }
            }

            if (EMPDamage > BrickThreshold && !bricked) //does the damage exceed the hard cap?
            {
                bricked = true; //if so brick the craft
                var message = vessel.vesselName + " is bricked!";
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);
            }
            if (EMPDamage <= 0 && disabled && !bricked) //reset craft
            {
                var message = "Rebooting " + vessel.vesselName;
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                EnableVessel(6);
            }
        }
        private void DisableVessel(int EMPbuildup)
        {
            if (EMPbuildup >= 1) //deactivate sensors
            {
                if (EMPbuildup <= lastTierTriggered) return; //don't re-trigger effects from a previous DisableVessel proc.
                foreach (var radar in VesselModuleRegistry.GetModules<ModuleRadar>(vessel))
                {
                    if (radar.radarEnabled)
                        radar.DisableRadar();
                }
                foreach (var spaceRadar in VesselModuleRegistry.GetModules<ModuleSpaceRadar>(vessel))
                {
                    if (spaceRadar.radarEnabled)
                        spaceRadar.DisableRadar();
                }
                foreach (var camera in VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel))
                {
                    if (camera.cameraEnabled)
                        camera.DisableCamera();
                }
                foreach (var IRST in VesselModuleRegistry.GetModules<ModuleIRST>(vessel))
                {
                    if (IRST.enabled)
                        IRST.DisableIRST();
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Sensors on {vessel.GetName()}");
            }
            if (EMPbuildup >= 2) //deactivate Engines
            {
                if (EMPbuildup <= lastTierTriggered) return;
                foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel))
                {
                    engine.Shutdown();
                    engine.allowRestart = false;
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Engines on {vessel.GetName()}");
            }
            if (EMPbuildup >= 3) //deactivate control surfaces and other hydraulics
            {
                if (EMPbuildup <= lastTierTriggered) return;
                foreach (var ctrl in VesselModuleRegistry.GetModules<ModuleControlSurface>(vessel))
                {
                    ctrl.authorityLimiter /= 10; //simpler than having to store all control surface values in a list somewhere
                    ctrl.ctrlSurfaceRange /= 10;
                }
                foreach (var turret in VesselModuleRegistry.GetModules<ModuleTurret>(vessel))
                {
                    turret.yawSpeedDPS /= 100;
                    turret.pitchSpeedDPS /= 100;
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling ControlSurfaces on {vessel.GetName()}");
            }
            if (EMPbuildup >= 4) //deactivate Weapons
            {
                if (EMPbuildup <= lastTierTriggered) return;
                foreach (var weapon in VesselModuleRegistry.GetModuleWeapons(vessel))
                {
                    weapon.weaponState = ModuleWeapon.WeaponStates.Locked; //prevent weapons from firing
                }
                foreach (var missile in VesselModuleRegistry.GetMissileBases(vessel))
                {
                    missile.engageRangeMax /= 10000; //prevent weapons from firing
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Disabling Weapons on {vessel.GetName()}");
            }
            if (EMPbuildup >= 5) //drain electrics.
            {
                if (EMPbuildup <= lastTierTriggered) return;
                foreach (Part p in vessel.parts)
                {
                    PartResource r = p.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
                    if (r != null)
                    {
                        if (r.amount >= 0)
                        {
                            p.RequestResource("ElectricCharge", r.amount);
                            //Random battery Fire if 'Fires' Battledamage enabled?
                        }
                    }
                }
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleDrainEC]: Shorting Electrics on {vessel.GetName()}");
            }
            if (EMPbuildup >= 6) //deactivate control
            {
                if (EMPbuildup <= lastTierTriggered) return;
                disabled = true;
                foreach (var command in VesselModuleRegistry.GetModuleCommands(vessel))
                {
                    {
                        command.minimumCrew *= 10; //disable vessel control
                    }
                }
                foreach (var AI in VesselModuleRegistry.GetIBDAIControls(vessel))
                {
                    if (AI.pilotEnabled) initialAIState = true;
                    AI.DeactivatePilot(); //disable AI
                }
                foreach (var WM in VesselModuleRegistry.GetMissileFires(vessel))
                {
                    if (initialWMState)
                    {
                        if (WM.guardMode) initialWMState = true;
                        WM.guardMode = false; //disable guardmode
                        WM.debilitated = true; //for weapon selection and targeting;
                    }
                }
                rebootTimer = BDArmorySettings.WEAPON_FX_DURATION;
                var message = "Disabling " + vessel.vesselName + " for " + rebootTimer + "s due to EMP damage";
                if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log("[BDArmory.ModuleDrainEC]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);

                var empFX = Instantiate(GameDatabase.Instance.GetModel("BDArmory/FX/Electroshock"),
                vessel.rootPart.transform.position, Quaternion.identity);
                empFX.SetActive(true);
                empFX.transform.SetParent(vessel.rootPart.transform);
                empFX.AddComponent<EMPShock>();
            }
        }
        private void EnableVessel(int TierToEnable)
        {
            switch (TierToEnable)
            {
                case 1: //reactivate sensors
                    {
                        foreach (var radar in VesselModuleRegistry.GetModules<ModuleRadar>(vessel))
                            {
                                if (radar.radarEnabled)
                                    radar.DisableRadar();
                            }
                        foreach (var spaceRadar in VesselModuleRegistry.GetModules<ModuleSpaceRadar>(vessel))
                        {
                            if (spaceRadar.radarEnabled)
                                spaceRadar.DisableRadar();
                        }
                        foreach (var camera in VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel))
                            {
                                if (camera.cameraEnabled)
                                    camera.DisableCamera();
                            }
                        foreach (var IRST in VesselModuleRegistry.GetModules<ModuleIRST>(vessel))
                        {
                            if (IRST.enabled)
                                IRST.DisableIRST();
                        }
                        break;
                    }
                case 2: //reactivate Engines
                    {
                        foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel))
                        {
                            engine.allowRestart = true;
                        }
                        vessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom10); // restart engines
                        break;
                    }
                case 3: //reactivate control surfaces
                    {
                        foreach (var ctrl in VesselModuleRegistry.GetModules<ModuleControlSurface>(vessel))
                        {
                            ctrl.authorityLimiter *= 10;
                            ctrl.ctrlSurfaceRange *= 10;
                        }
                        foreach (var turret in VesselModuleRegistry.GetModules<ModuleTurret>(vessel))
                        {
                            turret.yawSpeedDPS *= 100;
                            turret.pitchSpeedDPS *= 100;
                        }
                        break;
                    }
                case 4: //reactivate Weapons
                    {
                        foreach (var weapon in VesselModuleRegistry.GetModuleWeapons(vessel))
                        {
                            if (weapon.isAPS)
                                weapon.EnableWeapon(); //reactivate APS 
                            else
                                weapon.DisableWeapon(); //reset WeaponState
                        }
                        foreach (var missile in VesselModuleRegistry.GetMissileBases(vessel))
                        {
                            missile.engageRangeMax *= 10000;
                        }
                        break;
                    }
                case 6: //reactivate control
                    {
                        foreach (var command in VesselModuleRegistry.GetModuleCommands(vessel))
                        {
                            {
                                command.minimumCrew /= 10; //more elegant than a dict storing every crew part's cap to restore to original amount
                            }
                        }
                        foreach (var AI in VesselModuleRegistry.GetIBDAIControls(vessel))
                            if (initialAIState)
                            {
                                AI.ActivatePilot(); //It's Alive!
                                initialAIState = false;
                            }
                        foreach (var WM in VesselModuleRegistry.GetMissileFires(vessel))
                            if (initialWMState)
                            {
                                WM.guardMode = true;
                                WM.debilitated = false;
                                initialWMState = false;
                            }
                        break;
                    }
            }
            disabled = false;
        }
    }

    internal class EMPShock : MonoBehaviour
    {
        public void Start()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.AddParticleEmitter(pe);
                pe.emit = true;
                StartCoroutine(TimerRoutine());
            }
        }
        IEnumerator TimerRoutine()
        {
            yield return new WaitForSecondsFixed(5);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.RemoveParticleEmitter(pe);
            }

        }
    }
}
