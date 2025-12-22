using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Extensions;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;

namespace BDArmory.Radar
{
    public class RadarWarningReceiver : PartModule
    {
        public delegate void RadarPing(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource);

        public static event RadarPing OnRadarPing;

        public delegate void MissileLaunchWarning(Vector3 source, Vector3 direction, bool radar, Vessel vSource);

        public static event MissileLaunchWarning OnMissileLaunch;

        public enum RWRThreatTypes
        {
            None = -1,
            SAM = 0,
            Fighter = 1,
            AWACS = 2,
            MissileLaunch = 3,
            MissileLock = 4,
            Detection = 5,
            Sonar = 6,
            Torpedo = 7,
            TorpedoLock = 8,
            Jamming = 9,
            MWS = 10,
        }

        string[] iconLabels = new string[] { "S", "F", "A", "M", "M", "D", "So", "T", "T", "J" };

        // This field may not need to be persistent.  It was combining display with active RWR status.
        [KSPField(isPersistant = true)] public bool rwrEnabled;
        //for if the RWR should detect everything, or only be able to detect radar sources
        [KSPField(isPersistant = true)] public bool omniDetection = true;

        [KSPField] public float fieldOfView = 360; //for if making separate RWR and WM for mod competitions, etc.

        [KSPField] public float RWRMWSRange = 20000; //range of the MWS in m
        [KSPField] public float RWRMWSUpdateRate = 0.5f; //interval in s between MWS updates,
        //only here for performance and spam reasons, human pilot won't need a super high
        //update rate and we don't want the warning sound to be played at every frame
        
        public bool performMWSCheck = true;
        public float TimeOfLastMWSUpdate = -1f;
        private TargetSignatureData[] MWSData;
        private int MWSSlots = 0;

        public bool displayRWR = false; // This field was added to separate RWR active status from the display of the RWR.  the RWR should be running all the time...
        internal static bool resizingWindow = false;

        public Rect RWRresizeRect = new Rect(
            BDArmorySetup.WindowRectRwr.width - (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            BDArmorySetup.WindowRectRwr.height - (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            (16 * BDArmorySettings.RWR_WINDOW_SCALE));

        public static Texture2D rwrDiamondTexture =
            GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "rwrDiamond", false);

        public static Texture2D rwrMissileTexture =
            GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "rwrMissileIcon", false);

        public static AudioClip radarPingSound;
        public static AudioClip missileLockSound;
        public static AudioClip missileLaunchSound;
        public static AudioClip sonarPing;
        public static AudioClip torpedoPing;
        private float torpedoPingPitch;
        private float audioSourceRepeatDelay;
        private const float audioSourceRepeatDelayTime = 0.5f;

        //float lastTimePinged = 0;
        const float minPingInterval = 0.12f;
        const float pingPersistTime = 1;

        const int dataCount = 12;

        internal float rwrDisplayRange = BDArmorySettings.MAX_ACTIVE_RADAR_RANGE;
        internal static float RwrSize = 256;
        internal static float BorderSize = 10;
        internal static float HeaderSize = 15;

        public TargetSignatureData[] pingsData;
        //public Vector3[] pingWorldPositions;
        List<TargetSignatureData> launchWarnings;

        private float ReferenceUpdateTime = -1f;
        public float TimeSinceReferenceUpdate => Time.fixedTime - ReferenceUpdateTime;

        Transform rt;

        Transform referenceTransform
        {
            get
            {
                if (!rt)
                {
                    rt = new GameObject().transform;
                    rt.parent = part.transform;
                    rt.localPosition = Vector3.zero;
                }
                return rt;
            }
        }

        internal static Rect RwrDisplayRect = new Rect(0, 0, RwrSize * BDArmorySettings.RWR_WINDOW_SCALE, RwrSize * BDArmorySettings.RWR_WINDOW_SCALE);

        GUIStyle rwrIconLabelStyle;

        AudioSource audioSource;
        public static bool WindowRectRWRInitialized;

