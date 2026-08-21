using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using KSP.Localization;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons.Missiles;
using BDArmory.Competition;

namespace BDArmory.Radar
{
    public class ModuleExternalSensor : ModuleRadarSensorBase
    {
        #region KSPFields (Part Configuration)

        [KSPField]
        public float datalinkRange = 5000f;

        [KSPField]
        public bool detonateOnDisable = false;

        [KSPField]
        public bool retractOnDisable = false;

        [KSPField]
        public bool requireDirectConnection = false;

        [KSPField]
        public float deployDelay = -1f;

        [KSPField]
        public bool deployAltitudeTrigger = false;

        [KSPField]
        public float deployAltitude = -1f;

        [KSPField]
        public bool deployWhenLanded = false;

        #endregion KSPFields (Part Configuration)

        public override bool CanLock
        {
            get 
            { 
                return false;
            } 
        }

        ModuleExternalSensor baseModule = null;

        public ModuleExternalSensor BaseModule
        {
            get
            {
                return baseModule;
            }
        }

        #region Persisted State in flight

        // Within range?
        protected bool[] linksActive;

        #endregion Persisted State in flight

        #region Part members

        //vessel
        private MissileLauncher mssl;

        public MissileLauncher Missile
        {
            get
            {
                return mssl;
            }
        }

        public BDTeam Team
        {
            get
            {
                return mssl ? mssl.Team : null;
            }
        }

        private MissileFire wpmr;

        public override MissileFire WeaponManager
        {
            get
            {
                if (!wpmr) GetWPMR();
                return wpmr;
            }
        }

        public void GetWPMR()
        {
            // If somehow the missile is gone the sensor *should* be dead...
            if (!mssl)
            {
                wpmr = null;
                return;
            }
            // Return FiredByWM
            if (mssl.FiredByWM)
            {
                wpmr = mssl.FiredByWM;
                return;
            }
            // If dead, return the first linkedToVessels
            if (linkedToVessels == null)
            {
                wpmr = null;
                return;
            }
            for (int i = 0; i < linkedToVessels.Count; i++)
            {
                if (linkedToVessels[i] != null)
                {
                    wpmr = linkedToVessels[i].weaponManager;
                    return;
                }
            }
            wpmr = null;
            return;
        }

        #endregion Part members

        float deployTime = 0f;

        public void ArmSensor()
        {
            deployTime = Time.time + deployDelay;
            StartCoroutine(SensorActivationCoroutine());
        }

        IEnumerator SensorActivationCoroutine()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (true)
            {
                if (Time.time > deployTime && (!deployAltitudeTrigger || vessel.altitude < deployAltitude) && (!deployWhenLanded || (vessel.altitude < 1 || vessel.LandedOrSplashed)))
                {
                    EnableSensor();
                    yield break;
                }
                yield return wait;
            }
        }

        public override void EnableSensor()
        {
            sensorEnabled = true;

            linkedToVessels = BDATargetManager.RegisterExternalSensor(this);
            linksActive = new bool[linkedToVessels.Count];

            Deploy(true);
        }

