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
    public class ModuleExternalSensor : ModuleSensor
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

        // In case this is ever needed on an HSI or something
        private float DisplayUpdateTime = -1f;

        // Rotated forward vector according to azimuth and elevation
        // offsets for display purposes
        public Vector3 currDisplayForward;

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
                if (Time.time > deployTime && (!deployAltitudeTrigger || vessel.altitude < deployAltitude) && (!deployWhenLanded || vessel.LandedOrSplashed))
                {
                    EnableRadar();
                    yield break;
                }
                yield return wait;
            }
        }

        public override void EnableRadar()
        {
            radarEnabled = true;

            linkedToVessels = BDATargetManager.RegisterExternalSensor(this);
            linksActive = new bool[linkedToVessels.Count];

            Deploy(true);
        }

        public override void DisableRadar()
        {
            radarEnabled = false;

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
                if (radarEnabled)
                {
                    DisableRadar();
                }

                referenceTransform = null;
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup();

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

        IEnumerator StartUpRoutine()
        {
            if (BDArmorySettings.DEBUG_RADAR)
                Debug.Log("[BDArmory.ModuleRadar]: StartupRoutine: " + radarName + " enabled: " + radarEnabled);
            yield return new WaitWhile(() => !FlightGlobals.ready || vessel.packed || !vessel.loaded);
            yield return new WaitForFixedUpdate();

            // DISABLE RADAR
            /*
            if (radarEnabled)
            {
                EnableRadar();
            }
            */

            // This is for when loading in to a save with already deployed external sensors, to ensure they're already loaded in
            if (radarEnabled) EnableRadar();

            startupComplete = true;
        }

        public void UpdateDisplayTransform()
        {
            if (DisplayUpdateTime >= Time.time)
                return;
            UpdateReferenceTransform();

            if (radarElOffset != 0 || radarAzOffset != 0)
            {
                currDisplayForward = Quaternion.AngleAxis(radarElOffset, currRight) * Quaternion.AngleAxis(-radarAzOffset, currUp) * currForward;
            }
            else
            { 
                currDisplayForward = currForward; 
            }
            DisplayUpdateTime = Time.time;
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && startupComplete)
            {
                if (radarEnabled && isDeployed())
                {
                    UpdateReferenceTransform();

                    DrainElectricity(false); //physics behaviour, thus moved here from update

                    if (BDArmorySettings.DEBUG_RADAR)
                    {
                        Debug.Log($"[BDArmory.ModuleRadar] Vessel: {vessel.vesselName}, {(sonarMode == ModuleRadar.SonarModes.None ? "Radar" : "Sonar")}: {name}, beginning lock checks.");
                    }
                    
                    if (canScan)
                    {
                        CheckLinks();
                        Scan();
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && canScan)
            {
                UpdateModel();
            }
        }

        void UpdateModel()
        {
            //model rotation
            if (radarEnabled)
            {
                if (rotationTransform && canScan)
                {
                    Vector3 direction = Quaternion.AngleAxis(currentAngle, currUp) * currForward;

                    Vector3 localDirection = rotationTransform.parent.InverseTransformDirection(direction).ProjectOnPlanePreNormalized(Vector3.up);
                    if (localDirection != Vector3.zero)
                    {
                        rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation,
                            Quaternion.LookRotation(localDirection, Vector3.up), 10 * TimeWarp.fixedDeltaTime);
                    }
                }
            }
            else
            {
                if (rotationTransform)
                {
                    rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation,
                        Quaternion.identity, 5 * TimeWarp.fixedDeltaTime);
                }
            }
        }

        protected override void Scan()
        {
            float angleDelta = scanRotationSpeed * Time.fixedDeltaTime;
            RadarUtils.ExternalSensorScan(WeaponManager, currentAngle, radarElOffset, angleDelta, radarElFOV, this);

            if (omnidirectional)
            {
                currentAngle = Mathf.Repeat(currentAngle + angleDelta, 360f);
            }
            else
            {
                currentAngle += radialScanDirection * angleDelta;

                // If we're beyond the radar limits
                if (Mathf.Abs(currentAngle - radarAzOffset) > radarAzFOV * 0.5f)
                {
                    // Set current angle to either the left/right limit
                    currentAngle = currentAngle < 0f ? radarAzLimits[0] : radarAzLimits[1];
                    // Reverse the scan direction
                    radialScanDirection = -radialScanDirection;
                }
            }
        }

        bool isConnected = false;

        public void CheckLinks()
        {
            // If < 0 we don't care about EITHER range or LoS
            if (datalinkRange < 0f) return;
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
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", radarAzLimits[0], radarAzLimits[1]));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000041", radarElLimits[0], radarElLimits[1]));
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
