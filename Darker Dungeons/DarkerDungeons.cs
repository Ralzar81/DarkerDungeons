// Project:         Darker Dungeons mod for Daggerfall Unity (http://www.dfworkshop.net)
// Copyright:       Copyright (C) 2022 Ralzar
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Author:          Ralzar

using System;
using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;

namespace DarkerDungeons
{
    [FullSerializer.fsObject("v1")]
    public class DarkerDungeonsSaveData
    {
        public int DungeonID;
        public List<Vector3> DeactivatedLightsPos;
        public List<Vector3> DousedLightsPos;
    }

    public class DarkerDungeons : MonoBehaviour, IHasModSaveData
    {
        static Mod mod;
        static DarkerDungeons instance;
        static bool interiorLightAdjusting;
        static int autoDouse;

        static int dungeonID;
        static List<Vector3> deactivatedLightsPos;
        static List<Vector3> dousedLightsPos;
        static List<Vector3> deactivatedLightsPosSaved;
        static List<Vector3> dousedLightsPosSaved;
        static bool lightCheck = false;
        static bool savedLightCheck = false;
        static bool ttWarning = false;

        static PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
        static DaggerfallUnity dfUnity = DaggerfallUnity.Instance;

        static Shader LegacyShadersDiffuse = Shader.Find("Legacy Shaders/Diffuse");
        static Shader LegacyShadersVertexLit = Shader.Find("Legacy Shaders/VertexLit");

        public Type SaveDataType
        {
            get { return typeof(DarkerDungeonsSaveData); }
        }

        public object NewSaveData()
        {
            return new DarkerDungeonsSaveData
            {
                DungeonID = GameManager.Instance.PlayerGPS.CurrentMapID,
                DeactivatedLightsPos = new List<Vector3>(),
                DousedLightsPos = new List<Vector3>()
            };
        }

        public object GetSaveData()
        {
            return new DarkerDungeonsSaveData
            {
                DungeonID = dungeonID,
                DeactivatedLightsPos = deactivatedLightsPos,
                DousedLightsPos = dousedLightsPos
            };
        }

        public void RestoreSaveData(object saveData)
        {
            savedLightCheck = false;
            var torchTakerSaveData = (DarkerDungeonsSaveData)saveData;
            if (torchTakerSaveData.DousedLightsPos != null && torchTakerSaveData.DeactivatedLightsPos != null)
            {
                deactivatedLightsPosSaved = torchTakerSaveData.DeactivatedLightsPos;
                dousedLightsPosSaved = torchTakerSaveData.DousedLightsPos;
                if (deactivatedLightsPosSaved.Count > 0 || dousedLightsPosSaved.Count > 0)
                {
                    savedLightCheck = true;
                }

            }
        }

        private static void SyncDeactivatedLights(List<Vector3> savedLights)
        {
            Debug.Log("[Darker Dungeons] SyncDeactivatedLights");
            deactivatedLightsPos = new List<Vector3>();
            if (deactivatedLightsPos != null)
            {
                DaggerfallBillboard[] lightBillboards = (DaggerfallBillboard[])FindObjectsOfType(typeof(DaggerfallBillboard)); //Get all "light emitting objects" in the dungeon
                foreach (DaggerfallBillboard billBoard in lightBillboards)
                {
                    GameObject billBoardObj = billBoard.transform.gameObject;
                    Vector3 savedPos = savedLights.Find(x => x == billBoardObj.transform.position);
                    if (savedPos == billBoardObj.transform.position)
                    {
                        billBoardObj.SetActive(false);
                        deactivatedLightsPos.Add(billBoardObj.transform.position);
                    }
                    else
                        billBoardObj.SetActive(true);
                }
            }
        }

        private static void SyncDousedLights(List<Vector3> savedLights)
        {
            Debug.Log("[Darker Dungeons] SyncDousedLights");
            dousedLightsPos = new List<Vector3>();
            if (dousedLightsPos != null)
            {
                DaggerfallBillboard[] lightBillboards = (DaggerfallBillboard[])FindObjectsOfType(typeof(DaggerfallBillboard)); //Get all "light emitting objects" in the dungeon
                foreach (DaggerfallBillboard billBoard in lightBillboards)
                {
                    GameObject billBoardObj = billBoard.transform.gameObject;
                    Vector3 savedPos = savedLights.Find(x => x == billBoardObj.transform.position);
                    if (savedPos == billBoardObj.transform.position)
                        DouseLight(billBoardObj);
                    else
                        LightLight(billBoardObj);
                }
            }
        }

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            var go = new GameObject(mod.Title);
            instance = go.AddComponent<DarkerDungeons>();
            mod.SaveDataInterface = instance;

