using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static BDArmory.Radar.ModuleRadar;
using static UnityEngine.GraphicsBuffer;

namespace BDArmory.Radar
{
    public abstract class ModuleSensor : PartModule
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        public string radarName;

        [KSPField]
        public string rotationTransformName = string.Empty;
        protected Transform rotationTransform;

        [KSPField]
        public string radarTransformName = string.Empty;
        protected Transform radarTransform;

        #endregion General Configuration

        #region Radar Capabilities

        [KSPField]
        public int rwrThreatType = 0;               //IMPORTANT, configures which type of radar it will show up as on the RWR
        public RadarWarningReceiver.RWRThreatTypes rwrType = RadarWarningReceiver.RWRThreatTypes.SAM;

        [KSPField]
        public double resourceDrain = 0.825;        //resource (EC/sec) usage of active radar

        [KSPField]
        public string resourceName = "ElectricCharge";

        protected int resourceID;

        [KSPField]
        public bool omnidirectional = true;			//false=scan FoV limited to directionalFieldOfView

        // NOTE: The radar is assumed to have full roll stabilization capabilities!
        [KSPField]
        public string directionalFieldOfView = "90";   //relevant for NON-omnidirectional only

        [KSPField]
        public string elevationFOV = "-1";             //FoV of the radar in the vertical axis

        public float radarAzOffset = 0f;
        public float radarAzFOV = 90f;
        public float[] radarAzLimits = [-45f, 45f];
        public float radarElOffset = 0f;
        public float radarElFOV = 90f;
        public float[] radarElLimits = [-45f, 45f];

        public float[] radarMinMaxAzLimits = [-45f, 45f];
        public float[] radarMinMaxElLimits = [-45f, 45f];

        [KSPField]
        public float scanRotationSpeed = 120; 		//in degrees per second, relevant for omni and directional

        [KSPField]
        public bool showDirectionWhileScan = false; //radar can show direction indicator of contacts (false: can show contacts as blocks only)

        [KSPField]
        protected bool canScan = true;                 //radar has detection capabilities

        public bool CanScan
        { 
            get 
            {
                return canScan;
            } 
        }

        public abstract bool CanLock { get; }

        [KSPField]
        public FloatCurve radarDetectionCurve = new FloatCurve();		//FloatCurve defining at what range which RCS size can be detected

        [KSPField]
        public FloatCurve radarVelocityGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a doppler gate

        [KSPField]
        public FloatCurve radarRangeGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a range gate

        [KSPField]
        public bool radarCanNotch = true;

        [KSPField]
        public float radarGroundClutterFactor = 0.25f; //Factor defining how effective the radar is for look-down, compensating for ground clutter (0=ineffective, 1=fully effective)
                                                       //default to 0.25, so all cross sections of landed/splashed/submerged vessels are reduced to 1/4th, as these vessel usually a quite large
        [KSPField]
        public float radarChaffClutterFactor = 1.0f;     //Factor defining how effective the radar is at compensating for enemy chaff (0 = ineffective, 1 = no decrease in signal position/strength)
                                                         //default to 1, since that's legacy behavior. Relevant for guiding SARH ordnance. Allows up to two values for modifying chaff and notchMod.

        [KSPField]
        public string radarChaffNotchClutterFactor = "1.0";

        public float _radarChaffNotchVFac;
        public float _radarChaffNotchRFac;

        [KSPField]
        public FloatCurve radarGlintCurve = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a range gate

        [KSPField]
        public float radarGlintMult = -1f;

        [KSPField]
        public int sonarType = 0; //0 = Radar; 1 == Active Sonar; 2 == Passive Sonar

        public ModuleRadar.SonarModes sonarMode = ModuleRadar.SonarModes.None;

        //animation
        [KSPField] public string deployAnimationName;
        AnimationState deployAnimState;
        public bool hasDeployAnimation;
        [KSPField] public float deployAnimationSpeed = 1;
        [KSPField] public bool deployRotationBlock = false;

        public bool isDeployed()
        {
            return !hasDeployAnimation || deployAnimState.normalizedTime > 0.99;
        }

        bool editorDeployed;
        Coroutine deployAnimRoutine;

        #endregion Radar Capabilities

        #region Persisted State in flight

        [KSPField(isPersistant = true)]
        public bool radarEnabled;

        [KSPField(isPersistant = true)]
        public int rangeIndex = 99;

        [KSPField(isPersistant = true)]
        public float currentAngle;

        private float ReferenceUpdateTime = -1f;

        // Variables to pre-calculate transform directions
        public Vector3 currPosition;
        public Vector3 currForward;
        public Vector3 currUp;
        public Vector3 currRight;

        #endregion Persisted State in flight

        #endregion KSPFields (Part Configuration)

        #region Part members

