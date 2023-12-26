﻿using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Guidances
{
    public class MissileGuidance
    {
        public static Vector3 GetAirToGroundTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float descentRatio, float minSpeed = 200)
        {
            // Incorporate lead for target velocity
            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);
            float leadTime = Mathf.Clamp(targetDistance / (targetVelocity - currVel).magnitude, 0f, 8f);
            targetPosition += targetVelocity * leadTime;

            Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.CoM);
            //-FlightGlobals.getGeeForceAtPosition(targetPosition).normalized;
            Vector3 surfacePos = missileVessel.transform.position +
                                 Vector3.Project(targetPosition - missileVessel.transform.position, upDirection);
            //((float)missileVessel.altitude*upDirection);
            Vector3 targetSurfacePos;

            targetSurfacePos = targetPosition;

            float distanceToTarget = Vector3.Distance(surfacePos, targetSurfacePos);

            if (missileVessel.srfSpeed < 75 && missileVessel.verticalSpeed < 10)
            //gain altitude if launching from stationary
            {
                return missileVessel.transform.position + (5 * missileVessel.transform.forward) + (1 * upDirection);
            }

            float altitudeClamp = Mathf.Clamp(
                (distanceToTarget - ((float)missileVessel.srfSpeed * descentRatio)) * 0.22f, 0,
                (float)missileVessel.altitude);

            //Debug.Log("[BDArmory.MissileGuidance]: AGM altitudeClamp =" + altitudeClamp);
            Vector3 finalTarget = targetPosition + (altitudeClamp * upDirection.normalized);

            //Debug.Log("[BDArmory.MissileGuidance]: Using agm trajectory. " + Time.time);

            return finalTarget;
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vessel missileVessel, bool direct,
            out Vector3 finalTarget)
        {
            Vector3 up = VectorUtils.GetUpDirection(missileVessel.transform.position);
            Vector3 forward = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(up);
            float speed = (float)missileVessel.srfSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missileVessel.transform.position).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missileVessel.transform.position);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missileVessel.transform.position + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vector3 missilePosition,
            float missileSpeed, bool direct, out Vector3 finalTarget)
        {
            Vector3 up = VectorUtils.GetUpDirection(missilePosition);
            Vector3 forward = (targetPosition - missilePosition).ProjectOnPlanePreNormalized(up);
            float speed = missileSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missilePosition).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missilePosition);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missilePosition + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static Vector3 GetBeamRideTarget(Ray beam, Vector3 currentPosition, Vector3 currentVelocity,
            float correctionFactor, float correctionDamping, Ray previousBeam)
        {
            float onBeamDistance = Vector3.Project(currentPosition - beam.origin, beam.direction).magnitude;
            //Vector3 onBeamPos = beam.origin+Vector3.Project(currentPosition-beam.origin, beam.direction);//beam.GetPoint(Vector3.Distance(Vector3.Project(currentPosition-beam.origin, beam.direction), Vector3.zero));
            Vector3 onBeamPos = beam.GetPoint(onBeamDistance);
            Vector3 previousBeamPos = previousBeam.GetPoint(onBeamDistance);
            Vector3 beamVel = (onBeamPos - previousBeamPos) / Time.fixedDeltaTime;
            Vector3 target = onBeamPos + (500f * beam.direction);
            Vector3 offset = onBeamPos - currentPosition;
            offset += beamVel * 0.5f;
            target += correctionFactor * offset;

            Vector3 velDamp = correctionDamping * (currentVelocity - beamVel).ProjectOnPlanePreNormalized(beam.direction);
            target -= velDamp;

            return target;
        }

        public static Vector3 GetAirToAirTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact, float minSpeed = 200)
        {
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;

            leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            return targetPosition + (targetVelocity * leadTime);
        }

        public static Vector3 GetKappaTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, MissileLauncher ml, float thrust, float shapingAngle, out float ttgo, out float gLimit, float minSpeed = 200f)
        {
            Vector3 velDirection = ml.vessel.srf_vel_direction;

            float R = Vector3.Distance(targetPosition, ml.vessel.transform.position);

            float currSpeed = Mathf.Max((float)ml.vessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            float leadTime = R / (targetVelocity - currVel).magnitude;
            leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            //Vector3 Rdir = (targetPosition - ml.vessel.transform.position).normalized;
            //float ttgoInv = R/Vector3.Dot(targetVelocity - currVel, Rdir);
            ttgo = ml.vessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration);
            float ttgoInv = 1 / ttgo;

            float qS = (float)(0.5f * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * ml.liftArea;

            // Need to be changed if the lift curves are changed
            float Lalpha = 2.864788975654117f * qS * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float D0 = 0.00215f * qS * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;
            float eta = 0.025f * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER / BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;

            float TL = thrust / Lalpha;

            Vector3 upDirection = VectorUtils.GetUpDirection(ml.vessel.CoM);

            Vector3 accel;

            Vector3 predictedImpactPoint = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);

            Vector3 planarDirectionToTarget = ((predictedImpactPoint - ml.vessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;

            float K1;
            float K2;

            if (thrust > 0)
            {
                

                float F2sqr = Lalpha*(thrust-D0)*(TL*TL + 1f)*(TL*TL+1f)/ ((float)(ml.vessel.totalMass * ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * (2 * eta + TL));
                float F2 = Mathf.Sqrt(F2sqr);

                float sinF2R = Mathf.Sin(F2 * R);
                float cosF2R = Mathf.Cos(F2 * R);

                K1 = F2 * R * (sinF2R - F2 * R) / (2f - 2f * cosF2R - F2 * R * sinF2R);
                K2 = F2sqr * R * R * (1f - cosF2R) / (2f - 2f * cosF2R - F2 * R * sinF2R);
            }
            else
            {
                float Fsqr = D0 * Lalpha * (thrust + 1) * (thrust + 1) / ((float)(ml.vessel.totalMass * ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * (2 * eta + thrust));
                float F = Mathf.Sqrt(Fsqr);

                float eFR = Mathf.Exp(F * R);
                float enFR = Mathf.Exp(-F * R);

                K1 = (2f * Fsqr * R * R - F * R * (eFR - enFR)) / (eFR * (F * R - 2f) - enFR * (F * R + 2f) + 4f);
                K2 = (Fsqr * R * R * (eFR + enFR - 2f)) / (eFR * (F * R - 2f) - enFR * (F * R + 2f) + 4f);
            }

            accel = (K1 * ttgoInv) * (currSpeed * (Mathf.Cos(shapingAngle) * planarDirectionToTarget - Mathf.Sin(shapingAngle) * upDirection) - currVel) + (K2 * ttgoInv) * (predictedImpactPoint - ml.vessel.transform.position - currVel * ttgo);
            gLimit = accel.magnitude;
            return ml.vessel.CoM + currVel * ttgo + accel * ttgo * ttgo;
        }

        public static Vector3 GetAirToAirLoftTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float targetAlt, float maxAltitude,
            float rangeFactor, float vertVelComp, float velComp, float loftAngle, float termAngle,
            float termDist, ref int loftState, out float timeToImpact, out float gLimit,
            out float targetDistance, MissileBase.GuidanceModes homingModeTerminal, float N,
            float minSpeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            float currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            //Vector3 Rdir = (targetPosition - missileVessel.transform.position).normalized;
            //float rDot = Vector3.Dot(targetVelocity - currVel, Rdir);

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            //float leadTime = (targetDistance / rDot);

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            gLimit = -1f;

            // If loft is not terminal
            if ((targetDistance > termDist) && (loftState < 3))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                // Get up direction
                Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.CoM);

                // Use the gun aim-assist logic to determine ballistic angle (assuming no drag)
                Vector3 missileRelativePosition, missileRelativeVelocity, missileAcceleration, missileRelativeAcceleration, targetPredictedPosition, missileDropOffset, lastVelDirection, ballisticTarget, targetHorVel, targetCompVel;

                var firePosition = missileVessel.transform.position; //+ (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime). Not offsetting by part vel gives the correct initial placement.
                missileRelativePosition = targetPosition - firePosition;
                float timeToCPA = timeToImpact; // Rough initial estimate.
                targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, timeToCPA);

                // Velocity Compensation Logic
                float compMult = Mathf.Clamp(0.5f * (targetDistance - termDist) / termDist, 0f, 1f);
                Vector3 velDirectionHor = (velDirection.ProjectOnPlanePreNormalized(upDirection)).normalized; //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                targetHorVel = targetVelocity.ProjectOnPlanePreNormalized(upDirection); //targetVelocity - upDirection * Vector3.Dot(targetVelocity, upDirection); // Get target horizontal velocity (relative to missile frame)
                float targetAlVelMag = Vector3.Dot(targetHorVel, velDirectionHor); // Get magnitude of velocity aligned with the missile velocity vector (in the horizontal axis)
                targetAlVelMag *= Mathf.Sign(velComp) * compMult;
                targetAlVelMag = Mathf.Max(targetAlVelMag, 0f); //0.5f * (targetAlVelMag + Mathf.Abs(targetAlVelMag)); // Set -ve velocity (I.E. towards the missile) to 0 if velComp is +ve, otherwise for -ve

                float targetVertVelMag = Mathf.Max(0f, Mathf.Sign(vertVelComp) * compMult * Vector3.Dot(targetVelocity, upDirection));

                //targetCompVel = targetVelocity + velComp * targetHorVel.magnitude* targetHorVel.normalized; // Old velComp logic
                //targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor; // New velComp logic
                targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor + vertVelComp * targetVertVelMag * upDirection; // New velComp logic

                var count = 0;
                do
                {
                    lastVelDirection = velDirection;
                    currVel = currSpeed * velDirection;
                    //firePosition = missileVessel.transform.position + (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime).
                    missileAcceleration = FlightGlobals.getGeeForceAtPosition((firePosition + targetPredictedPosition) / 2f); // Drag is ignored.
                    //bulletRelativePosition = targetPosition - firePosition + compMult * altComp * upDirection; // Compensate for altitude
                    missileRelativePosition = targetPosition - firePosition; // Compensate for altitude
                    missileRelativeVelocity = targetVelocity - currVel;
                    missileRelativeAcceleration = targetAcceleration - missileAcceleration;
                    timeToCPA = AIUtils.TimeToCPA(missileRelativePosition, missileRelativeVelocity, missileRelativeAcceleration, timeToImpact * 3f);
                    targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetCompVel, targetAcceleration, timeToCPA);
                    missileDropOffset = -0.5f * missileAcceleration * timeToCPA * timeToCPA;
                    ballisticTarget = targetPredictedPosition + missileDropOffset;
                    velDirection = (ballisticTarget - missileVessel.transform.position).normalized;
                } while (++count < 10 && Vector3.Angle(lastVelDirection, velDirection) > 1f); // 1° margin of error is sufficient to prevent premature firing (usually)


                // Determine horizontal and up components of velocity, calculate the elevation angle
                float velUp = Vector3.Dot(velDirection, upDirection);
                float velForwards = (velDirection - upDirection * velUp).magnitude;
                float angle = Mathf.Atan2(velUp, velForwards);

                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Loft Angle: [{(angle * Mathf.Rad2Deg):G3}]");

                // Use simple lead compensation to minimize over-compensation
                // Get planar direction to target
                Vector3 planarDirectionToTarget =
                    ((AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime) - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;

                // Check if termination angle agrees with termAngle
                if ((angle > -termAngle * Mathf.Deg2Rad) && (loftState < 2))
                {
                    /*// If not yet at termination, simple lead compensation
                    targetPosition += targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;

                    // Get planar direction to target
                    Vector3 planarDirectionToTarget = //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                        ((targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;*/

                    // Altitude clamp based on rangeFactor and maxAlt, cannot be lower than target
                    float altitudeClamp = Mathf.Clamp(targetAlt + rangeFactor * Vector3.Dot(targetPosition - missileVessel.transform.position, planarDirectionToTarget), targetAlt, Mathf.Max(maxAltitude, targetAlt));

                    // Old loft climb logic, wanted to limit turn. Didn't work well but leaving it in if I decide to fix it
                    /*if (missileVessel.altitude < (altitudeClamp - 0.5f))
                    //gain altitude if launching from stationary
                    {*/
                    //currSpeed = (float)missileVessel.Velocity().magnitude;

                    // 5g turn, v^2/r = a, v^2/(dh*(tan(45°/2)sin(45°))) > 5g, v^2/(tan(45°/2)sin(45°)) > 5g * dh, I.E. start turning when you need to pull a 5g turn,
                    // before that the required gs is lower, inversely proportional
                    /*if (loftState == 1 || (currSpeed * currSpeed * 0.2928932188134524755991556378951509607151640623115259634116f) >= (5f * (float)PhysicsGlobals.GravitationalAcceleration) * (altitudeClamp - missileVessel.altitude))
                    {*/
                    /*
                    loftState = 1;

                    // Calculate upwards and forwards velocity components
                    velUp = Vector3.Dot(missileVessel.Velocity(), upDirection);
                    velForwards = (float)(missileVessel.Velocity() - upDirection * velUp).magnitude;

                    // Derivation of relationship between dh and turn radius
                    // tan(theta/2) = dh/L, sin(theta) = L/r
                    // tan(theta/2) = sin(theta)/(1+cos(theta))
                    float turnR = (float)(altitudeClamp - missileVessel.altitude) * (currSpeed * currSpeed + currSpeed * velForwards) / (velUp * velUp);

                    float accel = Mathf.Clamp(currSpeed * currSpeed / turnR, 0, 5f * (float)PhysicsGlobals.GravitationalAcceleration);
                    */

                    // Limit climb angle by turnFactor, turnFactor goes negative when above target alt
                    float turnFactor = (float)(altitudeClamp - missileVessel.altitude) / (4f * (float)missileVessel.srfSpeed);
                    turnFactor = Mathf.Clamp(turnFactor, -1f, 1f);

                    loftAngle = Mathf.Max(loftAngle, angle);

                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: AAM Loft altitudeClamp: [{altitudeClamp:G6}] COS: [{Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], SIN: [{Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], turnFactor: [{turnFactor:G3}].");
                    return missileVessel.transform.position + (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad) * upDirection));

                    /*
                    Vector3 newVel = (velForwards * planarDirectionToTarget + velUp * upDirection);
                    //Vector3 accVec = Vector3.Cross(newVel, Vector3.Cross(upDirection, planarDirectionToTarget));
                    Vector3 accVec = accel*(Vector3.Dot(newVel, planarDirectionToTarget) * upDirection - Vector3.Dot(newVel, upDirection) * planarDirectionToTarget).normalized;

                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * newVel + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec;
                    */
                    /*}
                    return missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * Mathf.Deg2Rad) * upDirection));
                    */
                    //}

                    //Vector3 finalTarget = missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * planarDirectionToTarget + ((altitudeClamp - (float)missileVessel.altitude) * upDirection.normalized);

                    //return finalTarget;
                }
                else
                {
                    loftState = 2;

                    // Tried to do some kind of pro-nav method. Didn't work well, leaving it just in case I want to fix it.
                    /*
                    Vector3 newVel = (float)missileVessel.srfSpeed * velDirection;
                    Vector3 accVec = (newVel - missileVessel.Velocity());
                    Vector3 unitVel = missileVessel.Velocity().normalized;
                    accVec = accVec - unitVel * Vector3.Dot(unitVel, accVec);

                    float accelTime = Mathf.Clamp(timeToImpact, 0f, 4f);

                    accVec = accVec / accelTime;

                    float accel = accVec.magnitude;

                    if (accel > 20f * (float)PhysicsGlobals.GravitationalAcceleration)
                    {
                        accel = 20f * (float)PhysicsGlobals.GravitationalAcceleration / accel;
                    }
                    else
                    {
                        accel = 1f;
                    }

                    Debug.Log("[BDArmory.MissileGuidance]: Loft: Diving, accel = " + accel);
                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * missileVessel.Velocity() + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec * accel;
                    */

                    Vector3 finalTargetPos;

                    if (velUp > 0f)
                    {
                        // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                        /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                            velDirection.y - upDirection.y * velUp,
                            velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                        finalTargetPos = missileVessel.transform.position + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    } else
                    {
                        // Otherwise just fly towards the target according to velUp and velForwards
                        finalTargetPos = missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velUp * upDirection.x + velForwards * planarDirectionToTarget.x,
                        velUp * upDirection.y + velForwards * planarDirectionToTarget.y,
                        velUp * upDirection.z + velForwards * planarDirectionToTarget.z);
                    }

                    // If the target is at <  2 * termDist start mixing
                    if (targetDistance < 2 * termDist)
                    {
                        float dummy;

                        if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                            return (1f - (targetDistance - termDist) / termDist) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out dummy) + ((targetDistance - termDist) / termDist) * finalTargetPos;
                        else if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                            return (1f - (targetDistance - termDist) / termDist) * GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out dummy) + ((targetDistance - termDist) / termDist) * finalTargetPos;
                        else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                            return (1f - (targetDistance - termDist) / termDist) * targetPosition + ((targetDistance - termDist) / termDist) * finalTargetPos;
                        else
                            return (1f - (targetDistance - termDist) / termDist) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out dummy) + ((targetDistance - termDist) / termDist) * finalTargetPos; // Default to PN
                    }

                    // No mixing if targetDistance > 2 * termDist
                    return finalTargetPos;

                    //if (velUp > 0f)
                    //{
                    //    // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                    //    /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                    //        velDirection.y - upDirection.y * velUp,
                    //        velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                    //    return missileVessel.transform.position + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    //}

                    //// Otherwise just fly towards the target according to velUp and velForwards
                    ////return missileVessel.transform.position + (float)missileVessel.srfSpeed *  velDirection;
                    //return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velUp * upDirection.x + velForwards * planarDirectionToTarget.x,
                    //    velUp * upDirection.y + velForwards * planarDirectionToTarget.y,
                    //    velUp * upDirection.z + velForwards * planarDirectionToTarget.z);
                }
            }
            else
            {
                // If terminal just go straight for target + lead
                loftState = 3;
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Terminal");

                if (targetDistance < termDist)
                {
                    if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                        return GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit);
                    else if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                        return GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out gLimit);
                    else if (homingModeTerminal == MissileBase.GuidanceModes.AAMLead)
                        return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
                    else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                        return targetPosition;
                    else
                        return GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit); // Default to PN
                }
                else
                {
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                    //return targetPosition + targetVelocity * leadTime;
                }
            }
        }