            int billboardRecord = 29;
            while (billboardRecord > -1)
            {
                if (billboardRecord != 8)
                    PlayerActivate.RegisterCustomActivation(mod, 210, billboardRecord, ActivateLight);
                billboardRecord--;
            }

            PlayerEnterExit.OnTransitionExterior += OnTransitionExterior_ListCleanup;
            PlayerEnterExit.OnTransitionDungeonExterior += OnTransitionExterior_ListCleanup;
        }

        void Awake()
        {
            ModSettings settings = mod.GetSettings();

            interiorLightAdjusting = settings.GetValue<bool>("HouseAmbientLightAdjustment", "MatchInteriorToDungeonLight");
            autoDouse = settings.GetValue<int>("AutoDousingOrRemoval", "AutoDouseOrRemove");
            Debug.Log("[Darker Dungeons] Mod setting MatchInteriorToDungeonLight = " + interiorLightAdjusting.ToString());
            Debug.Log("[Darker Dungeons] Mod setting AutoDouseOrRemove = " + autoDouse.ToString());

            Mod iil = ModManager.Instance.GetMod("Improved Interior Lighting");
            if (iil != null)
            {
                Debug.Log("[Darker Dungeons] Improved Interior Lighting is active");
            }
            else
            {
                PlayerEnterExit.OnTransitionDungeonInterior += RemoveVanillaLightSources;
                PlayerEnterExit.OnTransitionDungeonInterior += AddVanillaLightToLightSources;
                Debug.Log("[Darker Dungeons] Improved Interior Lighting is not active");
            }
            Mod tt = ModManager.Instance.GetMod("Torch Taker");
            if (tt != null)
                ttWarning = true;


            PlayerEnterExit.OnTransitionDungeonInterior += SetLightCheckFlag;
            if (interiorLightAdjusting)
            {
                PlayerAmbientLight ambientLights = (PlayerAmbientLight)FindObjectOfType(typeof(PlayerAmbientLight));
                ambientLights.InteriorNightAmbientLight = ambientLights.InteriorAmbientLight * (Mathf.Min(1.0f, DaggerfallUnity.Settings.DungeonAmbientLightScale + 0.1f));
                ambientLights.InteriorAmbientLight = ambientLights.InteriorAmbientLight * (Mathf.Min(1.0f, DaggerfallUnity.Settings.DungeonAmbientLightScale + 0.3f));
                //PlayerEnterExit.OnTransitionInterior += AdjustAmbientLight;
            }


            mod.IsReady = true;
        }

        void Update()
        {
            if (GameManager.Instance.IsPlayingGame())
            {
                if (savedLightCheck)
                {
                    savedLightCheck = false;
                    lightCheck = false;
                    if (deactivatedLightsPosSaved.Count > 0)
                    {
                        SyncDeactivatedLights(deactivatedLightsPosSaved);
                    }
                    if (dousedLightsPosSaved.Count > 0)
                    {
                        SyncDousedLights(dousedLightsPosSaved);
                    }
                }
                else if (lightCheck)
                {
                    savedLightCheck = false;
                    lightCheck = false;
                    DouseLights();
                }
                if (ttWarning)
                    DaggerfallUI.MessageBox("The Torch Taker mod is not compatible with Darker Dungeons. Please restart DFU and deactivate Torch Taker.", true);
            }
        }

        //private static void AdjustAmbientLight(PlayerEnterExit.TransitionEventArgs args)
        //{
        //    PlayerAmbientLight ambientLights = (PlayerAmbientLight)FindObjectOfType(typeof(PlayerAmbientLight));
        //    ambientLights.InteriorNightAmbientLight = ambientLights.InteriorAmbientLight * (Mathf.Min(1.0f, DaggerfallUnity.Settings.DungeonAmbientLightScale + 0.1f));
        //    ambientLights.InteriorAmbientLight = ambientLights.InteriorAmbientLight * (Mathf.Min(1.0f, DaggerfallUnity.Settings.DungeonAmbientLightScale + 0.3f));
        //}

