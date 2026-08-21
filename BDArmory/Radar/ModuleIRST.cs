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

namespace BDArmory.Radar
{
    public class ModuleIRST : ModuleSensorBase
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        public string IRSTName;

        [KSPField]
        public int turretID = 0;

        [KSPField]
        public string rotationTransformName = string.Empty;
        Transform rotationTransform;

        [KSPField]
        public string irstTransformName = string.Empty;
        Transform irstTransform;

        public Vector3 irstForward
        {
            get { return sensorTransform.up; }
        }

        #endregion General Configuration

        #region Capabilities

        [KSPField]
        public double resourceDrain = 0.825;        //resource (EC/sec) usage of active irst

        [KSPField]
        public string resourceName = "ElectricCharge";

        private int resourceID;

        [KSPField]
        public bool omnidirectional = true;			//false=boresight only

        [KSPField]
        public float directionalFieldOfView = 90;	//relevant for omnidirectional only

        [KSPField]
        public float boresightFOV = 10;				//relevant for boresight only

        [KSPField]
        public float scanRotationSpeed = 120; 		//in degrees per second, relevant for omni and directional

        [KSPField]
        public bool showDirectionWhileScan = false; //irst can show direction indicator of contacts (false: can show contacts as blocks only)

        [KSPField]
        public bool canScan = true;                 //irst has detection capabilities

        [KSPField]
        public bool irstRanging = false;            //irst can get ranging info for target distance

        [KSPField]
        public FloatCurve DetectionCurve = new FloatCurve();		//FloatCurve setting default ranging capabilities of the IRST

        [KSPField]
        public FloatCurve TempSensitivityCurve = new FloatCurve();		//FloatCurve setting default IR spectrum capabilities of the IRST

        [KSPField]
        public FloatCurve atmAttenuationCurve = new FloatCurve();        //FloatCurve range increase/decrease based on atm density/temp, thinner/cooler air yields longer range returns


        [KSPField]
        public float GroundClutterFactor = 0.16f; //Factor defining how effective the irst is at detecting heatsigs against ambient ground temperature (0=ineffective, 1=fully effective)
                                                  //default to 0.16, IRSTs have about a 6th of the detection range for ground targets vs air targets.

        #endregion Capabilities

        #region Persisted State in flight

        [KSPField(isPersistant = true)]
        public string linkedVesselID;

        [Obsolete]
        [KSPField(isPersistant = true)]
        public bool irstEnabled;

        [KSPField(isPersistant = true)]
        public int rangeIndex = 99;

        [KSPField(isPersistant = true)]
        public float currentAngle = 0;

        #endregion Persisted State in flight

        #endregion KSPFields (Part Configuration)

        #region KSP Events & Actions

        [KSPAction("Toggle IRST")]
        public void AGEnable(KSPActionParam param)
        {
            if (sensorEnabled)
            {
                DisableSensor();
            }
            else
            {
                EnableSensor();
            }
        }

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleIRST")]//Toggle IRST - FIXME - Localize
        public void Toggle()
        {
            if (sensorEnabled)
            {
                DisableSensor();
            }
            else
            {
                EnableSensor();
            }
        }

        #endregion KSP Events & Actions

        #region Part members