        public override void DisableSensor()
        {
            sensorEnabled = false;

            List<VesselRadarData>.Enumerator vrd = linkedToVessels.GetEnumerator();
            while (vrd.MoveNext())
            {
                if (vrd.Current == null) continue;
                vrd.Current.RemoveDataFromRadar(this);
            }
            vrd.Dispose();
            //var weaponManager = WeaponManager;
            //using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
            //    while (loadedvessels.MoveNext())
            //    {
            //        BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
            //    }

            // Remove our link...
            linkedToVessels = null;
            BDATargetManager.RemoveExternalSensor(this);

            if (detonateOnDisable)
            {
                mssl.Detonate();
            }
            else
            {
                if (retractOnDisable) Deploy(false);
            }
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (sensorEnabled)
                {
                    DisableSensor();
                }

                referenceTransform = null;
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup(radarTransformName);

                mssl = part.FindModuleImplementing<MissileLauncher>();
                baseModule = part.partInfo.partPrefab.FindModuleImplementing<ModuleExternalSensor>();

                StartCoroutine(StartUpRoutine());
            }
        }

        /*
        void OnGoOnRails(Vessel v)
        {
            if (v != vessel) return;
            unlinkOnDestroy = false;
            //myVesselID = vessel.id.ToString();
        }
        */

        protected override void StartupRoutineActions()
        {
            if (sensorEnabled) EnableSensor();
        }

        protected override void EnabledUpdate()
        {
            if (canScan)
            {
                CheckLinks();
                Scan();
            }
        }

        protected override void PerformScan(float angleDelta)
        {
            RadarUtils.ExternalSensorScan(WeaponManager, currentAngle, sensorElOffset, angleDelta, sensorElFOV, this);
        }

        bool isConnected = false;

        public void CheckLinks()
        {
            // If < 0 we don't care about EITHER range or LoS
            if (datalinkRange < 0f)
            {
                isConnected = true;
                return;
            }
            isConnected = false;
            Vector3 adjustedPos = vessel.LandedOrSplashed ? (vessel.CoM + vessel.up * ((vessel.mainBody.ocean && vessel.altitude < 0f) ? (5f - vessel.altitude) : 5f)) : vessel.CoM;
            for (int i = 0; i < linkedToVessels.Count; i++)
            {
                Vessel currVessel = linkedToVessels[i].vessel;
                Vector3 currPos = currVessel.CoM;
                // If range == 0, we don't care about range, only LoS
                if (datalinkRange > 0f && (currPos - vessel.CoM).sqrMagnitude > datalinkRange * datalinkRange)
                {
                    linksActive[i] = false;
                    continue;
                }
                // LoS must not be blocked
                if (!RadarUtils.TerrainCheck(adjustedPos, currPos, vessel.mainBody))
                {
                    linksActive[i] = true;
                    isConnected = true;
                    if (!requireDirectConnection) break;
                }
                else
                {
                    linksActive[i] = false;
                }    
            }
        }

        public override void ReceiveContactData(TargetSignatureData contactData, bool _locked)
        {
            if (!isConnected) return;
            for (int i = 0; i < linkedToVessels.Count; i++)
            {
                if (requireDirectConnection && datalinkRange >= 0f && !linksActive[i]) continue;
                VesselRadarData currVRD = linkedToVessels[i];
                if (currVRD == null) continue;
                if (currVRD.canReceiveRadarData && currVRD.vessel != contactData.vessel)
                {
                    currVRD.AddRadarContact(this, contactData, _locked, true);
                }
            }
        }

        public void CheckLinkArraySize()
        {
            // Check array size...
            if (linksActive.Length >= linkedToVessels.Count) return;

            // Resize array...
            linksActive = new bool[linkedToVessels.Count];
            // Populate array if datalinkRange < 0f
            // Not technically necessary since we skip this check in the code if
            // datalinkRange < 0, but this will probably save a headache in case
            // that gets changed for some reason...
            if (datalinkRange < 0f)
            {
                for (int i = 0; i < linksActive.Length; i++)
                {
                    linksActive[i] = true;
                }
            }
        }

        protected override void LinkToVRD(VesselRadarData vrd)
        {
            BDATargetManager.LinkExternalSensorGroup(vrd, BaseModule);
        }

        protected override void AddSensorToVRD()
        {
            LinkToVRD(vesselRadarData);
        }

        protected override void RemoveSensorFromVRD() { }

        // RMB info in editor
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000008", (omnidirectional ? StringUtils.Localize("#autoLOC_bda_1000019") : StringUtils.Localize("#autoLOC_bda_1000020"))));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000021", resourceDrain));

            // For some reason just doing this in OnStart(), even outside of the Flight scene check wasn't working...
            SetRadarLimits();

            if (!omnidirectional)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", sensorAzLimits[0], sensorAzLimits[1]));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000041", sensorElLimits[0], sensorElLimits[1]));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000023", getRWRType(rwrThreatType)));

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000024"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000025", canScan));

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000030"));

            if (canScan)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000031", radarDetectionCurve.Evaluate(radarMaxDistanceDetect), radarMaxDistanceDetect));
            else
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000032"));

            // Cannot lock...
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000034"));

            if (sonarType == 1)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000039"));
            if (sonarType == 2)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000040"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000035", radarGroundClutterFactor));

            return output.ToString();
        }
    }

}