        public float radarMinDistanceDetect
        {
            get { return radarDetectionCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Detection Range")]
        public float radarMaxDistanceDetect
        {
            get { return radarDetectionCurve.maxTime; }
        }

        public float radarMaxRangeGate
        {
            get { return radarRangeGate.maxTime; }
        }
        public float radarMinRangeGate
        {
            get { return radarRangeGate.minTime; }
        }

        public float radarMaxVelocityGate
        {
            get { return radarVelocityGate.maxTime; }
        }

        public float radarMinVelocityGate
        {
            get { return radarVelocityGate.minTime; }
        }

        //linked vessels
        protected List<VesselRadarData> linkedToVessels;
        public int linkedVRDs
        {
            get { return linkedToVessels.Count; }
        }
        //public List<ModuleRadar> availableRadarLinks;
        protected bool unlinkOnDestroy = true;

        //GUI
        public float signalPersistTime;
        public float signalPersistTimeForRwr;

        //scanning
        protected float currentAngleLock;
        public Transform referenceTransform;
        protected float radialScanDirection = 1;

        protected string myVesselID;

        // part state
        protected bool startupComplete;
        public float leftLimit;
        public float rightLimit;
        protected int snapshotTicker;

        //vessel
        public abstract MissileFire WeaponManager { get; }

        #endregion Part members

        public abstract void EnableRadar();
        public abstract void DisableRadar();

        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
        }

        protected void AnimSetup()
        {
            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                hasDeployAnimation = true;
                deployAnimState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
            }
        }

        protected void FlightSetup()
        {
            myVesselID = vessel.id.ToString();
            RadarUtils.SetupResources();

            if (string.IsNullOrEmpty(radarName))
            {
                radarName = part.partInfo.title;
            }

            SetRadarLimits();
            SetNotchChaffFac();

            signalPersistTime = omnidirectional
                ? 360 / (scanRotationSpeed + 5)
                : radarAzFOV / (scanRotationSpeed + 5);

            rwrType = (RadarWarningReceiver.RWRThreatTypes)rwrThreatType;
            sonarMode = (SonarModes)sonarType;
            if (rwrType == RadarWarningReceiver.RWRThreatTypes.Sonar)
                signalPersistTimeForRwr = RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME;
            else
            {
                signalPersistTimeForRwr = signalPersistTime / 2;
            }

            if (rotationTransformName != string.Empty)
            {
                rotationTransform = part.FindModelTransform(rotationTransformName);
            }
            radarTransform = radarTransformName != string.Empty ? part.FindModelTransform(radarTransformName) : part.transform;

            referenceTransform = (new GameObject()).transform;
            referenceTransform.parent = radarTransform;
            referenceTransform.localPosition = Vector3.zero;
        }

        protected void SetNotchChaffFac()
        {
            string[] chaffStrings = radarChaffNotchClutterFactor.Split([',']);
            if (chaffStrings.Length == 0)
            {
                _radarChaffNotchVFac = 1.0f;
                _radarChaffNotchRFac = 1.0f;
                return;
            }

            if (float.TryParse(chaffStrings[0], out float temp))
            {
                _radarChaffNotchVFac = temp;
            }
            else
            {
                _radarChaffNotchVFac = 1.0f;
            }

            if (chaffStrings.Length > 1 && float.TryParse(chaffStrings[1], out temp))
            {
                _radarChaffNotchRFac = temp;
            }
            else
            {
                _radarChaffNotchRFac = _radarChaffNotchVFac;
            }
        }

