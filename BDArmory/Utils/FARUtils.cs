using System;
using System.Reflection;
using UnityEngine;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class FerramAerospace : MonoBehaviour
    {
        public static FerramAerospace Instance;
        public static bool hasFAR = false;
        private static bool hasCheckedForFAR = false;
        public static bool hasFARWing = false;
        public static bool hasFARControllableSurface = false;
        private static bool hasCheckedForFARWing = false;
        private static bool hasCheckedForFARControllableSurface = false;

        public static Assembly FARAssembly;
        public static Type FARWingModule;
        public static Type FARControllableSurfaceModule;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new FerramAerospace();
        }

        void Start()
        {
            CheckForFAR();
            if (hasFAR)
            {
                CheckForFARWing();
                CheckForFARControllableSurface();
            }
        }

        public static bool CheckForFAR()
        {
            if (hasCheckedForFAR) return hasFAR;
            hasCheckedForFAR = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.StartsWith("FerramAerospaceResearch"))
                {
                    FARAssembly = assy.assembly;
                    hasFAR = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR Assembly: {FARAssembly.FullName}");
                }
            }
            return hasFAR;
        }

        public static bool CheckForFARWing()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARWing) return hasFARWing;
            hasCheckedForFARWing = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARWingAerodynamicModel")
                {
                    FARWingModule = type;
                    hasFARWing = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR wing module type.");
                }
            }
            return hasFARWing;
        }

        public static bool CheckForFARControllableSurface()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARControllableSurface) return hasFARControllableSurface;
            hasCheckedForFARControllableSurface = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARControllableSurface")
                {
                    FARControllableSurfaceModule = type;
                    hasFARControllableSurface = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found FAR controllable surface module type.");
                }
            }
            return hasFARControllableSurface;
        }

        public static float GetFARMassMult(Part part)
        {
            if (!hasFARWing) return 1;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var massMultiplier = (float)FARWingModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found wing Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var massMultiplier = (float)FARControllableSurfaceModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found ctrl. srf. Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
            }
            return 1;
        }
    }

    public class ProceduralWing : MonoBehaviour
    {
        public static ProceduralWing Instance;
        public static bool hasB9ProcWing = false;
        private static bool hasCheckedForB9PW = false;
        public static bool hasPwingModule = false;
        private static bool hasCheckedForPwingModule = false;

        public static Assembly PWAssembly;
        public static Type PWType;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new ProceduralWing();
        }

        void Start()
        {
            CheckForB9ProcWing();
            if (hasB9ProcWing) CheckForPWModule();
        }

        public static bool CheckForB9ProcWing()
        {
            if (hasCheckedForB9PW) return hasB9ProcWing;
            hasCheckedForB9PW = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.Contains("B9") && assy.assembly.FullName.Contains("PWings")) // Not finding 'if (assy.assembly.FullName.StartsWith("B9-PWings-Fork"))'?
                {
                    PWAssembly = assy.assembly;
                    hasB9ProcWing = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found Pwing Assembly: {PWAssembly.FullName}");
                }
            }
            return hasB9ProcWing;
        }

        public static bool CheckForPWModule()
        {
            if (!hasB9ProcWing) return false;
            if (hasCheckedForPwingModule) return hasPwingModule;
            hasCheckedForPwingModule = true;
            foreach (var type in PWAssembly.GetTypes())
            {
                //Debug.Log($"[BDArmory.FARUtils]: Found module " + type.Name);

                if (type.Name == "WingProcedural")
                {
                    PWType = type;
                    hasPwingModule = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found Pwing module.");
                }
            }
            return hasPwingModule;
        }

        public static float GetPWingVolume(Part part)
        {
            if (!hasPwingModule)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: hasPwing check failed!");
                return -1;
            }

            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType || module.GetType().IsSubclassOf(PWType))
                {
                    if (module.GetType() == PWType)
                    {
                        bool WingctrlSrf = (bool)PWType.GetField("isWingAsCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        bool ctrlSrf = (bool)PWType.GetField("isCtrlSrf", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        float length = (float)PWType.GetField("sharedBaseLength", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                        float width = ((float)PWType.GetField("sharedBaseWidthRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseWidthTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                        float thickness = ((float)PWType.GetField("sharedBaseThicknessRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseThicknessTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module));
                        if (thickness > 0.36f) //0.18 * 2
                            thickness = (Mathf.Max(0.36f, ((Mathf.Log(thickness * 0.5f) + 1) * 0.75f))); 
                            //thickness = 0.36f; //thickness doesn't add to pwing mass, so why should it add to HP? 
                            //- because edge lift don't contribute to HP anymore
                            //look into making thickness add mass?
                            //-that seems like a change that really should be part of pwings proper, not bolted on here, even if it really would help balance out pwings...
                        float aeroVolume = (0.7f * length * width * thickness) / 4;
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found volume of {aeroVolume} for {part.name}.");
						if (BDArmorySettings.RUNWAY_PROJECT && !ctrlSrf) //if RunwayProject and part !controlsurface, remove lift/mass from edges to bring inline with stock boards
						{
                            float liftCoeff = (length * (width / 2)) / 3.52f;
                            PWType.GetField("stockLiftCoefficient", BindingFlags.Public | BindingFlags.Instance).SetValue(module, liftCoeff); //adjust PWing GUI lift readout
                            part.Modules.GetModule<ModuleLiftingSurface>().deflectionLiftCoeff = (length * (width / 2) / 3.52f); //adjust lift to be inline with stock wings
                            if (!WingctrlSrf)
                                PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, liftCoeff / 10); //Adjust PWing GUI mass readout
                            else
                                PWType.GetField("aeroUIMass", BindingFlags.Public | BindingFlags.Instance).SetValue(module, liftCoeff / 5); //this modifies the IPartMassModifier, so the mass will also change along with the GUI
                        }
                        //Pcontrol surfaces are more difficult; can easily be just an edge with span thickness = 0, so removing lift from these would render them essentially decorative and nothing else
                        //...add a child boxCollider...? Again, a change that really should be done in Pwings, not here
                        return aeroVolume;
                    }
                }
            }
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Pwing module not found!");
            return -1;
        }

        public static float GetPWingArea(Part part)
        {
            if (!hasPwingModule) return -1;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == PWType)
                {
                    var length = (float)PWType.GetField("sharedBaseLength", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    var width = ((float)PWType.GetField("sharedBaseWidthRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseWidthTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;
                    var thickness = ((float)PWType.GetField("sharedBaseThicknessRoot", BindingFlags.Public | BindingFlags.Instance).GetValue(module) + (float)PWType.GetField("sharedBaseThicknessTip", BindingFlags.Public | BindingFlags.Instance).GetValue(module)) / 2;
                    float area = (2 * (length * width)) + (2 * (width * thickness)) + (2 * (length * thickness));
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FARUtils]: Found wing area of {area}: {length} * {width} * {thickness} * 2 for {part.name}.");
                    if (thickness <= 0.25f) area /= 2; //for ~stock thickness wings, halve area to prevent to prevent double armor. Thicker wings/Wings ued as structural elements that can conceivably have other stuff inside them, treat as standard part for armor volume
                    return area;
                }
            }
            return -1;
        }
    }
}