        public override void OnAwake()
        {
            radarPingSound = SoundUtils.GetAudioClip("BDArmory/Sounds/rwrPing");
            missileLockSound = SoundUtils.GetAudioClip("BDArmory/Sounds/rwrMissileLock");
            missileLaunchSound = SoundUtils.GetAudioClip("BDArmory/Sounds/mLaunchWarning");
            sonarPing = SoundUtils.GetAudioClip("BDArmory/Sounds/rwr_sonarping");
            torpedoPing = SoundUtils.GetAudioClip("BDArmory/Sounds/rwr_torpedoping");
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                pingsData = new TargetSignatureData[dataCount];
                MWSData = new TargetSignatureData[dataCount];
                //pingWorldPositions = new Vector3[dataCount];
                TargetSignatureData.ResetTSDArray(ref pingsData);
                launchWarnings = new List<TargetSignatureData>();

                rwrIconLabelStyle = new GUIStyle();
                rwrIconLabelStyle.alignment = TextAnchor.MiddleCenter;
                rwrIconLabelStyle.normal.textColor = Color.green;
                rwrIconLabelStyle.fontSize = 12;
                rwrIconLabelStyle.border = new RectOffset(0, 0, 0, 0);
                rwrIconLabelStyle.clipping = TextClipping.Overflow;
                rwrIconLabelStyle.wordWrap = false;
                rwrIconLabelStyle.fontStyle = FontStyle.Bold;

                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.minDistance = 500;
                audioSource.maxDistance = 1000;
                audioSource.spatialBlend = 1;
                audioSource.dopplerLevel = 0;
                audioSource.loop = false;

                UpdateVolume();
                BDArmorySetup.OnVolumeChange += UpdateVolume;

                if (!WindowRectRWRInitialized)
                {
                    BDArmorySetup.WindowRectRwr = new Rect(BDArmorySetup.WindowRectRwr.x, BDArmorySetup.WindowRectRwr.y, RwrDisplayRect.height + BorderSize, RwrDisplayRect.height + BorderSize + HeaderSize);
                    // BDArmorySetup.WindowRectRwr = new Rect(40, Screen.height - RwrDisplayRect.height, RwrDisplayRect.height + BorderSize, RwrDisplayRect.height + BorderSize + HeaderSize);
                    WindowRectRWRInitialized = true;
                }

                using (var mf = VesselModuleRegistry.GetMissileFires(vessel).GetEnumerator())
                    while (mf.MoveNext())
                    {
                        if (mf.Current == null) continue;
                        mf.Current.rwr = this; // Set the rwr on all weapon managers to this.
                    }
                //if (rwrEnabled) EnableRWR();
                EnableRWR();
            }
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_UI_VOLUME;
            }
        }

        public void UpdateReferenceTransform()
        {
            if (TimeSinceReferenceUpdate < Time.fixedDeltaTime)
                return;

            Vector3 upVec = VectorUtils.GetUpDirection(transform.position);

            referenceTransform.rotation = Quaternion.LookRotation(vessel.ReferenceTransform.up.ProjectOnPlanePreNormalized(upVec), upVec);

            ReferenceUpdateTime = Time.fixedTime;
        }

        public void EnableRWR()
        {
            OnRadarPing += ReceivePing;
            OnMissileLaunch += ReceiveLaunchWarning;
            rwrEnabled = true;
        }

        public void DisableRWR()
        {
            OnRadarPing -= ReceivePing;
            OnMissileLaunch -= ReceiveLaunchWarning;
            rwrEnabled = false;
        }

        void OnDestroy()
        {
            OnRadarPing -= ReceivePing;
            OnMissileLaunch -= ReceiveLaunchWarning;
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)) return;

            if (!omniDetection || !rwrEnabled || !performMWSCheck || (Time.fixedTime - TimeOfLastMWSUpdate < RWRMWSUpdateRate)) return;

            MWSSlots = 0;

            TimeOfLastMWSUpdate = Time.fixedTime;

            float sqrDist = float.PositiveInfinity;

            UpdateReferenceTransform();

            for (int i = 0; i < BDATargetManager.FiredMissiles.Count; i++)
            {
                MissileBase currMissile = BDATargetManager.FiredMissiles[i] as MissileBase;

                if (PerformMWSCheck(currMissile, out float currSqrDist) && sqrDist < currSqrDist)
                {
                    sqrDist = currSqrDist;
                }
            }

            if (!float.IsPositiveInfinity(sqrDist))
            {
                PlayWarningSound(RWRThreatTypes.MWS, sqrDist);
            }
        }
        
        public void ResetMWSSlots()
        {
            MWSSlots = 0;
        }

        public bool PerformMWSCheck(MissileBase currMissile, out float currSqrDist, bool addTarget = true)
        {
            currSqrDist = -1f;

            // No nulls and no torps!
            if (currMissile == null || currMissile.vessel == null || currMissile.SourceVessel == vessel || currMissile.GetWeaponClass() == WeaponClasses.SLW) return false;

            float currRange = RWRMWSRange;

            if (BDArmorySettings.VARIABLE_MISSILE_VISIBILITY) // assume same detectability logic as visual detection, does mean the MWS is implied to be IR based
            {
                currRange *= (currMissile.MissileState == MissileBase.MissileStates.Boost ? 1 : (currMissile.MissileState == MissileBase.MissileStates.Cruise ? 0.75f : 0.33f));
            }

            Vector3 relativePos = vessel.CoM - currMissile.vessel.CoM;

            currSqrDist = relativePos.sqrMagnitude;

            // Are we out of range?
            if (currRange * currRange < currSqrDist) return false;

            // Is the missile facing us?
            if (Vector3.Dot(relativePos, currMissile.GetForwardTransform()) < 0) return false;

            if (!addTarget) return true;

            if (MWSSlots < MWSData.Length)
            {
                Vector2 currPos = RadarUtils.WorldToRadar(currMissile.vessel.CoM, referenceTransform, RwrDisplayRect, rwrDisplayRange);
                MWSData[MWSSlots] = new TargetSignatureData(currMissile.vessel.CoM, currPos, true, RWRThreatTypes.MWS, currMissile.vessel);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                ++MWSSlots;
            }

            /*int openIndex = -1;
            bool foundPing = false;
            Vector2 currPos = RadarUtils.WorldToRadar(currMissile.vessel.CoM, referenceTransform, RwrDisplayRect, rwrDisplayRange);
            for (int i = 0; i < pingsData.Length; i++)
            {
                TargetSignatureData tempPing = pingsData[i];
                if (!tempPing.exists)
                {
                    // as soon as we have an open index, break
                    openIndex = i;
                    break;
                }

                // Consider swapping this to a vessel check, since we know the vessel anyways.
                if ((tempPing.pingPosition - currPos).sqrMagnitude < (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY ? 100f : 900f))    //prevent ping spam
                {
                    foundPing = true;
                    break;
                }
            }

            if (openIndex >= 0)
            {
                pingsData[openIndex] = new TargetSignatureData(currMissile.vessel.CoM, currPos, true, RWRThreatTypes.MWS, currMissile.vessel);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                StartCoroutine(PingLifeRoutine(openIndex, RWRMWSUpdateRate));

                return true;
            }*/

            return true;
        }

        public bool IsVesselDetected(Vessel v)
        {
            for (int i = 0; i < pingsData.Length; i++)
            {
                // Should account for the noTarget values as well as those have vessel == null
                if (pingsData[i].vessel == v) return true;
            }

            return false;
        }

        IEnumerator PingLifeRoutine(int index, float lifeTime)
        {
            yield return new WaitForSecondsFixed(Mathf.Clamp(lifeTime - 0.04f, minPingInterval, lifeTime));
            pingsData[index] = TargetSignatureData.noTarget;
        }

        IEnumerator LaunchWarningRoutine(TargetSignatureData data)
        {
            launchWarnings.Add(data);
            yield return new WaitForSecondsFixed(2);
            launchWarnings.Remove(data);
        }

        void ReceiveLaunchWarning(Vector3 source, Vector3 direction, bool radar, Vessel vSource)
        {
            if (referenceTransform == null) return;
            if (part == null || !part.isActiveAndEnabled) return;
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager == null) return;
            if (!omniDetection && !radar) return;

            UpdateReferenceTransform();

            Vector3 currPos = part.transform.position;
            float sqrDist = (currPos - source).sqrMagnitude;
            //if ((weaponManager && weaponManager.guardMode) && (sqrDist > (weaponManager.guardRange * weaponManager.guardRange))) return; //doesn't this clamp the RWR to visual view range, not radar/RWR range?
            if ((radar || sqrDist < RWRMWSRange * RWRMWSRange) && sqrDist > 10000f && VectorUtils.Angle(direction, currPos - source) < 15f)
            {
                StartCoroutine(
                    LaunchWarningRoutine(new TargetSignatureData(source,
                        RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange),
                        true, RWRThreatTypes.MissileLaunch, vSource)));
                PlayWarningSound(RWRThreatTypes.MissileLaunch);

                if (weaponManager.guardMode)
                {
                    //weaponManager.FireAllCountermeasures(Random.Range(1, 2)); // Was 2-4, but we don't want to take too long doing this initial dump before other routines kick in
                    weaponManager.incomingThreatPosition = source;
                    weaponManager.missileIsIncoming = true;
                }
            }
        }

        void ReceivePing(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            if (v == null || v.packed || !v.loaded || !v.isActiveAndEnabled || v != vessel) return;
            if (referenceTransform == null) return;
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager == null) return;
            if (!rwrEnabled) return;

            //if we are airborne or on land, no Sonar or SLW type weapons on the RWR!
            if ((type == RWRThreatTypes.Torpedo || type == RWRThreatTypes.TorpedoLock || type == RWRThreatTypes.Sonar) && (vessel.situation != Vessel.Situations.SPLASHED))
            {
                // rwr stays silent...
                return;
            }

            UpdateReferenceTransform();

            if (type == RWRThreatTypes.MissileLaunch || type == RWRThreatTypes.Torpedo)
            {
                StartCoroutine(
                    LaunchWarningRoutine(new TargetSignatureData(source,
                        RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange),
                        true, type, vSource)));
                PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);
                return;
            }
            else if (type == RWRThreatTypes.MissileLock)
            {
                if (weaponManager.guardMode)
                {
                    weaponManager.FireChaff();
                    weaponManager.missileIsIncoming = true;
                    // TODO: if torpedo inbound, also fire accoustic decoys (not yet implemented...)
                }
            }

            int openIndex = -1;
            Vector2 currPos = RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange);
            for (int i = 0; i < pingsData.Length; i++)
            {
                TargetSignatureData tempPing = pingsData[i];

                if (!tempPing.exists)
                {
                    // as soon as we have an open index, break
                    openIndex = i;
                    break;
                }
                
                // Consider swapping this to a vessel check, since we know the vessel anyways.
                if (tempPing.exists && 
                    (tempPing.pingPosition - currPos).sqrMagnitude < (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY ? 100f : 900f))    //prevent ping spam
                    break;
            }

            if (openIndex >= 0)
            {
                pingsData[openIndex] = new TargetSignatureData(source, currPos, true, type, vSource);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                if (weaponManager.hasAntiRadiationOrdnance)
                {
                    BDATargetManager.ReportVessel(AIUtils.VesselClosestTo(source), weaponManager); // Report RWR ping as target for anti-rads
                } //MissileFire RWR-vessel checks are all (RWR ping position - guardtarget.CoM).Magnitude < 20*20?, could we simplify the more complex vessel aquistion function used here?
                StartCoroutine(PingLifeRoutine(openIndex, persistTime));

                PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);
            }
        }

        public void PlayWarningSound(RWRThreatTypes type, float sqrDistance = 0f)
        {
            if (vessel.isActiveVessel && audioSourceRepeatDelay <= 0f)
            {
                switch (type)
                {
                    case RWRThreatTypes.MissileLaunch:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = missileLaunchSound;
                        audioSource.Play();
                        break;

                    case RWRThreatTypes.Sonar:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = sonarPing;
                        audioSource.Play();
                        break;

                    case RWRThreatTypes.Torpedo:
                    case RWRThreatTypes.TorpedoLock:
                        if (audioSource.isPlaying)
                            break;
                        torpedoPingPitch = Mathf.Lerp(1.5f, 1.0f, sqrDistance / (2000 * 2000)); //within 2km increase ping pitch
                        audioSource.Stop();
                        audioSource.clip = torpedoPing;
                        audioSource.pitch = torpedoPingPitch;
                        audioSource.Play();
                        audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        break;

                    case RWRThreatTypes.MissileLock:
                    case RWRThreatTypes.MWS:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = (missileLockSound);
                        audioSource.Play();
                        audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        break;
                    case RWRThreatTypes.None:
                        break;
                    default:
                        if (!audioSource.isPlaying)
                        {
                            audioSource.clip = (radarPingSound);
                            audioSource.Play();
                            audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        }
                        break;
                }
            }
        }

        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !BDArmorySetup.GAME_UI_ENABLED ||
                !vessel.isActiveVessel || !displayRWR) return;
            if (audioSourceRepeatDelay > 0)
                audioSourceRepeatDelay -= Time.fixedDeltaTime;

            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }

            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectRwr.position);
            BDArmorySetup.WindowRectRwr = GUI.Window(94353, BDArmorySetup.WindowRectRwr, WindowRwr, "Radar Warning Receiver", GUI.skin.window);
            GUIUtils.UseMouseEventInRect(RwrDisplayRect);
        }

        internal void WindowRwr(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, BDArmorySetup.WindowRectRwr.width - 18, 30));
            if (GUI.Button(new Rect(BDArmorySetup.WindowRectRwr.width - 18, 2, 16, 16), "X", GUI.skin.button))
            {
                displayRWR = false;
                BDArmorySetup.SaveConfig();
            }
            GUI.BeginGroup(new Rect(BorderSize / 2, HeaderSize + (BorderSize / 2), RwrDisplayRect.width, RwrDisplayRect.height));
            //GUI.DragWindow(RwrDisplayRect);

            GUI.DrawTexture(RwrDisplayRect, VesselRadarData.omniBgTexture, ScaleMode.StretchToFill, false);
            float pingSize = 32 * BDArmorySettings.RWR_WINDOW_SCALE;

            for (int i = 0; i < pingsData.Length; i++)
            {
                TargetSignatureData currPing = pingsData[i];
                Vector2 pingPosition = currPing.pingPosition;
                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);

                if (!currPing.exists) continue;
                if (currPing.signalType == RWRThreatTypes.MissileLock || currPing.signalType == RWRThreatTypes.MWS)
                {
                    GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
                }
                else
                {
                    GUI.DrawTexture(pingRect, rwrDiamondTexture, ScaleMode.StretchToFill, true);
                    GUI.Label(pingRect, iconLabels[(int)currPing.signalType], rwrIconLabelStyle);
                }
            }

            // Tell the compiler to not worry about bounds checking
            for (int i = 0; i < MWSData.Length; i++)
            {
                // Actual end of for loop
                if (i + 1 > MWSSlots) break;
                TargetSignatureData currPing = MWSData[i];
                Vector2 pingPosition = currPing.pingPosition;
                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);

                GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
            }

            List<TargetSignatureData>.Enumerator lw = launchWarnings.GetEnumerator();
            while (lw.MoveNext())
            {
                Vector2 pingPosition = lw.Current.pingPosition;
                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));

                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);
                GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
            }
            lw.Dispose();
            GUI.EndGroup();

            // Resizing code block.
            RWRresizeRect =
                new Rect(BDArmorySetup.WindowRectRwr.width - 18, BDArmorySetup.WindowRectRwr.height - 18, 16, 16);
            GUI.DrawTexture(RWRresizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && RWRresizeRect.Contains(Event.current.mousePosition))
            {
                resizingWindow = true;
            }

            if (Event.current.type == EventType.Repaint && resizingWindow)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    float diff = (Mathf.Abs(Mouse.delta.x) > Mathf.Abs(Mouse.delta.y) ? Mouse.delta.x : Mouse.delta.y) / BDArmorySettings.UI_SCALE_ACTUAL;
                    BDArmorySettings.RWR_WINDOW_SCALE = Mathf.Clamp(BDArmorySettings.RWR_WINDOW_SCALE + diff / RwrSize, BDArmorySettings.RWR_WINDOW_SCALE_MIN, BDArmorySettings.RWR_WINDOW_SCALE_MAX);
                    BDArmorySetup.ResizeRwrWindow(BDArmorySettings.RWR_WINDOW_SCALE);
                }
            }
            // End Resizing code.

            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectRwr);
        }

        public static void PingRWR(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            if (OnRadarPing != null)
            {
                OnRadarPing(v, source, type, persistTime, vSource);
            }
        }

        public static void PingRWR(Ray ray, float fov, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            using (var vessel = FlightGlobals.Vessels.GetEnumerator())
                while (vessel.MoveNext())
                {
                    if (vessel.Current == null || !vessel.Current.loaded) continue;
                    if (VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.Current.vesselType)) continue;
                    Vector3 dirToVessel = vessel.Current.CoM - ray.origin;
                    if (VectorUtils.Angle(ray.direction, dirToVessel) < fov * 0.5f)
                    {
                        PingRWR(vessel.Current, ray.origin, type, persistTime, vSource);
                    }
                }
        }

        public static void WarnMissileLaunch(Vector3 source, Vector3 direction, bool radarMissile, Vessel vSource)
        {
            OnMissileLaunch?.Invoke(source, direction, radarMissile, vSource);
        }
    }
}