        private static void RemoveVanillaLightSources(PlayerEnterExit.TransitionEventArgs args)
        {
            DungeonLightHandler[] dfLights = (DungeonLightHandler[])FindObjectsOfType(typeof(DungeonLightHandler)); //Get all dungeon lights in the scene
            for (int i = 0; i < dfLights.Length; i++)
            {
                if (dfLights[i].gameObject.name.StartsWith("DaggerfallLight [Dungeon]"))
                {
                    Destroy(dfLights[i].gameObject);
                }
            }
        }

        private static void AddVanillaLightToLightSources(PlayerEnterExit.TransitionEventArgs args)
        {
            DaggerfallBillboard[] lightBillboards = (DaggerfallBillboard[])FindObjectsOfType(typeof(DaggerfallBillboard)); //Get all "light emitting objects" in the dungeon
            foreach (DaggerfallBillboard billBoard in lightBillboards)
            {
                if (billBoard.Summary.Archive == 210)
                {
                    GameObject lightsNode = new GameObject("ImprovedDungeonLight");
                    lightsNode.transform.SetParent(billBoard.transform);

                    lightsNode.transform.localPosition = new Vector3(0, 0, 0);
                    Light newLight = lightsNode.AddComponent<Light>();
                    newLight.range = 6;
                }
            }
        }

        private static void AddTrigger(GameObject obj)
        {
            BoxCollider boxTrigger = obj.AddComponent<BoxCollider>();
            {
                boxTrigger.isTrigger = true;
            }
        }

        private static void SetLightCheckFlag(PlayerEnterExit.TransitionEventArgs args)
        {
            lightCheck = true;
        }