/*        public static Vector3 GetAirToAirHybridTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float termDist, out float timeToImpact,
            MissileBase.GuidanceModes homingModeTerminal, float N, float minSpeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            float currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            if (targetDistance < termDist)
            {
                if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                    return GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                    return GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                    return targetPosition;
                else
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
            }
            else
            {
                return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                                                                                                                                        //return targetPosition + targetVelocity * leadTime;
            }
        }*/

        public static Vector3 GetAirToAirTargetModular(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact)
        {
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            //Basic lead time calculation
            Vector3 currVel = missileVessel.Velocity();
            timeToImpact = targetDistance / (targetVelocity - currVel).magnitude;

            // Calculate time to CPA to determine target position
            float timeToCPA = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 16f);
            timeToImpact = (timeToCPA < 16f) ? timeToCPA : timeToImpact;
            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp((timeToCPA - 5f))) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            return AIUtils.PredictPosition(targetPosition, targetVelocity * easeVel, targetAcceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.
        }

        public static Vector3 GetPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }

        public static Vector3 GetAPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            // float tgo = relRange.magnitude / relVelocity.magnitude;
            Vector3 accelBias = Vector3.Cross(relRange.normalized, targetAcceleration);
            accelBias = Vector3.Cross(RefVector, accelBias);
            normalAccel -= 0.5f * N * accelBias;
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }
        public static float GetLOSRate(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 LOSRate = Mathf.Rad2Deg * RotVector;
            return LOSRate.magnitude;
        }

        /// <summary>
        /// Air-2-Air fire solution used by the AI for steering, WM checking if a missile can be launched, unguided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetVessel"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel)
        {
            if (!targetVessel)
            {
                return missile.transform.position + (missile.GetForwardTransform() * 1000);
            }
            Vector3 targetPosition = targetVessel.transform.position;
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetVessel.transform.position, missile.transform.position);

            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
    
            Vector3 vel = missile.vessel.Velocity();
            Vector3 VelOpt = missile.GetForwardTransform() * (launcher != null ? launcher.optimumAirspeed : 1500);
            float accel = launcher != null ? (launcher.thrust / missile.part.mass) : modLauncher != null ? (modLauncher.thrust/modLauncher.mass) : 10;
            Vector3 deltaVel = targetVessel.Velocity() - vel;
            Vector3 DeltaOptvel = targetVessel.Velocity() - VelOpt;
            float T = Mathf.Clamp(Vector3.Project(VelOpt - vel, missile.GetForwardTransform()).magnitude / accel, 0, 8); //time to optimal airspeed

            Vector3 relPosition = targetPosition - missile.transform.position;
            Vector3 relAcceleration = targetVessel.acceleration_immediate - missile.GetForwardTransform() * accel;
            leadTime = AIUtils.TimeToCPA(relPosition, deltaVel, relAcceleration, T); //missile accelerating, T is greater than our max look time of 8s
            if (T < 8 && leadTime == T)//missile has reached max speed, and is now cruising; sim positions ahead based on T and run CPA from there
            {
                relPosition = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration_immediate, T) -
                    AIUtils.PredictPosition(missile.transform.position, vel, missile.GetForwardTransform() * accel, T);
                relAcceleration = targetVessel.acceleration_immediate; // - missile.MissileReferenceTransform.forward * 0; assume missile is holding steady velocity at optimumAirspeed
                leadTime = AIUtils.TimeToCPA(relPosition, DeltaOptvel, relAcceleration, 8 - T) + T;
            }

            if (missile.vessel.atmDensity < 0.05 && missile.vessel.InOrbit()) // More accurate, but too susceptible to slight acceleration changes for use in-atmo
            {
                Vector3 relPos = targetPosition - missile.transform.position;
                Vector3 relVel = targetVessel.Velocity() - missile.vessel.Velocity();
                Vector3 relAcc = targetVessel.acceleration_immediate - (missile.vessel.acceleration_immediate + missile.GetForwardTransform() * accel);
                targetPosition = AIUtils.PredictPosition(relPos, relVel, relAcc, leadTime);
            }
            else // Not accurate enough for orbital speeds, but more resilient to acceleration changes
            {
                targetPosition += (Vector3)targetVessel.acceleration_immediate * 0.05f * leadTime * leadTime;
            }

            return targetPosition;
        }
        /// <summary>
        /// Air-2-Air lead offset calcualtion used for guided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetPosition"></param>
        /// <param name="targetVelocity"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vector3 targetPosition, Vector3 targetVelocity)
        {
            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
            float leadTime = 0;
            Vector3 leadPosition = targetPosition;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 leadDirection, velOpt;
            float accel = launcher != null ? launcher.thrust / missile.part.mass : modLauncher.thrust / modLauncher.mass;
            float leadTimeError = 1f;
            int count = 0;
            do
            {
                leadDirection = leadPosition - missile.transform.position;
                float targetDistance = leadDirection.magnitude;
                leadDirection.Normalize();
                velOpt = leadDirection * (launcher != null ? launcher.optimumAirspeed : 1500);
                float deltaVel = Vector3.Dot(targetVelocity - vel, leadDirection);
                float deltaVelOpt = Vector3.Dot(targetVelocity - velOpt, leadDirection);
                float T = Mathf.Clamp((velOpt - vel).magnitude / accel, 0, 8); //time to optimal airspeed, clamped to at most 8s
                float D = deltaVel * T + 1 / 2 * accel * (T * T); //relative distance covered accelerating to optimum airspeed
                leadTimeError = -leadTime;
                if (targetDistance > D) leadTime = (targetDistance - D) / deltaVelOpt + T;
                else leadTime = (-deltaVel - BDAMath.Sqrt((deltaVel * deltaVel) + 2 * accel * targetDistance)) / accel;
                leadTime = Mathf.Clamp(leadTime, 0f, 8f);
                leadTimeError += leadTime;
                leadPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime);
            } while (++count < 5 && Mathf.Abs(leadTimeError) > 1e-3f);  // At most 5 iterations to converge. Also, 1e-2f may be sufficient.
            return leadPosition;
        }

        public static Vector3 GetCruiseTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = VectorUtils.GetUpDirection(missileVessel.transform.position);
            float currentRadarAlt = GetRadarAltitude(missileVessel);
            float distanceSqr =
                (targetPosition - (missileVessel.transform.position - (currentRadarAlt * upDirection))).sqrMagnitude;

            Vector3 planarDirectionToTarget = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection).normalized;

            float error;

            if (currentRadarAlt > 1600)
            {
                error = 500000;
            }
            else
            {
                Vector3 tRayDirection = (planarDirectionToTarget * 10) - (10 * upDirection);
                Ray terrainRay = new Ray(missileVessel.transform.position, tRayDirection);
                RaycastHit rayHit;

                if (Physics.Raycast(terrainRay, out rayHit, 8000, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
                {
                    float detectedAlt =
                        Vector3.Project(rayHit.point - missileVessel.transform.position, upDirection).magnitude;

                    error = Mathf.Min(detectedAlt, currentRadarAlt) - radarAlt;
                }
                else
                {
                    error = currentRadarAlt - radarAlt;
                }
            }

            error = Mathf.Clamp(0.05f * error, -5, 3);
            return missileVessel.transform.position + (10 * planarDirectionToTarget) - (error * upDirection);
        }

        public static Vector3 GetTerminalManeuveringTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = -FlightGlobals.getGeeForceAtPosition(missileVessel.GetWorldPos3D()).normalized;
            Vector3 planarVectorToTarget = (targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection);
            Vector3 planarDirectionToTarget = planarVectorToTarget.normalized;
            Vector3 crossAxis = Vector3.Cross(planarDirectionToTarget, upDirection).normalized;
            float sinAmplitude = Mathf.Clamp(Vector3.Distance(targetPosition, missileVessel.transform.position) - 850, 0,
                4500);
            Vector3 sinOffset = (Mathf.Sin(1.25f * Time.time) * sinAmplitude * crossAxis);
            Vector3 targetSin = targetPosition + sinOffset;
            Vector3 planarSin = missileVessel.transform.position + planarVectorToTarget + sinOffset;

            Vector3 finalTarget;
            float finalDistance = 2500 + GetRadarAltitude(missileVessel);
            if ((targetPosition - missileVessel.transform.position).sqrMagnitude > finalDistance * finalDistance)
            {
                finalTarget = targetPosition;
            }
            else if (!GetBallisticGuidanceTarget(targetSin, missileVessel, true, out finalTarget))
            {
                //finalTarget = GetAirToGroundTarget(targetSin, missileVessel, 6);
                finalTarget = planarSin;
            }
            return finalTarget;
        }

        public static FloatCurve DefaultLiftCurve = null;
        public static FloatCurve DefaultDragCurve = null;

        const float TRatioInflec1 = 1.181181181181181f; // Thrust to Lift Ratio (at AoA of 30) where the maximum occurs
        // after the 65 degree mark
        const float TRatioInflec2 = 2.242242242242242f; // Thrust to Lift Ratio (at AoA of 30) where a local maximum no
        // longer exists, above this every section must be searched

        public static FloatCurve AoACurve = null; // Floatcurve containing AoA of (local) max acceleration
        // for a given thrust to lift (at the max CL of 1.5 at 30 degrees of AoA) ratio. Limited to a max
        // of TRatioInflec2 where a local maximum no longer exists

        public static FloatCurve AoAEqCurve = null; // Floatcurve containing AoA after which the acceleration goes above
        // that of the local maximums'. Only exists between TRatioInflec1 and TRatioInflec2.

        public static FloatCurve gMaxCurve = null; // Floatcurve containing max acceleration times the mass (total force)
        // normalized by q*S*GLOBAL_LIFT_MULTIPLIER for TRatio between 0 and TRatioInflec2. Note that after TRatioInflec1
        // this becomes a local maxima not a global maxima. This is used to narrow down what part of the curve we should
        // solve on.

        // Linearized CL v.s. AoA curve to enable fast solving. Algorithm performs bisection using the fast calculations of the bounds
        // and then performs a linear solve 
        public static float[] linAoA = null;
        public static float[] linCL = null;
        public static float[] linSin = null;
        public static float[] linSlope = null;
        public static float[] linIntc = null;

        public static float getGLimit(MissileLauncher ml, float thrust, float gLim, float margin)//, out bool gLimited)
        {
            bool gLimited = false;

            // Force required to reach g-limit
            gLim *= (float)(ml.vessel.totalMass * PhysicsGlobals.GravitationalAcceleration);

            float maxAoA = ml.maxAoA;

            float currAoA = maxAoA;

            int interval = 0;

            // Factor by which to multiply the lift coefficient to get lift, it's the dynamic pressure times the lift area times
            // the global lift multiplier
            float qSk = (float) (0.5f * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * ml.liftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;

            if (DefaultLiftCurve == null)
            {
                DefaultLiftCurve = new FloatCurve();
                DefaultLiftCurve.Add(0, 0);
                DefaultLiftCurve.Add(8, .35f);
                //	DefaultLiftCurve.Add(19, 1);
                //	DefaultLiftCurve.Add(23, .9f);
                DefaultLiftCurve.Add(30, 1.5f);
                DefaultLiftCurve.Add(65, .6f);
                DefaultLiftCurve.Add(90, .7f);
            }

            if (DefaultDragCurve == null)
            {
                DefaultDragCurve = new FloatCurve();
                DefaultDragCurve.Add(0, 0.00215f);
                DefaultDragCurve.Add(5, .00285f);
                DefaultDragCurve.Add(15, .007f);
                DefaultDragCurve.Add(29, .01f);
                DefaultDragCurve.Add(55, .3f);
                DefaultDragCurve.Add(90, .5f);
            }

            if (AoACurve == null)
            {
                AoACurve = new FloatCurve();
                AoACurve.Add(0.0000000000f, 30.0000000000f);
                AoACurve.Add(0.7107107107f, 33.9639639640f);
                AoACurve.Add(1.5315315315f, 39.6396396396f);
                AoACurve.Add(1.9419419419f, 43.6936936937f);
                AoACurve.Add(2.1421421421f, 46.6666666667f);
                AoACurve.Add(2.2122122122f, 48.3783783784f);
                AoACurve.Add(2.2422422422f, 49.7297297297f);
            }

            if (AoAEqCurve == null)
            {
                AoAEqCurve = new FloatCurve();
                AoAEqCurve.Add(1.1911911912f, 89.6396396396f);
                AoAEqCurve.Add(1.3413413413f, 81.6216216216f);
                AoAEqCurve.Add(1.5215215215f, 73.3333333333f);
                AoAEqCurve.Add(1.7217217217f, 67.4774774775f);
                AoAEqCurve.Add(1.9819819820f, 62.4324324324f);
                AoAEqCurve.Add(2.1821821822f, 56.6666666667f);
                AoAEqCurve.Add(2.2422422422f, 52.6126126126f);
            }

            if (gMaxCurve == null)
            {
                gMaxCurve = new FloatCurve();
                gMaxCurve.Add(0.0000000000f, 1.5000000000f);
                gMaxCurve.Add(1.2012012012f, 2.4907813293f);
                gMaxCurve.Add(1.9119119119f, 3.1757276995f);
                gMaxCurve.Add(2.2422422422f, 3.5307206802f);
            }

            if (linAoA == null)
            {
                linAoA = new float[] { 0f, 10f, 24f, 30f, 38f, 57f, 65f, 90f };
            }

            if (linCL == null)
            {
                linCL = new float[] { 0f, 0.454444597111092f, 1.34596044049850f, 1.5f, 1.38043381924198f, 0.719566180758018f, 0.6f, 0.7f };
            }

            if (linSin == null)
            {
                linSin = new float[] { 0f, 0.173648177666930f, 0.406736643075800f, 0.5f, 0.615661475325658f, 0.838670567945424f, 0.906307787036650f, 1f };
            }

            if (linSlope == null)
            {
                linSlope = new float[] { 0.0454444597111092f, 0.0636797030991005f, 0.0256732599169169f, -0.0149457725947522f, -0.0347825072886297f, -0.0149457725947522f, 0.004f };
            }

            if (linIntc == null)
            {
                linIntc = new float[] { 0f, -0.182352433879912f, 0.729802202492494f, 1.94837317784257f, 2.70216909620991f, 1.57147521865889f, 0.34f };
            }

            float currG = 0;

            if (thrust == 0)
            {
                if (gLim > 1.5f*qSk)
                {
                    gLimited = false;
                    return maxAoA;
                } else
                {
                    currG = linCL[2] * qSk; // CL(alpha)*qSk + thrust*sin(alpha)

                    if (currG < gLim)
                    {
                        interval = 2;
                    }
                    else
                    {
                        currG = linCL[1] * qSk;

                        if (currG > gLim)
                        {
                            interval = 0;
                        } else
                        {
                            interval = 1;
                        }
                    }

                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[interval], linIntc[interval], 0);

                    gLimited = currAoA < maxAoA;
                    return gLimited ? currAoA : maxAoA;
                }
            }
            else
            {
                float TRatio = thrust / (1.5f * qSk);

                int LHS = 0;
                int RHS = 7;

                if (TRatio < TRatioInflec2)
                {
                    currG = gMaxCurve.Evaluate(TRatio);

                    if (TRatio > TRatioInflec1)
                    {
                        margin = Mathf.Max(margin, 0f);

                        margin *= (float)ml.vessel.totalMass;

                        if (currG + margin < gLim)
                        {
                            if (currG < gLim)
                            {
                                float AoAMax = AoACurve.Evaluate(TRatio);
                                
                                if (AoAMax > linAoA[4])
                                {
                                    RHS = 5;
                                }
                                else if (AoAMax > linAoA[3])
                                {
                                    RHS = 4;
                                }
                                else
                                {
                                    RHS = 3;
                                }
                            }
                            else
                            {
                                currAoA = AoACurve.Evaluate(TRatio);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }
                        }
                        else
                        {
                            currG = 0.3f * qSk + thrust;

                            // If the absolute maximum g we can achieve is not enough, then return
                            // the local maximum in order to preserve energy
                            if (currG < gLim)
                            {
                                currAoA = AoACurve.Evaluate(TRatio);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }

                            float AoAEq = AoAEqCurve.Evaluate(TRatio);

                            if (AoAEq > linAoA[6])
                            {
                                currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[6], linIntc[6], 0);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }
                            else if (AoAEq > linAoA[5])
                            {
                                LHS = 5;
                            }
                            else
                            {
                                LHS = 4;
                            }
                        }
                    }
                    else
                    {
                        float AoAMax = AoACurve.Evaluate(TRatio);

                        if (currG < gLim)
                        {
                            if (AoAMax > linAoA[3])
                            {
                                RHS = 4;
                            }
                            else
                            {
                                RHS = 3;
                            }
                        }
                        else
                        {
                            gLimited = currAoA < maxAoA;
                            return gLimited ? currAoA : maxAoA;
                        }
                    }
                }

                while ( (RHS - LHS) > 1)
                {
                    interval = (int)(0.5f * (RHS + LHS));

                    currG = linCL[interval] * qSk + thrust * linSin[interval];

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: LHS: {LHS}, RHS: {RHS}, interval: {interval}, currG: {currG}, gLim: {gLim}");

                    if (currG < gLim)
                    {
                        LHS = interval;
                    }
                    else
                    {
                        RHS = interval;
                    }
                }

                if (LHS < 2)
                {
                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[LHS], linIntc[LHS], 0);
                }
                else
                {
                    currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[LHS], linIntc[LHS], 0);
                }

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Final Interval: {LHS}, currAoA: {currAoA}, gLim: {gLim}");

                gLimited = currAoA < maxAoA;
                return gLimited ? currAoA : maxAoA;
            }

            // If T = 0
            // We know it's in the first section. If m*gReq > (1.5*q*k*s) then set to min of maxAoA and 30 (margin?). If
            // < then we first make linear estimate, then solve by bisection of intervals first -> solve on interval.
            // If TRatio < TRatioInflec2
            // First we check the endpoints -> both gMax, and, if TRatio > TRatioInflec1, then 0.3*q*S*k + T (90 degree case).
            // If gMax > m*gReq then the answer < AoACurve -> Determine where it is via calculating the pre-calculated points
            // then seeing which one has gCalc > m*gReq, using the interval bounded by the point with gCalc > m*gReq on the
            // right end. Use bisection -> we know it's bounded at the RHS by the 38 or the 57 section. We can compare the
            // AoACurve with 38, if > 38 then use 57 as the bound, otherwise bisection with 38 as the bound. Using this to
            // determine which interval we're looking at, we then calc AoACalc. Return the min of maxAoA and AoACalc.
            // If gMax < m*gReq, then if TRatio < TRatioInflec1, set to min of AoACurve and maxAoA. If TRatio > TRatioInflec1
            // then we look at the 0.3*q*S*k + T. If < m*gReq then we'll set it to the min of maxAoA and either AoACurve or
            // 90, depends on the margin. See below. If > m*gReq then it's in the last two sections, bound by AoAEq on the LHS.
            // If AoAEq > 65, then we solve on the last section. If AoAEq < 65 then we check the point at AoA = 65 using the
            // pre-calculated values. If > m*gReq then we know that it's in the 57-65 section, otherwise we know it's in the
            // 65-90 section.
            // Consider adding a margin, if gMax only misses m*gReq by a little we should probably avoid going to the higher
            // angles as it adds a lot of drag. Maybe distance based? User settable?
            // If TRatio > TRatioInflec2 then we have a continuously monotonically increasing function
            // We use the fraction m*gReq/(0.3*q*S*k + T) to determine along which interval we should solve, noting that this
            // is an underestimate of the thrust required. (Maybe use arcsin for a more accurate estimate? Costly.) Then simply
            // calculate the pre-calculated value at the next point -> bisection and solve on the interval.
            
            // For all cases, if AoA < 15 then we can use the linear approximation of sin, if an interval includes both AoA < 15
            // and AoA > 15 then try < 15 (interval 2) first, then if > 15 try the non-linear starting from 15. Otherwise we use
            // non-linear equation.
        }

        public static float calcAoAforGLinear(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            return Mathf.Rad2Deg * (mg - CLintc * qSk) / (CLalpha * qSk + thrust);
        }

        public static float calcAoAforGNonLin(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            CLalpha *= qSk;

            return Mathf.Rad2Deg * (2f * CLalpha + Mathf.PI * thrust + 2f * Mathf.Sqrt(CLalpha * CLalpha + Mathf.PI * thrust * CLalpha + 2f * thrust * (CLintc * qSk + thrust - mg))) / (2f * thrust);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA)
        {
            if (DefaultLiftCurve == null)
            {
                DefaultLiftCurve = new FloatCurve();
                DefaultLiftCurve.Add(0, 0);
                DefaultLiftCurve.Add(8, .35f);
                //	DefaultLiftCurve.Add(19, 1);
                //	DefaultLiftCurve.Add(23, .9f);
                DefaultLiftCurve.Add(30, 1.5f);
                DefaultLiftCurve.Add(65, .6f);
                DefaultLiftCurve.Add(90, .7f);
            }

            if (DefaultDragCurve == null)
            {
                DefaultDragCurve = new FloatCurve();
                DefaultDragCurve.Add(0, 0.00215f);
                DefaultDragCurve.Add(5, .00285f);
                DefaultDragCurve.Add(15, .007f);
                DefaultDragCurve.Add(29, .01f);
                DefaultDragCurve.Add(55, .3f);
                DefaultDragCurve.Add(90, .5f);
            }

            FloatCurve liftCurve = DefaultLiftCurve;
            FloatCurve dragCurve = DefaultDragCurve;

            return DoAeroForces(ml, targetPosition, liftArea, steerMult, previousTorque, maxTorque, maxAoA, liftCurve,
                dragCurve);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA, FloatCurve liftCurve, FloatCurve dragCurve)
        {
            Rigidbody rb = ml.part.rb;
            if (rb == null || rb.mass == 0) return Vector3.zero;
            double airDensity = ml.vessel.atmDensity;
            double airSpeed = ml.vessel.srfSpeed;
            Vector3d velocity = ml.vessel.Velocity();

            //temp values
            Vector3 CoL = new Vector3(0, 0, -1f);
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;

            //lift
            float AoA = Mathf.Clamp(Vector3.Angle(ml.transform.forward, velocity.normalized), 0, 90);
            if (AoA > 0)
            {
                double liftForce = 0.5 * airDensity * airSpeed * airSpeed * liftArea * liftMultiplier * liftCurve.Evaluate(AoA);
                Vector3 forceDirection = -velocity.ProjectOnPlanePreNormalized(ml.transform.forward).normalized;
                rb.AddForceAtPosition((float)liftForce * forceDirection,
                    ml.transform.TransformPoint(ml.part.CoMOffset + CoL));
            }

            //drag
            if (airSpeed > 0)
            {
                double dragForce = 0.5 * airDensity * airSpeed * airSpeed * liftArea * dragMultiplier * dragCurve.Evaluate(AoA);
                rb.AddForceAtPosition((float)dragForce * -velocity.normalized,
                    ml.transform.TransformPoint(ml.part.CoMOffset + CoL));
            }

            //guidance
            if (airSpeed > 1 || (ml.vacuumSteerable && ml.Throttle > 0))
            {
                Vector3 targetDirection;
                float targetAngle;
                if (AoA < maxAoA)
                {
                    targetDirection = (targetPosition - ml.transform.position);
                    targetAngle = Mathf.Min(maxAoA,Vector3.Angle(velocity.normalized, targetDirection) * 4);
                }
                else
                {
                    targetDirection = velocity.normalized;
                    targetAngle = AoA;
                }

                Vector3 torqueDirection = -Vector3.Cross(targetDirection, velocity.normalized).normalized;
                torqueDirection = ml.transform.InverseTransformDirection(torqueDirection);

                float torque = Mathf.Clamp(targetAngle * steerMult, 0, maxTorque);
                Vector3 finalTorque = Vector3.Lerp(previousTorque, torqueDirection * torque, 1).ProjectOnPlanePreNormalized(Vector3.forward);

                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
            else
            {
                Vector3 finalTorque = Vector3.Lerp(previousTorque, Vector3.zero, 0.25f).ProjectOnPlanePreNormalized(Vector3.forward);
                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
        }

        public static float GetRadarAltitude(Vessel vessel)
        {
            float radarAlt = Mathf.Clamp((float)(vessel.mainBody.GetAltitude(vessel.CoM) - vessel.terrainAltitude), 0,
                (float)vessel.altitude);
            return radarAlt;
        }

        public static float GetRadarAltitudeAtPos(Vector3 position)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);

            float radarAlt = Mathf.Clamp(
                (float)(FlightGlobals.currentMainBody.GetAltitude(position) -
                         FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos)), 0,
                (float)FlightGlobals.currentMainBody.GetAltitude(position));
            return radarAlt;
        }

        public static float GetRaycastRadarAltitude(Vector3 position)
        {
            Vector3 upDirection = -FlightGlobals.getGeeForceAtPosition(position).normalized;

            float altAtPos = FlightGlobals.getAltitudeAtPos(position);
            if (altAtPos < 0)
            {
                position += 2 * Mathf.Abs(altAtPos) * upDirection;
            }

            Ray ray = new Ray(position, -upDirection);
            float rayDistance = FlightGlobals.getAltitudeAtPos(position);

            if (rayDistance < 0)
            {
                return 0;
            }

            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, rayDistance, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
            {
                return rayHit.distance;
            }
            else
            {
                return rayDistance;
            }
        }
    }
}