        public float irstMinDistanceDetect
        {
            get { return DetectionCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Detection Range")]
        public float irstMaxDistanceDetect
        {
            get { return DetectionCurve.maxTime; }
        }

        //GUI
        private bool drawGUI;
        public float signalPersistTime;

        //scanning
        public Transform referenceTransform;
        private float radialScanDirection = 1;

        public bool boresightScan;

        //locking
        public bool slaveTurrets;
        public ModuleTurret lockingTurret;
        public bool lockingPitch = true;
        public bool lockingYaw = true;

        //vessel
        private MissileFire wpmr;

        public MissileFire WeaponManager
        {
            get
            {
                if (wpmr == null || !wpmr.IsPrimaryWM || wpmr.vessel != vessel)
                    wpmr = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return wpmr;
            }
        }

        public VesselRadarData vesselRadarData;
        private string myVesselID;

        // part state
        private bool startupComplete;
        public float leftLimit;
        public float rightLimit;

        #endregion Part members

        void UpdateToggleGuiName()
        {
            Events[nameof(Toggle)].guiName = sensorEnabled ? StringUtils.Localize("#autoLOC_bda_1000036") : StringUtils.Localize("#autoLOC_bda_1000037");		// fixme - fix localizations
        }
        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
        }

        protected override void AddSensorToVRD()
        {
            vesselRadarData.AddIRST(this);
        }

        protected override void RemoveSensorFromVRD()
        {
            vesselRadarData.RemoveIRST(this);
        }

        public override void EnableSensor()
        {
            sensorEnabled = true;
            EnsureVesselRadarData(true);

            UpdateToggleGuiName();
            //vesselRadarData.AddIRST(this);
            var weaponManager = WeaponManager;
            if (weaponManager != null)
            {
                weaponManager._irstsEnabled = true;
            }
        }

        public override void DisableSensor()
        {
            sensorEnabled = false;
            UpdateToggleGuiName();

            if (vesselRadarData)
            {
                vesselRadarData.RemoveIRST(this);
            }
            var weaponManager = WeaponManager;
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
                }
            if (weaponManager != null)
            {
                if (weaponManager.irsts.Count > 1)
                {
                    using (List<ModuleIRST>.Enumerator irst = weaponManager.irsts.GetEnumerator())
                        while (irst.MoveNext())
                        {
                            if (irst.Current == null) continue;
                            weaponManager._irstsEnabled = false;
                            if (irst.Current != this && irst.Current.sensorEnabled)
                            {
                                weaponManager._irstsEnabled = true;
                                break;
                            }
                        }
                }
                else weaponManager._irstsEnabled = false;
            }
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (vesselRadarData)
                {
                    vesselRadarData.RemoveIRST(this);
                    vesselRadarData.RemoveDataFromIRST(this);
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (irstEnabled)
            {
                sensorEnabled = true;
                irstEnabled = false;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup(irstTransformName);

                // fill TempSensitivityCurve with default values if not set by part config:
                if (TempSensitivityCurve.minTime == float.MaxValue)
                    TempSensitivityCurve.Add(0f, 1f);

                List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
                while (turr.MoveNext())
                {
                    if (turr.Current == null) continue;
                    if (turr.Current.turretID != turretID) continue;
                    lockingTurret = turr.Current;
                    break;
                }
                turr.Dispose();

                //GameEvents.onVesselGoOnRails.Add(OnGoOnRails);    //not needed
                EnsureVesselRadarData();
                StartCoroutine(StartUpRoutine());
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                //Editor only:
                List<ModuleTurret>.Enumerator tur = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
                while (tur.MoveNext())
                {
                    if (tur.Current == null) continue;
                    if (tur.Current.turretID != turretID) continue;
                    lockingTurret = tur.Current;
                    break;
                }
                tur.Dispose();
                if (lockingTurret)
                {
                    lockingTurret.Fields[nameof(lockingTurret.minPitch)].guiActiveEditor = false;
                    lockingTurret.Fields[nameof(lockingTurret.maxPitch)].guiActiveEditor = false;
                    lockingTurret.Fields[nameof(lockingTurret.yawRange)].guiActiveEditor = false;
                }
            }
        }

        protected override void StartupRoutineActions()
        {
            UpdateToggleGuiName();
        }

        void Update()
        {
            drawGUI = (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && sensorEnabled &&
                       vessel.isActiveVessel && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled);
        }

        protected override void EnabledUpdate()
        {
            if (boresightScan)
            {
                BoresightScan();
            }
            else if (canScan)
            {
                Scan();
            }
        }

        protected override void PerformScan(float angleDelta)
        {
            RadarUtils.IRSTUpdateScan(WeaponManager, currentAngle, sensorElOffset, angleDelta, sensorElFOV, this);
        }

        void BoresightScan()
        {
            currentAngle = Mathf.Lerp(currentAngle, 0, 0.08f);
            RadarUtils.IRSTUpdateScan(WeaponManager, currentAngle, referenceTransform, boresightFOV, referenceTransform.position, this);
        }

        public void ReceiveContactData(TargetSignatureData contactData, float _magnitude)
        {
            if (vesselRadarData)
            {
                vesselRadarData.AddIRSTContact(this, contactData, _magnitude);
            }
        }


        void OnGUI()
        {
            if (drawGUI)
            {
                if (boresightScan)
                {
                    GUIUtils.DrawTextureOnWorldPos(transform.position + (3500 * transform.up),
                        BDArmorySetup.Instance.dottedLargeGreenCircle, new Vector2(156, 156), 0);
                }
            }
        }

        // RMB info in editor
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000008", omnidirectional ? StringUtils.Localize("#autoLOC_bda_1000019") : StringUtils.Localize("#autoLOC_bda_1000020")));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000021", resourceDrain)); //Ec/sec

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", directionalFieldOfView)); //Field of View

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000024")); //Capabilities
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000025", canScan)); //-Scanning

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000030")); //Performance

            if (canScan)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000031", DetectionCurve.Evaluate(irstMaxDistanceDetect) - 273, irstMaxDistanceDetect)); //Detection x.xx deg C @ n km
            else
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000032"));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000034"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000035", GroundClutterFactor));


            return output.ToString();
        }
    }
}