        private static void DouseLights()
        {
            deactivatedLightsPos = new List<Vector3>();
            dousedLightsPos = new List<Vector3>();
            if (autoDouse > 0)
            {
                bool humanoidDungeon = HumanoidDungeon();
                GameObject[] lightObjects = (GameObject[])FindObjectsOfType(typeof(GameObject));
                foreach (GameObject obj in lightObjects)
                {
                    if (obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210,") && !HumanoidsNear(obj.transform.position))
                    {
                        if (!humanoidDungeon)
                        {
                            if (autoDouse == 1 || DaggerfallWorkshop.Game.Utility.Dice100.SuccessRoll(50))
                                DouseLight(obj);
                            else
                                DeactivateLight(obj);
                        }
                        else if (humanoidDungeon && Vector3.Distance(GameManager.Instance.PlayerObject.transform.position, obj.transform.position) > 5)
                            DouseLight(obj);
                    }
                }
            }
            
            Debug.Log("[Darker Dungeons] " + deactivatedLightsPos.Count.ToString() + " in deactivatedLightsPos");
            Debug.Log("[Darker Dungeons] " + dousedLightsPos.Count.ToString() + " in dousedLightsPos");
        }


        private static void ActivateLight(RaycastHit hit)
        {
            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            PlayerActivateModes activateMode = GameManager.Instance.PlayerActivate.CurrentMode;
            GameObject lightObj = hit.transform.gameObject;
            int lightType = LightTypeInt(lightObj);
            string itemName = LightNameFromInt(lightType);
            if (lightType > 0)
            {
                if (!playerEnterExit.IsPlayerInside && lightType >= 4) //brazier, campfire or chandelier
                {
                    if (activateMode == PlayerActivateModes.Info)
                        DaggerfallUI.AddHUDText("You see a burning " + itemName + ".");
                    else
                        ModManager.Instance.SendModMessage("Climates & Calories", "campPopup", hit);
                }
                else if (dousedLightsPos.Find(x => x == lightObj.transform.position) != lightObj.transform.position)
                {
                    if (lightType == 1) //torch
                    {
                        if (activateMode == PlayerActivateModes.Steal)
                        {
                            if (lightObj.GetComponent<DaggerfallAction>() == null)
                            {
                                DaggerfallUnityItem TorchItem = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Torch);
                                TorchItem.currentCondition /= UnityEngine.Random.Range(2, 4);
                                playerEntity.Items.AddItem(TorchItem);
                                DeactivateLight(lightObj);
                                DaggerfallUI.AddHUDText("You take the " + itemName + ".");
                            }
                            else
                                DaggerfallUI.AddHUDText("The " + itemName + " is firmly stuck...");
                        }
                        else if (activateMode == PlayerActivateModes.Info)
                        {
                            DaggerfallUI.AddHUDText("You see a " + itemName + ".");
                        }
                        else
                        {
                            DouseLight(lightObj);
                            DaggerfallUI.AddHUDText("You douse the " + itemName + ".");
                        }
                    }
                    else if (lightType == 2) //candle
                    {
                        if (activateMode == PlayerActivateModes.Steal)
                        {
                            if (lightObj.GetComponent<DaggerfallAction>() == null)
                            {
                                DaggerfallUnityItem CandleItem = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Candle);
                                CandleItem.currentCondition /= UnityEngine.Random.Range(2, 4);
                                playerEntity.Items.AddItem(CandleItem);
                                DeactivateLight(lightObj);
                                DaggerfallUI.AddHUDText("You take the " + itemName + ".");
                            }
                            else
                                DaggerfallUI.AddHUDText("The " + itemName + " is firmly stuck...");
                        }
                        else if (activateMode == PlayerActivateModes.Info)
                        {
                            DaggerfallUI.AddHUDText("You see a " + itemName + ".");
                        }
                        else
                        {
                            DouseLight(lightObj);
                            DaggerfallUI.AddHUDText("You douse the " + itemName + ".");
                        }
                    }
                    else if (lightType == 3) //lantern
                    {
                        if (activateMode == PlayerActivateModes.Steal)
                        {
                            if (lightObj.GetComponent<DaggerfallAction>() == null)
                            {
                                DaggerfallUnityItem oilItem = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Oil);
                                playerEntity.Items.AddItem(oilItem);
                                DouseLight(lightObj);
                                DaggerfallUI.AddHUDText("You pour the oil out of the " + itemName + ".");
                            }
                            else
                                DaggerfallUI.AddHUDText("The " + itemName + " is firmly stuck...");
                        }
                        else if (activateMode == PlayerActivateModes.Info)
                        {
                            DaggerfallUI.AddHUDText("You see a " + itemName + ".");
                        }
                    }
                    else if (lightType >= 4) //brazier, campfire or chandelier
                    {
                        if (activateMode == PlayerActivateModes.Info)
                            DaggerfallUI.AddHUDText("You see a burning " + itemName + ".");
                        else
                            ModManager.Instance.SendModMessage("Climates & Calories", "campPopup", hit);
                    }

                }
                else
                {
                    if (activateMode != PlayerActivateModes.Info)
                    {
                        bool fuelAvailable = false;
                        if (lightType == 1) //torch
                        {
                            if (activateMode == PlayerActivateModes.Steal)
                            {
                                DaggerfallUnityItem TorchItem = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Torch);
                                TorchItem.currentCondition /= UnityEngine.Random.Range(2, 4);
                                playerEntity.Items.AddItem(TorchItem);
                                DeactivateLight(lightObj);
                                DaggerfallUI.AddHUDText("You take the " + itemName + ".");
                            }
                            else
                            {
                                DaggerfallUI.AddHUDText("You light the " + itemName + ".");
                                LightLight(lightObj);
                            }   
                        }
                        else if (lightType == 2) //candle
                        {
                            if (activateMode == PlayerActivateModes.Steal)
                            {
                                DaggerfallUnityItem CandleItem = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Candle);
                                CandleItem.currentCondition /= UnityEngine.Random.Range(2, 4);
                                playerEntity.Items.AddItem(CandleItem);
                                DeactivateLight(lightObj);
                                DaggerfallUI.AddHUDText("You take the " + itemName + ".");
                            }
                            else
                            {
                                DaggerfallUI.AddHUDText("You light the " + itemName + ".");
                                LightLight(lightObj);
                            }
                        }
                        else if (lightType == 3) //lantern
                        {
                            List<DaggerfallUnityItem> inventoryOil = playerEntity.Items.SearchItems(ItemGroups.UselessItems2, (int)UselessItems2.Oil);
                            foreach (DaggerfallUnityItem oilItem in inventoryOil)
                            {
                                if ((playerEntity.LightSource != oilItem) && !fuelAvailable)
                                {
                                    fuelAvailable = true;
                                    oilItem.stackCount -= 1;
                                    if (oilItem.stackCount <= 0)
                                        playerEntity.Items.RemoveItem(oilItem);
                                    DaggerfallUI.AddHUDText("You refill the " + itemName + ".");
                                    LightLight(lightObj);
                                }
                            }
                            if (!fuelAvailable)
                                DaggerfallUI.AddHUDText("You have no oil.");
                        }
                        else if (lightType >= 4) //brazier, campfire or chandelier
                        {
                            DaggerfallUI.AddHUDText("You light the " + itemName + ".");
                            LightLight(lightObj);
                        }
                        
                    }
                    else
                    {
                        DaggerfallUI.AddHUDText("You see an extinguished " + itemName + ".");
                    }
                }
            }
        }

        private static void DeactivateLight(GameObject lightObj)
        {
            lightObj.SetActive(false);
            deactivatedLightsPos.Add(lightObj.transform.position);
        }

        private static void DouseLight(GameObject lightObj)
        {
            int lightTypeInt = LightTypeInt(lightObj);
            if (lightTypeInt > 0)
            {
                if (lightObj.GetComponent<DaggerfallAction>() != null)
                {
                    Debug.Log("[Darker Dungeons] Avoided dousing trigger");
                }
                else if (lightObj != null)
                {
                    DaggerfallBillboard lightBillboard = lightObj.GetComponent<DaggerfallBillboard>();
                    if (lightBillboard.Summary.Record != 8)
                    {
                        if (lightBillboard != null)
                        {
                            lightBillboard.SetMaterial(540, lightBillboard.Summary.Record);
                            if (lightBillboard.Summary.Record >= 3 && lightBillboard.Summary.Record <= 5)
                            {
                                Debug.Log("Candle " + lightBillboard.Summary.Record);
                                Vector3 candleScale = new Vector3(0.5f, 0.5f, 1f);
                                lightBillboard.transform.gameObject.transform.localScale = candleScale;
                            }
                        }

                        ParticleSystem[] lightParticles = lightObj.GetComponentsInChildren<ParticleSystem>(true);
                        foreach (ParticleSystem lightParticle in lightParticles)
                        {
                            if (lightParticle != null)
                                lightParticle.transform.gameObject.SetActive(false);
                        }

                        Light[] lightLights = lightObj.GetComponentsInChildren<Light>(true);
                        foreach (Light lightLight in lightLights)
                        {
                            if (lightLight != null)
                                lightLight.transform.gameObject.SetActive(false);
                        }

                        MeshRenderer[] lightMeshs = lightObj.GetComponentsInChildren<MeshRenderer>(true);
                        foreach (MeshRenderer lightMesh in lightMeshs)
                        {
                            if (lightMesh != null)
                            {
                                Renderer lightMeshRend = lightMesh.transform.gameObject.GetComponent<Renderer>();
                                if (lightBillboard == null && lightMeshRend != null && lightMeshRend.material.shader != null && lightTypeInt == 1)
                                    lightMeshRend.material.shader = Shader.Find("Legacy Shaders/Diffuse");
                            }
                        }

                        Renderer lightRend = lightObj.GetComponent<Renderer>();
                        if (lightBillboard == null && lightRend != null && lightRend.material.shader != null && lightTypeInt == 1)
                            lightRend.material.shader = Shader.Find("Legacy Shaders/Diffuse");


                        if (lightObj.GetComponent<AudioSource>() != null)
                            lightObj.GetComponent<AudioSource>().mute = true;

                        if (lightObj.GetComponent<BoxCollider>() == null)
                            AddTrigger(lightObj);

                        dousedLightsPos.Add(lightObj.transform.position);
                    }
                }
            }
        }

        private static void LightLight(GameObject lightObj)
        {
            int lightTypeInt = LightTypeInt(lightObj);
            if (lightTypeInt > 0)
            {
                Debug.Log("position clicked = " + lightObj.transform.position.ToString());

                DaggerfallBillboard lightBillboard = lightObj.GetComponent<DaggerfallBillboard>();
                if (lightBillboard.Summary.Record != 8)
                {
                    if (lightBillboard != null)
                    {
                        if (lightBillboard.Summary.Record == 7)
                            lightBillboard.SetMaterial(210, 23);
                        else if (lightBillboard.Summary.Record == 10)
                            lightBillboard.SetMaterial(210, 11);
                        else
                            lightBillboard.SetMaterial(210, lightBillboard.Summary.Record);
                        if (lightBillboard.Summary.Record >= 3 && lightBillboard.Summary.Record <= 5)
                        {
                            Vector3 candleScale = new Vector3(1f, 1f, 1f);
                            lightBillboard.transform.gameObject.transform.localScale = candleScale;
                        }
                    }

                    ParticleSystem[] lightParticles = lightObj.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (ParticleSystem lightParticle in lightParticles)
                    {
                        if (lightParticle != null)
                            lightParticle.transform.gameObject.SetActive(true);
                    }

                    Light[] lightLights = lightObj.GetComponentsInChildren<Light>(true);
                    foreach (Light lightLight in lightLights)
                    {
                        if (lightLight != null)
                            lightLight.transform.gameObject.SetActive(true);
                    }

                    MeshRenderer[] lightMeshs = lightObj.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (MeshRenderer lightMesh in lightMeshs)
                    {
                        if (lightMesh != null)
                        {
                            Renderer lightMeshRend = lightMesh.transform.gameObject.GetComponent<Renderer>();
                            if (lightBillboard == null && lightMeshRend != null && lightMeshRend.material.shader != null && lightTypeInt == 1)
                                lightMeshRend.material.shader = Shader.Find("Legacy Shaders/VertexLit");
                        }
                    }

                    Renderer lightRend = lightObj.GetComponent<Renderer>();
                    if (lightBillboard == null && lightRend != null && lightRend.material.shader != null && lightTypeInt == 1)
                        lightObj.GetComponent<Renderer>().material.shader = Shader.Find("Legacy Shaders/VertexLit");

                    if (lightObj.GetComponent<AudioSource>() != null)
                        lightObj.GetComponent<AudioSource>().mute = false;

                    dousedLightsPos.Remove(lightObj.transform.position);
                }
            }
        }


        private static int LightTypeInt(GameObject obj)
        {
            //0 = not light item
            //1 = torch
            //2 = candle
            //3 = lamp or lantern
            //4 = brazier
            //5 = campfire
            //6 = chandelier
            if (obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=6]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=16]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=17]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=18]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=20]"))
                return 1;
            else if (
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=2]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=3]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=4]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=5]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=11]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=21]"))
                return 2;
            else if (
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=8]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=12]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=13]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=14]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=15]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=22]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=24]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=25]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=26]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=27]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=28]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=29]"))
                return 3;
            else if (
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=0]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=19]")
                )
                return 4;
            else if (obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=1]")
                )
                return 5;
            else if (
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=7]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=9]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=10]") ||
                obj.name.StartsWith("DaggerfallBillboard [TEXTURE.210, Index=23]"))
                return 6;
            else
                return 0;
        }

        private static string LightNameFromInt(int identifier)
        {
            switch (identifier)
            {
                case 1:
                    return "torch";
                case 2:
                    return "candle";
                case 3:
                    return "lamp";
                case 4:
                    return "brazier";
                case 5:
                    return "campfire";
                case 6:
                    return "chandelier";
                default:
                    return "light";

            }
        }

        private static bool HumanoidDungeon()
        {
            switch (GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.DungeonType)
            {
                case DFRegion.DungeonTypes.BarbarianStronghold:
                case DFRegion.DungeonTypes.HumanStronghold:
                case DFRegion.DungeonTypes.Laboratory:
                case DFRegion.DungeonTypes.OrcStronghold:
                case DFRegion.DungeonTypes.Prison:
                    return true;
            }

            return false;
        }

        private static bool HumanoidsNear(Vector3 lightPos)
        {
            float radius = 512 * MeshReader.GlobalScale;
            if (!HumanoidDungeon())
                radius /= 2;
            int enemyLayerMask = 1 << 11;
            bool humanoid = false;
            Collider[] hitColliders = Physics.OverlapSphere(lightPos, radius, enemyLayerMask);
            for (int i = 0; i < hitColliders.Length; i++)
            {
                DaggerfallEntityBehaviour entityBehaviour = hitColliders[i].transform.gameObject.GetComponent<DaggerfallEntityBehaviour>();
                if (entityBehaviour != null)
                {
                    EnemyEntity enemyEntity = entityBehaviour.Entity as EnemyEntity;
                    if (enemyEntity != null)
                    {
                        switch (enemyEntity.MobileEnemy.Team)
                        {
                            case MobileTeams.Centaurs:
                            case MobileTeams.CityWatch:
                            case MobileTeams.Criminals:
                            case MobileTeams.Daedra:
                            case MobileTeams.Giants:
                            case MobileTeams.KnightsAndMages:
                            case MobileTeams.Nymphs:
                            case MobileTeams.Orcs:
                                humanoid = true;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            return humanoid;
        }

        private static void OnTransitionExterior_ListCleanup(PlayerEnterExit.TransitionEventArgs args)
        {
            Debug.Log("[Darker Dungeons] OnTransitionExterior_ListCleanup event = " + args.ToString());
            if (deactivatedLightsPos != null)
                deactivatedLightsPos.Clear();
            if (dousedLightsPos != null)
                dousedLightsPos.Clear();
        }
    }
}