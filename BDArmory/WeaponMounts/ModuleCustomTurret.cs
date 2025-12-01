using BDArmory.Extensions;
using BDArmory.Utils;
using BDArmory.Weapons;
using Expansions.Serenity;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.WeaponMounts
{
    public class ModuleCustomTurret : PartModule
    {
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_TurretID"),//Max Pitch
 UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float turretID;

        [KSPField] public string pitchTransformName = "TopJoint";
        public Transform pitchTransform;

        [KSPField] public string yawTransformName = "TopJoint";
        public Transform yawTransform;

        [KSPField] public string baseTransformName = "BottomJoint";
        public Transform bottomTransform;

        Transform referenceTransform; //set this to gun's fireTransform

        public float maxPitch = 0;
        public float minPitch = 0;
        public float maxYaw = 0;
        public float minYaw = 0;
        public bool fullRotation = false;

        [KSPField(isPersistant = true)] public float minPitchLimit = 400;
        [KSPField(isPersistant = true)] public float maxPitchLimit = 400;
        [KSPField(isPersistant = true)] public float yawRangeLimit = 400;

        ModuleRoboticServoHinge Hinge;
        ModuleRoboticRotationServo Servo;

        public Vector3 yawNormal;

        public bool isYawRotor => Servo != null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor) GameEvents.onEditorPartPlaced.Add(OnEditorPartPlaced);
            yawTransform = part.FindModelTransform(yawTransformName);            
            var hinge = part.FindModuleImplementing<ModuleRoboticServoHinge>();
            if (hinge != null)
            {
                Hinge = hinge;
                minPitch = Mathf.Min(hinge.softMinMaxAngles.x, hinge.softMinMaxAngles.y);
                maxPitch = Mathf.Max(hinge.softMinMaxAngles.x, hinge.softMinMaxAngles.y);

                pitchTransform = part.FindModelTransform(hinge.servoTransformName);
                bottomTransform = part.FindModelTransform(hinge.baseTransformName);
                if (!pitchTransform)
                {
                    Debug.LogWarning("[BDArmory.ModuleCustomTurret]: " + part.partInfo.title + " has no pitchTransform");
                }
                if (!bottomTransform)
                {
                    Debug.LogWarning("[BDArmory.ModuleCustomTurret]: " + part.partInfo.title + " has no bottomTransform");
                }
            }
            var servo = part.FindModuleImplementing<ModuleRoboticRotationServo>();
            if (servo != null)
            {
                Servo = servo;
                if (servo.allowFullRotation)
                    fullRotation = true;
                else
                {
                    minYaw = Mathf.Min(servo.softMinMaxAngles.x, servo.softMinMaxAngles.y);
                    maxYaw = Mathf.Max(servo.softMinMaxAngles.x, servo.softMinMaxAngles.y);
                }
                yawTransform = part.FindModelTransform(servo.servoTransformName);
                bottomTransform = part.FindModelTransform(servo.baseTransformName);
                if (!yawTransform)
                {
                    Debug.LogWarning("[BDArmory.ModuleCustomTurret]: " + part.partInfo.title + " has no yawTransform");
                }
                if (!bottomTransform)
                {
                    Debug.LogWarning("[BDArmory.ModuleCustomTurret]: " + part.partInfo.title + " has no bottomTransform");
                }
            }
            if (!referenceTransform)
            {
                if (pitchTransform)
                    SetReferenceTransform(pitchTransform);
                else if (yawTransform)
                    SetReferenceTransform(yawTransform);
                else
                    Debug.LogWarning("[BDArmory.ModuleCustomTurret]: " + part.partInfo.title + " has no referenceTransform");
            }
            if (!bottomTransform) bottomTransform = part.transform;

            yawNormal = yawTransform.up;
            //because ofc Squad can't have consistant standard for servo/hinge axis transform orientation...
            //Also need to account for rotation/facing; ModuleTurret is agnostic, but targetAngle in the hinge module is not.
            if (Hinge)
            {
                yawNormal = Hinge.mainAxis switch
                {
                    "X" => -pitchTransform.forward,
                    "Z" => pitchTransform.right,
                    _ => yawTransform.up
                };
            }
        }

        public void AimToTarget(Vector3 targetPosition, bool pitch = true, bool yaw = true)
        {
            AimInDirection(targetPosition - referenceTransform.position);
        }

        public void AimInDirection(Vector3 targetDirection)
        {
            if ((Servo && !yawTransform) || (Hinge && !pitchTransform))
            {
                return;
            }
            if (!bottomTransform) return;

            Vector3 yawComponent = targetDirection.ProjectOnPlanePreNormalized(yawNormal);
            Vector3 pitchComponent = targetDirection.ProjectOnPlane(Vector3.Cross(yawComponent, yawNormal));
            //float currentYaw = Hinge ? 0 : Servo ? Servo.currentAngle : 0; //currentAngle for whatever reason only updates when the PAW is open. WTF, KSP.
            float currentYaw = Servo ? VectorUtils.SignedAngleDP(bottomTransform.forward, referenceTransform.forward, bottomTransform.right) : 0;
            float yawError = VectorUtils.SignedAngleDP(
                referenceTransform.forward.ProjectOnPlanePreNormalized(yawNormal),
                yawComponent,
                Vector3.Cross(yawNormal, referenceTransform.forward));
            float targetYawAngle = (currentYaw + yawError).ToAngle();
            // clamp target yaw in a non-wobbly way
            if (fullRotation)
            {
                if (Mathf.Abs(targetYawAngle) > 180)
                {
                    var nonWobblyWay = Vector3.Dot(yawTransform.parent.right, targetDirection + referenceTransform.position - yawTransform.position);
                    //if (float.IsNaN(nonWobblyWay)) return;

                    targetYawAngle = 180 * Math.Sign(nonWobblyWay);
                }
            }
            else
            {
                targetYawAngle = Mathf.Clamp(targetYawAngle, minYaw, maxYaw); // clamp yaw
            }
            if (!fullRotation && Mathf.Abs(currentYaw - targetYawAngle) >= 180)
            {
                //if (float.IsNaN(currentYaw)) return;
                targetYawAngle = currentYaw - (Math.Sign(currentYaw) * 179);
            }
            if (Servo)
            {
                Servo.targetAngle = targetYawAngle;
                if (Servo.inverted) Servo.targetAngle *= -1;
                Debug.Log($"[CUSTOMTURRET] CurrYaw: {currentYaw}; YawError: {yawError}; Servo Yaw: {Servo.currentAngle}; Servo target Angle {Servo.targetAngle}");
            }
            if (Hinge)
            {
                float pitchError = (float)VectorUtils.SignedAngleDP(pitchComponent, yawNormal, Hinge.mainAxis == "X" ? pitchTransform.right : pitchTransform.forward) - (float)VectorUtils.SignedAngleDP(referenceTransform.forward, yawNormal, Hinge.mainAxis == "X" ? pitchTransform.right : pitchTransform.forward);
                //float currentPitch = Hinge ? Hinge.currentAngle : Servo ? 0 : 0;
                float currentPitch = Hinge ? VectorUtils.SignedAngleDP(Hinge.mainAxis == "X" ? bottomTransform.up : bottomTransform.right, referenceTransform.forward, bottomTransform.forward) : Servo ? 0 : 0;
                float targetPitchAngle = (currentPitch + pitchError).ToAngle();
                targetPitchAngle = Mathf.Clamp(targetPitchAngle, minPitch, maxPitch); // clamp pitch
                Debug.Log($"[CUSTOMTURRET] CurrPitch: {currentPitch}; PitchError: {pitchError}; Hinge Pitch: {Hinge.currentAngle}; Hinge target Angle {Hinge.targetAngle}");
                Hinge.targetAngle = targetPitchAngle;
            }
        }

        public bool ReturnTurret()
        {
            if ((Servo && !yawTransform) || (Hinge && !pitchTransform))
            {
                return false;
            }

            if (!(Hinge || Servo))
                return true;

            if (Servo) Servo.targetAngle = 0;
            if (Hinge) Hinge.targetAngle = 0;

            return true;
        }

        public void SetReferenceTransform(Transform t)
        {
            referenceTransform = t;
        }
        void OnEditorPartPlaced(Part p = null)
        {
            if (part.children.Count == 0) return;
            FindChildGuns(part.children); 
        }
        private void FindChildGuns(List<Part> children)
        {
            using (List<Part>.Enumerator child = children.GetEnumerator())
                while (child.MoveNext())
                {
                    if (child.Current == null) continue;
                    if (child.Current.IsWeapon())
                    {
                        var gun = child.Current.FindModuleImplementing<ModuleWeapon>();
                        if (gun != null)
                        gun.Fields["customTurretID"].guiActiveEditor = true;
                    }
                    //only have gun's TurrID slider appear if a child of the turret servo to reduce PAW clutter

                    if (child.Current.children.Count > 0)
                    {
                        FindChildGuns(child.Current.children);
                    }
                }
        }
        void OnDestroy()
        {
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlaced);
        }
        /*
        void OnGUI()
        {
            if ((Servo && !yawTransform) || (Hinge && !pitchTransform)) return;
            /*
            Vector3 fwdPos = referenceTransform.position + (5 * referenceTransform.forward);
            GUIUtils.DrawLineBetweenWorldPositions(referenceTransform.position, fwdPos, 4, Color.blue);

            Vector3 upPos = referenceTransform.position + (5 * referenceTransform.up);
            GUIUtils.DrawLineBetweenWorldPositions(referenceTransform.position, upPos, 4, Color.green);

            Vector3 rightPos = referenceTransform.position + (5 * referenceTransform.right);
            GUIUtils.DrawLineBetweenWorldPositions(referenceTransform.position, rightPos, 4, Color.red);

            Vector3 yawNrm = yawTransform.position + (10 * yawTransform.up);
            if (Hinge)
            {
                if (Hinge.mainAxis == "X") yawNrm = pitchTransform.position + (10 * -pitchTransform.forward);
                if (Hinge.mainAxis == "Z") yawNrm = pitchTransform.position + (10 * pitchTransform.right);
            }
            GUIUtils.DrawLineBetweenWorldPositions(yawTransform.position, yawNrm, 2, Color.white);            
        }
        */
    }
}