        protected void SetRadarLimits()
        {
            ParseRadarLimits(directionalFieldOfView, out radarAzOffset, out radarAzFOV, out radarAzLimits, out radarMinMaxAzLimits);
            // Retain old radar characteristics, if omnidirectional the radar should be able to see targets at +/- 90, otherwise
            // the radar could previously see targets at +/- 90 but not lock them, so we'll just lock it to a square FoV
            ParseRadarLimits(elevationFOV, out radarElOffset, out radarElFOV, out radarElLimits, out radarMinMaxElLimits, true);
            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.ModuleRadar] radarAzOffset {radarAzOffset}, radarAzFOV: {radarAzFOV}, radarAzLimits: {radarAzLimits[0]},{radarAzLimits[1]}, radarMinMaxAzLimits: {radarMinMaxAzLimits[0]},{radarMinMaxAzLimits[1]}");
                Debug.Log($"[BDArmory.ModuleRadar] radarElOffset {radarElOffset}, radarElFOV: {radarElFOV}, radarElLimits: {radarElLimits[0]},{radarElLimits[1]}, radarMinMaxAzLimits: {radarMinMaxElLimits[0]},{radarMinMaxElLimits[1]}");
            }
        }

        void ParseRadarLimits(in string radarLimitString, out float radarOffset, out float radarFOV, out float[] radarLimits, out float[] radarMinMaxLimits, bool elevationLimits = false)
        {
            // If we're parsing elevation limits
            if (elevationLimits)
            {
                // Then consider if it's omnidirectional or not, by default, omni radars are allowed +/- 90° FoV
                // Otherwise the default is a square radar scan area (based on radarAZLimits)
                radarLimits = omnidirectional ? [-90f, 90f] : [radarAzLimits[0], radarAzLimits[1]];
                //radarMinMaxLimits = omnidirectional ? [90f, 90f] : [radarMinMaxAzLimits[0], radarMinMaxAzLimits[1]];
                // Even if the azimuth is offset, we should start with no offset for elevation
                radarMinMaxLimits = omnidirectional ? [90f, 90f] : [0.5f * radarAzFOV, 0.5f * radarAzFOV];
                // Default omnidirectional FoV is 180°
                radarFOV = omnidirectional ? 180f : radarAzFOV;
            }
            else
            {
                // If we're not parsing elevation limits, then default to a +/- 45° FoV
                radarLimits = [-45f, 45f];
                radarMinMaxLimits = [45f, 45f];
                radarFOV = 90f;
            }
            
            // For both az/el the dfault is 0 offset
            radarOffset = 0f;
            
            string[] limitStrings = radarLimitString.Split([',']);
            if (limitStrings.Length > 0)
            {
                // If we're setting a left/right limit
                if (limitStrings.Length > 1)
                {
                    float tempLim = -45f;
                    // Get first limit
                    if (float.TryParse(limitStrings[0], out float temp))
                        tempLim = temp;
                    // Get second limit
                    if (float.TryParse(limitStrings[1], out temp))
                    {
                        // Test which limit should be which
                        if (tempLim < temp)
                        {
                            radarLimits[0] = tempLim;
                            radarLimits[1] = temp;
                        }
                        else
                        {
                            radarLimits[0] = temp;
                            radarLimits[1] = tempLim;
                        }
                    }

                    radarMinMaxLimits[0] = Mathf.Min(Mathf.Abs(radarLimits[0]), Mathf.Abs(radarLimits[1]));
                    radarMinMaxLimits[1] = Mathf.Max(Mathf.Abs(radarLimits[0]), Mathf.Abs(radarLimits[1]));

                    // Set the offset
                    radarOffset = (radarLimits[1] + radarLimits[0]) * 0.5f;
                    // Set the total width
                    radarFOV = radarLimits[1] - radarLimits[0];
                }
                else
                {
                    // Set total width
                    if (float.TryParse(limitStrings[0], out float temp))
                    {
                        if (temp < 0f)
                            return;
                        radarFOV = temp;
                    }

                    // Set left/right limits
                    radarLimits[1] = 0.5f * radarFOV;
                    radarLimits[0] = -radarLimits[1];

                    radarMinMaxLimits[0] = radarLimits[1];
                    radarMinMaxLimits[1] = radarLimits[1];
                }
            }
        }

        public void UpdateReferenceTransform()
        {
            if (ReferenceUpdateTime >= Time.time)
                return;

            if (omnidirectional)
            {
                referenceTransform.position = part.transform.position;
                currPosition = referenceTransform.position;
                referenceTransform.rotation =
                    Quaternion.LookRotation(VectorUtils.GetNorthVector(currPosition, vessel.mainBody),
                        vessel.up);
            }
            else
            {
                referenceTransform.position = part.transform.position;
                currPosition = referenceTransform.position;
                // THIS IMPLEMENTS FULL ROLL STABILIZATION
                // We assume the radar can *always* roll such that the up direction is the projection of
                // the up vector onto the radarTransform up plane.
                referenceTransform.rotation = Quaternion.LookRotation(radarTransform.up,
                    vessel.up.ProjectOnPlanePreNormalized(radarTransform.up).normalized);
            }
            currForward = referenceTransform.forward;
            currUp = referenceTransform.up;
            currRight = referenceTransform.right;

            ReferenceUpdateTime = Time.time;
        }

        protected void Deploy(bool forward)
        {
            if (hasDeployAnimation)
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(forward));
            }
        }

        IEnumerator DeployAnimation(bool forward)
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;

            if (forward)
            {
                while (deployAnimState.normalizedTime < 1)
                {
                    deployAnimState.speed = deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 1;
            }
            else
            {
                deployAnimState.speed = 0;

                // This does force rotationTransform to be a ModuleSensor-level thing but it doesn't have to be set or used...
                if (deployRotationBlock && rotationTransform)
                {
                    // Need to account for both identity and its -1 counterpart...
                    yield return new WaitWhileFixed(() => (rotationTransform.localRotation != Quaternion.identity && rotationTransform.localRotation != new Quaternion(0f, 0f, 0f, -1f)));
                }

                while (deployAnimState.normalizedTime > 0)
                {
                    deployAnimState.speed = -deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 0;
            }

            deployAnimState.speed = 0;
        }

        protected abstract void Scan();

        /// <summary>
        /// Checks if targetPosition is within the radar's FoV limits
        /// </summary>
        /// <param name="targetPosition">World target position.</param>
        /// <returns>Boolean value, true if the target is within the radar's FoV limits.</returns>
        public bool CheckFOV(Vector3 targetPosition)
        {
            if (omnidirectional)
            {
                // Check elevation only, determine angle from the vertical axis
                return (Mathf.Abs(VectorUtils.GetElevation(targetPosition - currPosition, currUp) - radarElOffset) < 0.5f * radarElFOV);
            }
            else
            {
                // Target exists and omnidirectional, we must check if we're within radar FoV
                //VectorUtils.GetAzimuthElevation(targetPosition - currPosition, currForward, currUp, out float az, out float el);
                Vector3 relativePosition = targetPosition - currPosition;

                // Radar azimuth is reversed, for whatever reason
                float az = VectorUtils.GetAngleOnPlane(relativePosition, currForward, currRight);
                float el = VectorUtils.GetElevation(relativePosition, currUp);

                // Check if we're outside FoV
                return (Mathf.Abs(az - radarAzOffset) < 0.5f * radarAzFOV && Mathf.Abs(el - radarElOffset) < 0.5f * radarElFOV);
            }
        }

        /// <summary>
        /// Checks if the direction vector is within the radar's FoV limits
        /// </summary>
        /// <param name="dir">Target direction relative to radar (unit vector).</param>
        /// <returns>Boolean value, true if the target is within the radar's FoV limits.</returns>
        public bool CheckFOVDir(Vector3 dir)
        {
            if (omnidirectional)
            {
                // Check elevation only, determine angle from the vertical axis
                return (Mathf.Abs(VectorUtils.GetElevationPreNorm(dir, currUp) - radarElOffset) < 0.5f * radarElFOV);
            }
            else
            {
                // Target exists and omnidirectional, we must check if we're within radar FoV
                // Radar azimuth is reversed, for whatever reason
                float az = VectorUtils.GetAngleOnPlane(dir, currForward, currRight);
                float el = VectorUtils.GetElevationPreNorm(dir, currUp);

                // Check if we're outside FoV
                return (Mathf.Abs(az - radarAzOffset) < 0.5f * radarAzFOV && Mathf.Abs(el - radarElOffset) < 0.5f * radarElFOV);
            }
        }

        public abstract void ReceiveContactData(TargetSignatureData contactData, bool _locked);

        protected abstract void LinkToVRD(VesselRadarData vrd);

        public string getRWRType(int i)
        {
            switch (i)
            {
                case 0:
                    return StringUtils.Localize("#autoLOC_bda_1000002");		// #autoLOC_bda_1000002 = SAM

                case 1:
                    return StringUtils.Localize("#autoLOC_bda_1000003");		// #autoLOC_bda_1000003 = FIGHTER

                case 2:
                    return StringUtils.Localize("#autoLOC_bda_1000004");		// #autoLOC_bda_1000004 = AWACS

                case 3:
                case 4:
                    return StringUtils.Localize("#autoLOC_bda_1000005");		// #autoLOC_bda_1000005 = MISSILE

                case 5:
                    return StringUtils.Localize("#autoLOC_bda_1000006");		// #autoLOC_bda_1000006 = DETECTION

                case 6:
                    return StringUtils.Localize("#autoLOC_bda_1000017");		// #autoLOC_bda_1000017 = SONAR
            }
            return StringUtils.Localize("#autoLOC_bda_1000007");		// #autoLOC_bda_1000007 = UNKNOWN
            //{SAM = 0, Fighter = 1, AWACS = 2, MissileLaunch = 3, MissileLock = 4, Detection = 5, Sonar = 6}
        }

        protected void DrainElectricity(bool showMessage = true)
        {
            if (resourceDrain <= 0)
            {
                return;
            }

            double drainAmount = resourceDrain * TimeWarp.fixedDeltaTime;
            double chargeAvailable = part.RequestResource(resourceID, drainAmount, ResourceFlowMode.ALL_VESSEL);
            if (chargeAvailable < drainAmount * 0.95f)
            {
                if (showMessage)
                {
                    ScreenMessages.PostScreenMessage($"{part.partInfo.title} {StringUtils.Localize("#autoLOC_244332")} {PartResourceLibrary.Instance.GetDefinition(resourceName).displayName}", 5.0f, ScreenMessageStyle.UPPER_CENTER);		// [part Title] Requires [localized resource name]
                }
                DisableRadar();
            }
        }
    }
}
